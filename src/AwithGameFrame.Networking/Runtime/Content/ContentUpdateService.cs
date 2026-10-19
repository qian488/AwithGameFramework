using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking.Content
{
    /// <summary>Verified incremental files, resumable staging and atomic activation. Never overwrites a pinned version.</summary>
    public sealed class ContentUpdateService : IAsyncDisposable
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates = new ConcurrentDictionary<string, SemaphoreSlim>(
            System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly string root;
        private readonly HttpClient http;
        private readonly SemaphoreSlim gate;
        private readonly TimeSpan downloadTimeout;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly object sync = new object();
        private Task disposeTask;
        private bool closing;

        public ContentUpdateService(string rootDirectory, HttpMessageHandler handler = null, TimeSpan? downloadTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("An installation directory is required.", nameof(rootDirectory));
            root = System.IO.Path.GetFullPath(rootDirectory);
            if (root.Length > System.IO.Path.GetPathRoot(root).Length)
                root = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            this.downloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(60);
            if (this.downloadTimeout != Timeout.InfiniteTimeSpan &&
                (this.downloadTimeout <= TimeSpan.Zero || this.downloadTimeout.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
            gate = RootGates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
            http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
            http.Timeout = Timeout.InfiniteTimeSpan;
            System.IO.Directory.CreateDirectory(root);
        }

        public ContentSnapshot OpenCurrent()
        {
            lock (sync) { if (closing) throw new ObjectDisposedException(nameof(ContentUpdateService)); }
            var pointer = System.IO.Path.Combine(root, "active");
            return File.Exists(pointer) ? OpenVersion(ReadPointer(pointer)) : null;
        }

        public async Task<ContentSnapshot> UpdateAsync(ContentManifest manifest, Uri baseUri, CancellationToken cancellationToken = default)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (baseUri == null || !baseUri.IsAbsoluteUri || (baseUri.Scheme != "http" && baseUri.Scheme != "https") ||
                !baseUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal) || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
                throw new ArgumentException("Expected an HTTP(S) content directory URI ending in '/'.", nameof(baseUri));
            using var linked = Link(cancellationToken);
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                var destination = System.IO.Path.Combine(root, "versions", manifest.Fingerprint);
                if (!System.IO.Directory.Exists(destination))
                {
                    var staging = System.IO.Path.Combine(root, "staging", manifest.Fingerprint);
                    var partials = System.IO.Path.Combine(root, "partials", manifest.Fingerprint);
                    System.IO.Directory.CreateDirectory(staging);
                    System.IO.Directory.CreateDirectory(partials);
                    var previous = OpenCurrent();
                    foreach (var file in manifest.Files)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var target = System.IO.Path.Combine(staging, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        if (ValidFile(target, file)) continue;
                        var copied = false;
                        if (previous != null)
                        {
                            foreach (var old in previous.Manifest.Files)
                            {
                                if (!old.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) || old.Size != file.Size) continue;
                                var candidate = previous.GetPath(old.Key);
                                if (!ValidFile(candidate, file)) continue;
                                File.Copy(candidate, target, overwrite: true);
                                copied = true;
                                break;
                            }
                        }
                        if (!copied)
                        {
                            var partial = System.IO.Path.Combine(partials, ContentManifest.ComputeHash(Encoding.UTF8.GetBytes(file.Path)) + ".part");
                            var relative = string.Join("/", Array.ConvertAll(file.Path.Split('/'), Uri.EscapeDataString));
                            await DownloadAsync(new Uri(baseUri, relative), file, partial, linked.Token).ConfigureAwait(false);
                            if (File.Exists(target)) File.Delete(target); // Only uncommitted staging is replaced.
                            File.Move(partial, target);
                        }
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    File.WriteAllBytes(System.IO.Path.Combine(staging, "content-manifest.json"), manifest.ToJson());
                    ValidateDirectory(staging, manifest);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                    System.IO.Directory.Move(staging, destination);
                }
                var snapshot = OpenVersion(manifest.Fingerprint);
                linked.Token.ThrowIfCancellationRequested();
                CommitPointer(manifest.Fingerprint);
                return snapshot;
            }
            finally { gate.Release(); }
        }

        /// <summary>Verify and switch back to the preceding committed version; existing snapshots stay valid.</summary>
        public async Task<ContentSnapshot> RollbackAsync(CancellationToken cancellationToken = default)
        {
            using var linked = Link(cancellationToken);
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                var backup = System.IO.Path.Combine(root, "active.bak");
                if (!File.Exists(backup)) throw new InvalidOperationException("No preceding content version.");
                var fingerprint = ReadPointer(backup);
                var snapshot = OpenVersion(fingerprint);
                linked.Token.ThrowIfCancellationRequested();
                CommitPointer(fingerprint);
                return snapshot;
            }
            finally { gate.Release(); }
        }

        private async Task DownloadAsync(Uri uri, ContentFile file, string partial, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (downloadTimeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(downloadTimeout);
            try { await DownloadCoreAsync(uri, file, partial, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
            { throw new TimeoutException("Content download timed out: " + file.Key, error); }
        }

        private async Task DownloadCoreAsync(Uri uri, ContentFile file, string partial, CancellationToken token)
        {
            if (ValidFile(partial, file)) return;
            var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            // A complete invalid partial must start over; a truncated partial can resume against the manifest hash.
            if (offset >= file.Size) { File.Delete(partial); offset = 0; }
            if (file.Size == 0) { File.WriteAllBytes(partial, Array.Empty<byte>()); if (!ValidFile(partial, file)) throw new InvalidDataException("Empty file hash mismatch."); return; }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException("Content download failed with HTTP " + (int)response.StatusCode + ".");
            if (response.StatusCode == HttpStatusCode.OK) offset = 0;
            else
            {
                var range = response.Content.Headers.ContentRange;
                if (range == null || range.Unit != "bytes" || range.From != offset || range.Length != file.Size || range.To != file.Size - 1)
                    throw new InvalidDataException("Unexpected HTTP content range.");
            }
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                output.Position = offset;
                var buffer = new byte[65536];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (read > file.Size - output.Position) throw new InvalidDataException("Content exceeds the declared length.");
                    await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (!ValidFile(partial, file)) throw new InvalidDataException("Content size or SHA-256 mismatch: " + file.Key);
        }

        private CancellationTokenSource Link(CancellationToken token)
        {
            lock (sync)
            {
                if (closing) throw new ObjectDisposedException(nameof(ContentUpdateService));
                return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            }
        }
        private ContentSnapshot OpenVersion(string fingerprint)
        {
            if (!ContentManifest.IsHash(fingerprint)) throw new InvalidDataException("Invalid active content pointer.");
            var directory = System.IO.Path.Combine(root, "versions", fingerprint);
            var manifest = ContentManifest.FromJson(File.ReadAllBytes(System.IO.Path.Combine(directory, "content-manifest.json")));
            if (!manifest.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest identity mismatch.");
            ValidateDirectory(directory, manifest);
            return new ContentSnapshot(directory, manifest);
        }
        private static void ValidateDirectory(string directory, ContentManifest manifest)
        {
            foreach (var file in manifest.Files)
                if (!ValidFile(System.IO.Path.Combine(directory, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)), file))
                    throw new InvalidDataException("Installed content is missing or corrupted: " + file.Key);
        }
        private static bool ValidFile(string path, ContentFile file) => File.Exists(path) && new FileInfo(path).Length == file.Size &&
            ContentManifest.HashFile(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
        private static string ReadPointer(string path)
        {
            // Keep readers compatible with atomic replacement on Windows.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Trim();
        }
        private void CommitPointer(string fingerprint)
        {
            var pointer = System.IO.Path.Combine(root, "active");
            if (File.Exists(pointer) && ReadPointer(pointer) == fingerprint) return;
            var temp = System.IO.Path.Combine(root, ".active-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(fingerprint);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(pointer)) File.Replace(temp, pointer, pointer + ".bak");
                else File.Move(temp, pointer);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (sync)
            {
                if (disposeTask != null) return new ValueTask(disposeTask);
                closing = true;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                disposeTask = completion.Task;
            }
            Exception cancellationError = null;
            try { lifetime.Cancel(); }
            catch (Exception error) { cancellationError = error; }
            _ = DisposeCoreAsync(completion, cancellationError);
            return new ValueTask(completion.Task);
        }
        private async Task DisposeCoreAsync(TaskCompletionSource<bool> completion, Exception cancellationError)
        {
            var errors = new List<Exception>();
            if (cancellationError != null) errors.Add(cancellationError);
            try
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    try { http.Dispose(); } catch (Exception error) { errors.Add(error); }
                    try { lifetime.Dispose(); } catch (Exception error) { errors.Add(error); }
                }
                finally { gate.Release(); }
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count == 0) completion.TrySetResult(true);
            else completion.TrySetException(new AggregateException("Content updater shutdown reported errors.", errors));
        }
    }
}
