using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class VersionedSaveTests
    {
        private string directory;
        private ISerializer serializer;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AwithGameFrame.VersionedSaves", Guid.NewGuid().ToString("N"));
            serializer = new JsonSerializer();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Test]
        public async Task Save_ShouldReopenDataAndMetadataWithoutStaticPersistenceInitialization()
        {
            var service = NewService();
            var saved = await service.SaveAsync(3, new Progress { Level = 17 }, "boss entrance");
            var reopened = NewService();
            var result = await reopened.LoadAsync(3);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.Loaded));
                Assert.That(result.Data.Level, Is.EqualTo(17));
                Assert.That(result.Metadata.Description, Is.EqualTo("boss entrance"));
                Assert.That(result.Metadata.SavedAtUtc, Is.EqualTo(saved.SavedAtUtc));
                Assert.That(result.Metadata.SavedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(result.Metadata.SlotIndex, Is.EqualTo(3));
                Assert.That(result.Metadata.SchemaVersion, Is.EqualTo(1));
                Assert.That(result.WasMigrated, Is.False);
                Assert.That(Directory.GetFiles(directory, "*.save"), Has.Length.EqualTo(1));
            });
            Assert.That((await reopened.ListSlotsAsync()).Select(info => info.SlotIndex), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public async Task CorruptPrimary_ShouldRestorePreviousRevisionAndPersistRecoveryAcrossReopen()
        {
            var service = NewService();
            var previous = await service.SaveAsync(2, new Progress { Level = 8 }, "previous revision");
            await service.SaveAsync(2, new Progress { Level = 9 }, "new revision");
            var path = service.GetFilePath(2);
            var corrupted = File.ReadAllBytes(path);
            corrupted[corrupted.Length / 2] ^= 0x20;
            File.WriteAllBytes(path, corrupted);

            var recovered = await NewService().LoadAsync(2);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Status, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
                Assert.That(recovered.Data.Level, Is.EqualTo(8));
                Assert.That(recovered.Metadata.Description, Is.EqualTo("previous revision"));
                Assert.That(recovered.Metadata.SavedAtUtc, Is.EqualTo(previous.SavedAtUtc));
            });
            var reopened = await NewService().LoadAsync(2);
            Assert.That(reopened.Status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(reopened.Data.Level, Is.EqualTo(8));
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(File.ReadAllBytes(path)));
        }

        [Test]
        public async Task MissingPrimary_ShouldRecoverBackupButMissingOrBrokenCopiesRemainDistinct()
        {
            var service = NewService();
            Assert.That((await service.LoadAsync(1)).Status, Is.EqualTo(SaveLoadStatus.NotFound));
            await service.SaveAsync(1, new Progress { Level = 4 });
            await service.SaveAsync(1, new Progress { Level = 5 });
            File.Delete(service.GetFilePath(1));
            Assert.That((await service.LoadAsync(1)).Status, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));

            File.WriteAllText(service.GetFilePath(1), "truncated primary");
            File.WriteAllText(service.GetFilePath(1) + ".bak", "truncated backup");
            var error = Assert.ThrowsAsync<SaveCorruptedException>(async () => await service.LoadAsync(1));
            Assert.That(error.SlotIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task ExplicitSaveOverCorruptPrimary_ShouldKeepHealthyBackupAvailable()
        {
            var service = NewService();
            await service.SaveAsync(0, new Progress { Level = 3 }, "healthy backup");
            await service.SaveAsync(0, new Progress { Level = 4 });
            File.WriteAllText(service.GetFilePath(0), "corrupted primary");
            await service.SaveAsync(0, new Progress { Level = 5 });
            File.WriteAllText(service.GetFilePath(0), "another corrupted primary");

            var result = await NewService().LoadAsync(0);
            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
            Assert.That(result.Data.Level, Is.EqualTo(3));
            Assert.That(result.Metadata.Description, Is.EqualTo("healthy backup"));
        }

        [Test]
        public async Task MultiStepMigration_ShouldCommitNewSchemaAndOnlyRunOnceAfterReopen()
        {
            var old = new VersionedSaveService<OldProgress>(directory, serializer, 1);
            var saved = await old.SaveAsync(0, new OldProgress { Experience = 40 }, "old hero");
            var steps = new List<int>();
            var migrations = new[]
            {
                new SaveMigration(1, 2, async (bytes, token) =>
                {
                    steps.Add(1);
                    var value = await serializer.DeserializeAsync<OldProgress>(bytes);
                    return await serializer.SerializeAsync(new IntermediateProgress { Level = value.Experience / 10 });
                }),
                new SaveMigration(2, 3, async (bytes, token) =>
                {
                    steps.Add(2);
                    var value = await serializer.DeserializeAsync<IntermediateProgress>(bytes);
                    return await serializer.SerializeAsync(new Progress { Level = value.Level + 1 });
                })
            };
            var upgraded = new VersionedSaveService<Progress>(directory, serializer, 3, migrations);
            var result = await upgraded.LoadAsync(0);
            Assert.Multiple(() =>
            {
                Assert.That(steps, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(result.Data.Level, Is.EqualTo(5));
                Assert.That(result.WasMigrated, Is.True);
                Assert.That(result.Metadata.SchemaVersion, Is.EqualTo(3));
                Assert.That(result.Metadata.SavedAtUtc, Is.EqualTo(saved.SavedAtUtc));
                Assert.That(result.Metadata.Description, Is.EqualTo("old hero"));
            });
            var reopened = await new VersionedSaveService<Progress>(directory, serializer, 3).LoadAsync(0);
            Assert.That(reopened.Data.Level, Is.EqualTo(5));
            Assert.That(reopened.WasMigrated, Is.False);
            Assert.That(File.Exists(upgraded.GetFilePath(0) + ".bak"), Is.True);
        }

        [Test]
        public async Task FutureSchema_ShouldFailWithoutFallingBackToCompatibleBackupOrOverwritingIt()
        {
            var old = NewService();
            await old.SaveAsync(0, new Progress { Level = 1 });
            await new VersionedSaveService<Progress>(directory, serializer, 2).SaveAsync(0, new Progress { Level = 99 });
            var original = File.ReadAllBytes(old.GetFilePath(0));

            Assert.ThrowsAsync<SaveVersionException>(async () => await old.LoadAsync(0));
            Assert.ThrowsAsync<SaveVersionException>(async () => await old.SaveAsync(0, new Progress { Level = 2 }));
            Assert.That(File.ReadAllBytes(old.GetFilePath(0)), Is.EqualTo(original));
        }

        [Test]
        public async Task MissingOrFailedMigration_ShouldKeepOriginalAndNotRecoverBackup()
        {
            var old = NewService();
            await old.SaveAsync(0, new Progress { Level = 1 });
            await old.SaveAsync(0, new Progress { Level = 2 });
            var original = File.ReadAllBytes(old.GetFilePath(0));
            var missing = new VersionedSaveService<Progress>(directory, serializer, 3);
            Assert.ThrowsAsync<SaveVersionException>(async () => await missing.LoadAsync(0));
            var failed = new VersionedSaveService<Progress>(directory, serializer, 2, new[]
            {
                new SaveMigration(1, 2, (bytes, token) => throw new InvalidOperationException("invalid migration deployment"))
            });
            Assert.ThrowsAsync<SaveVersionException>(async () => await failed.LoadAsync(0));
            Assert.That(File.ReadAllBytes(old.GetFilePath(0)), Is.EqualTo(original));
            Assert.That((await old.LoadAsync(0)).Data.Level, Is.EqualTo(2));
        }

        [Test]
        public async Task CancelledMigration_ShouldNotCommitAndRemainReadableWithOldVersion()
        {
            var old = NewService();
            await old.SaveAsync(0, new Progress { Level = 3 });
            using var cancellation = new CancellationTokenSource();
            var upgraded = new VersionedSaveService<Progress>(directory, serializer, 2, new[]
            {
                new SaveMigration(1, 2, (bytes, token) =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(bytes);
                })
            });
            Assert.ThrowsAsync<OperationCanceledException>(async () => await upgraded.LoadAsync(0, cancellation.Token));
            Assert.That((await old.LoadAsync(0)).Data.Level, Is.EqualTo(3));
            Assert.That(File.Exists(old.GetFilePath(0) + ".bak"), Is.False);
        }

        [Test]
        public async Task ReplacementFailure_ShouldKeepCommittedDataAndMetadataAndRemoveTemporaryFile()
        {
            var service = NewService();
            await service.SaveAsync(0, new Progress { Level = 7 }, "committed");
            // A directory where the revision backup must go causes a real filesystem commit failure.
            Directory.CreateDirectory(service.GetFilePath(0) + ".bak");
            Assert.That(async () => await service.SaveAsync(0, new Progress { Level = 70 }, "uncommitted"), Throws.InstanceOf<IOException>());

            var reopened = await NewService().LoadAsync(0);
            Assert.That(reopened.Data.Level, Is.EqualTo(7));
            Assert.That(reopened.Metadata.Description, Is.EqualTo("committed"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [Test]
        public async Task SharedSlotGate_ShouldCancelWaitingWriterAcrossInstancesWithoutAffectingOwner()
        {
            var blocker = new BlockingSerializer(serializer);
            var owner = new VersionedSaveService<Progress>(directory, blocker, 1);
            var write = owner.SaveAsync(0, new Progress { Level = 11 }, "owner");
            await blocker.Entered.Task;
            using var cancellation = new CancellationTokenSource();
            var waiter = NewService().SaveAsync(0, new Progress { Level = 12 }, "cancelled waiter", cancellation.Token);
            Assert.That(waiter.IsCompleted, Is.False);
            cancellation.Cancel();
            Assert.That(async () => await waiter, Throws.InstanceOf<OperationCanceledException>());
            blocker.Release.TrySetResult(true);
            await write;

            var reopened = await NewService().LoadAsync(0);
            Assert.That(reopened.Data.Level, Is.EqualTo(11));
            Assert.That(reopened.Metadata.Description, Is.EqualTo("owner"));
        }

        [Test]
        public async Task ConcurrentSaves_ShouldKeepMatchingDataAndMetadataAndDeleteBothRevisions()
        {
            var tasks = Enumerable.Range(1, 30).Select(index => NewService().SaveAsync(4, new Progress { Level = index }, "revision " + index));
            await Task.WhenAll(tasks);
            var reopened = NewService();
            var result = await reopened.LoadAsync(4);
            Assert.That(result.Metadata.Description, Is.EqualTo("revision " + result.Data.Level));
            await reopened.DeleteAsync(4);
            Assert.That((await NewService().LoadAsync(4)).Status, Is.EqualTo(SaveLoadStatus.NotFound));
            Assert.That(File.Exists(reopened.GetFilePath(4) + ".bak"), Is.False);
        }

        [Test]
        public void CancelledSaveAndInvalidSlot_ShouldNotCreateFiles()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(async () => await NewService().SaveAsync(0, new Progress(), cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.Throws<ArgumentOutOfRangeException>(() => NewService().GetFilePath(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewService().GetFilePath(20));
            Assert.That(Directory.Exists(directory), Is.False);
        }

        private VersionedSaveService<Progress> NewService() => new VersionedSaveService<Progress>(directory, serializer, 1);

        public sealed class Progress { public int Level; }
        public sealed class OldProgress { public int Experience; }
        public sealed class IntermediateProgress { public int Level; }

        private sealed class BlockingSerializer : ISerializer
        {
            private readonly ISerializer inner;
            internal readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public BlockingSerializer(ISerializer inner) { this.inner = inner; }
            public SerializationFormat Format => inner.Format;
            public bool SupportsCompression => inner.SupportsCompression;
            public async Task<byte[]> SerializeAsync<TValue>(TValue value)
            {
                Entered.TrySetResult(true);
                await Release.Task;
                return await inner.SerializeAsync(value);
            }
            public Task<TValue> DeserializeAsync<TValue>(byte[] data) => inner.DeserializeAsync<TValue>(data);
            public Task<string> SerializeToStringAsync<TValue>(TValue value) => inner.SerializeToStringAsync(value);
            public Task<TValue> DeserializeFromStringAsync<TValue>(string json) => inner.DeserializeFromStringAsync<TValue>(json);
            public bool IsValidData(byte[] data) => inner.IsValidData(data);
            public Task<int> GetSerializedSizeAsync<TValue>(TValue value) => inner.GetSerializedSizeAsync(value);
        }
    }
}
