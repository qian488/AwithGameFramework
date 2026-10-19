using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking.Content;
using AwithGameFrame.Core.Resources;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class ContentUpdateTests
    {
        private string directory;
        [SetUp] public void Setup() => directory = Path.Combine(Path.GetTempPath(), "AwithContentTests", Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        private static ContentManifest Manifest(string version, byte[] bytes, string path = "config/data.bin") =>
            new ContentManifest(version, new[] { new ContentFile("config", path, bytes.Length, Hash(bytes)) });

        private sealed class Handler : HttpMessageHandler
        {
            public byte[] Bytes = Encoding.UTF8.GetBytes("abcdefghij");
            public int Requests;
            public long? LastRange;
            public bool Truncate, IgnoreRange, InvalidRange, Block, ThrowOnCancel, Disposed;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests++;
                LastRange = null;
                if (request.Headers.Range != null)
                    foreach (var range in request.Headers.Range.Ranges) { LastRange = range.From; break; }
                if (Block)
                {
                    if (ThrowOnCancel) token.Register(() => throw new InvalidOperationException("Injected cancellation callback failure."));
                    await Task.Delay(Timeout.Infinite, token);
                }
                var start = IgnoreRange ? 0 : LastRange ?? 0;
                var body = new byte[(Truncate ? Bytes.Length / 2 : Bytes.Length) - (int)start];
                Array.Copy(Bytes, start, body, 0, body.Length);
                var response = new HttpResponseMessage(start == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(body) };
                if (start > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(InvalidRange ? start - 1 : start, Bytes.Length - 1, Bytes.Length);
                return response;
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }
        private static readonly Uri ContentUri = new Uri("https://content.example/");

        [TestCase("../escape")]
        [TestCase("/absolute")]
        [TestCase("dir\\file")]
        [TestCase("content-manifest.json/overwrite")]
        [TestCase("NUL.txt")]
        [TestCase("dir./a")]
        public void ManifestRejectsNonPortablePathsBeforeFileOperations(string path)
        {
            Assert.Throws<ArgumentException>(() => Manifest("1", Array.Empty<byte>(), path));
        }

        [Test]
        public void ManifestRoundTripAndConflictingPathsAreValidated()
        {
            var source = Manifest("1", new byte[] { 1, 2 });
            var decoded = ContentManifest.FromJson(source.ToJson());
            Assert.That(decoded.Fingerprint, Is.EqualTo(source.Fingerprint));
            var hash = Hash(Array.Empty<byte>());
            Assert.Throws<ArgumentException>(() => new ContentManifest("1", new[]
            {
                new ContentFile("a", "folder", 0, hash), new ContentFile("b", "folder/child", 0, hash)
            }));
            Assert.Throws<ArgumentException>(() => new ContentManifest("1", new[]
            {
                new ContentFile("a", "dir/A", 0, hash), new ContentFile("b", "dir/a", 0, hash)
            }));
        }

        [Test]
        public async Task ActivationIsIncrementalAndPreviousSnapshotSurvivesUpdateAndRollback()
        {
            var handler = new Handler();
            await using var service = new ContentUpdateService(directory, handler);
            var first = await service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri);
            var second = await service.UpdateAsync(Manifest("2", handler.Bytes, "new/path.bin"), ContentUri);
            Assert.That(handler.Requests, Is.EqualTo(1), "unchanged hashes should be copied from the verified previous installation");
            Assert.That(File.ReadAllBytes(first.GetPath("config")), Is.EqualTo(handler.Bytes));
            Assert.That(service.OpenCurrent().Manifest.Version, Is.EqualTo("2"));
            var rolledBack = await service.RollbackAsync();
            Assert.That(rolledBack.Manifest.Version, Is.EqualTo("1"));
            Assert.That(File.ReadAllBytes(second.GetPath("config")), Is.EqualTo(handler.Bytes));
        }

        [Test]
        public async Task CachedFileResourcesStayPinnedWhileNewContentVersionIsActivated()
        {
            var handler = new Handler();
            await using var updater = new ContentUpdateService(directory, handler);
            var original = (byte[])handler.Bytes.Clone();
            var first = await updater.UpdateAsync(Manifest("1", original), ContentUri);
            await using var resources = new ResourceStore(new FileResourceLoader(first.GetPath),
                cacheOptions: new ResourceCacheOptions(1024));
            await resources.PreloadAsync<byte[]>("config");
            handler.Bytes = Encoding.UTF8.GetBytes("updated data");
            var second = await updater.UpdateAsync(Manifest("2", handler.Bytes), ContentUri);
            await using (var scope = new ResourceScope(resources))
            {
                var lease = await scope.LoadAsync<byte[]>("config");
                Assert.That(lease.Value, Is.EqualTo(original));
            }
            await resources.TrimCacheAsync();
            await using (var scope = new ResourceScope(resources))
            {
                var lease = await scope.LoadAsync<byte[]>("config");
                Assert.That(lease.Value, Is.EqualTo(original), "cache eviction must not change the pinned resolver");
            }
            await using var nextResources = new ResourceStore(new FileResourceLoader(second.GetPath));
            await using var nextScope = new ResourceScope(nextResources);
            var next = await nextScope.LoadAsync<byte[]>("config");
            Assert.That(next.Value, Is.EqualTo(handler.Bytes));
        }

        [Test]
        public async Task TruncatedDownloadResumesFromVerifiedManifestWithoutPublishingPartialVersion()
        {
            var handler = new Handler { Truncate = true };
            await using var service = new ContentUpdateService(directory, handler);
            var manifest = Manifest("1", handler.Bytes);
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.UpdateAsync(manifest, ContentUri));
            Assert.That(service.OpenCurrent(), Is.Null);
            handler.Truncate = false;
            var snapshot = await service.UpdateAsync(manifest, ContentUri);
            Assert.That(handler.LastRange, Is.EqualTo(5));
            Assert.That(File.ReadAllBytes(snapshot.GetPath("config")), Is.EqualTo(handler.Bytes));
        }

        [Test]
        public async Task IgnoredRangeRestartsFileInsteadOfAppendingDuplicateBytes()
        {
            var handler = new Handler { Truncate = true };
            await using var service = new ContentUpdateService(directory, handler);
            var manifest = Manifest("1", handler.Bytes);
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.UpdateAsync(manifest, ContentUri));
            handler.Truncate = false;
            handler.IgnoreRange = true;
            var snapshot = await service.UpdateAsync(manifest, ContentUri);
            Assert.That(File.ReadAllBytes(snapshot.GetPath("config")), Is.EqualTo(handler.Bytes));
        }

        [Test]
        public async Task InvalidRangeAndWrongHashCannotReplaceActiveVersion()
        {
            var handler = new Handler();
            await using var service = new ContentUpdateService(directory, handler);
            await service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri);
            handler.Bytes = Encoding.UTF8.GetBytes("new bytes!");
            var newer = Manifest("2", handler.Bytes);
            handler.Truncate = true;
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.UpdateAsync(newer, ContentUri));
            handler.Truncate = false;
            handler.InvalidRange = true;
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.UpdateAsync(newer, ContentUri));
            Assert.That(service.OpenCurrent().Manifest.Version, Is.EqualTo("1"));
            handler.InvalidRange = false;
            await service.UpdateAsync(newer, ContentUri);
            var invalid = Manifest("3", Encoding.UTF8.GetBytes("mismatch!!"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.UpdateAsync(invalid, ContentUri));
            Assert.That(service.OpenCurrent().Manifest.Version, Is.EqualTo("2"));
        }

        [Test]
        public async Task ClosingCancelsInflightDownloadAndWaitsBeforeReleasingTransport()
        {
            var handler = new Handler { Block = true };
            var service = new ContentUpdateService(directory, handler);
            var pending = service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri);
            await service.DisposeAsync();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await pending);
            Assert.That(File.Exists(Path.Combine(directory, "active")), Is.False);
        }

        [Test]
        public void ThrowingCancellationCallbackStillClosesOwnedTransport()
        {
            var handler = new Handler { Block = true, ThrowOnCancel = true };
            var service = new ContentUpdateService(directory, handler);
            var pending = service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri);
            var closing = service.DisposeAsync().AsTask();
            Assert.ThrowsAsync<AggregateException>(async () => await closing);
            Assert.That(handler.Disposed, Is.True);
            Assert.That(service.DisposeAsync().AsTask(), Is.SameAs(closing));
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        }

        [Test]
        public async Task DownloadTimeoutDoesNotPublishPartialVersion()
        {
            var handler = new Handler { Block = true };
            await using var service = new ContentUpdateService(directory, handler, TimeSpan.FromMilliseconds(30));
            Assert.ThrowsAsync<TimeoutException>(async () => await service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri));
            Assert.That(service.OpenCurrent(), Is.Null);
        }

        [Test]
        public async Task EquivalentInstallationPathsShareTheSameWriterGate()
        {
            var blockedHandler = new Handler { Block = true };
            var nextHandler = new Handler();
            await using var first = new ContentUpdateService(directory, blockedHandler);
            await using var second = new ContentUpdateService(directory + Path.DirectorySeparatorChar, nextHandler);
            using var cancellation = new CancellationTokenSource();
            var blocked = first.UpdateAsync(Manifest("1", blockedHandler.Bytes), ContentUri, cancellation.Token);
            Assert.That(blockedHandler.Requests, Is.EqualTo(1));
            var pending = second.UpdateAsync(Manifest("2", nextHandler.Bytes), ContentUri);
            Assert.That(nextHandler.Requests, Is.Zero, "path spelling must not allow concurrent writers into the same staging directory");
            cancellation.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await blocked);
            var installed = await pending;
            Assert.That(installed.Manifest.Version, Is.EqualTo("2"));
        }

        [Test]
        public async Task CorruptedRollbackTargetIsRejectedWithoutChangingCurrentVersion()
        {
            var handler = new Handler();
            await using var service = new ContentUpdateService(directory, handler);
            var first = await service.UpdateAsync(Manifest("1", handler.Bytes), ContentUri);
            await service.UpdateAsync(Manifest("2", handler.Bytes), ContentUri);
            File.WriteAllText(first.GetPath("config"), "corrupted");
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.RollbackAsync());
            Assert.That(service.OpenCurrent().Manifest.Version, Is.EqualTo("2"));
        }
    }
}
