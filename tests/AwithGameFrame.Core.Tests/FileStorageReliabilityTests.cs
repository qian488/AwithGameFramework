using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class FileStorageReliabilityTests
    {
        private string dataDirectory;

        [SetUp]
        public void SetUp()
        {
            dataDirectory = Path.Combine(Path.GetTempPath(), "AwithGameFrame.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task PartialWriteFailure_ShouldKeepExistingSaveAndReleaseHandles(StorageType type)
        {
            var config = CreateConfig();
            var expected = CreateRecord(7);
            using (var writer = CreateProvider(type))
            {
                Assert.That(await writer.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await writer.SaveAsync("snapshot", expected), Is.EqualTo(DataOperationResult.Success));
            }

            var filePath = GetFilePath(type, "snapshot");
            var originalBytes = File.ReadAllBytes(filePath);
            Assert.Throws<IOException>(() => FileStorageIO.Write(filePath, stream =>
            {
                stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
                throw new IOException("Injected failure after a partial write.");
            }));

            Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(originalBytes));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(filePath)), Is.EquivalentTo(new[] { filePath }));
            using (var reopened = CreateProvider(type))
            {
                Assert.That(await reopened.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var loaded = await reopened.LoadAsync<SavedRecord>("snapshot");
                AssertRecord(loaded, expected.Id);
                Assert.That(await reopened.SaveAsync("snapshot", CreateRecord(8)), Is.EqualTo(DataOperationResult.Success));
                AssertRecord(await reopened.LoadAsync<SavedRecord>("snapshot"), 8);
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task FailedReplacement_ShouldKeepExistingSaveAndRemoveTemporaryFile(StorageType type)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                Assert.Ignore("This failure case requires Windows file sharing semantics.");
            var config = CreateConfig();
            using (var storage = CreateProvider(type))
            {
                Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await storage.SaveAsync("snapshot", CreateRecord(3)), Is.EqualTo(DataOperationResult.Success));
                var filePath = GetFilePath(type, "snapshot");
                var originalBytes = File.ReadAllBytes(filePath);

                // A handle that denies deletion prevents replacement on Windows.
                // The atomic-write contract must preserve the previous save on failure.
                using (var locked = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Assert.That(await storage.SaveAsync("snapshot", CreateRecord(4)), Is.EqualTo(DataOperationResult.Failed));
                Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(originalBytes));
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(filePath)), Is.EquivalentTo(new[] { filePath }));

                Assert.That(await storage.SaveAsync("snapshot", CreateRecord(5)), Is.EqualTo(DataOperationResult.Success));
                AssertRecord(await storage.LoadAsync<SavedRecord>("snapshot"), 5);
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task SeparateProviders_ShouldReadCompleteSnapshotsDuringConcurrentWrites(StorageType type)
        {
            var config = CreateConfig();
            using (var first = CreateProvider(type))
            using (var second = CreateProvider(type))
            {
                Assert.That(await first.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await second.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await first.SaveAsync("snapshot", CreateRecord(0)), Is.EqualTo(DataOperationResult.Success));

                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task WriteSnapshots(IStorageProvider storage, int firstId)
                {
                    await start.Task;
                    for (var id = firstId; id < firstId + 16; id++)
                    {
                        Assert.That(await storage.SaveAsync("snapshot", CreateRecord(id)), Is.EqualTo(DataOperationResult.Success));
                        await Task.Yield();
                    }
                }

                var firstWriter = Task.Run(() => WriteSnapshots(first, 1));
                var secondWriter = Task.Run(() => WriteSnapshots(second, 17));
                var reader = Task.Run(async () =>
                {
                    await start.Task;
                    for (var i = 0; i < 100; i++)
                    {
                        var loaded = await first.LoadAsync<SavedRecord>("snapshot");
                        Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                        Assert.That(loaded.data, Is.Not.Null);
                        Assert.That(loaded.data.Payload, Is.EqualTo(CreateRecord(loaded.data.Id).Payload));
                        await Task.Yield();
                    }
                });
                start.SetResult(true);
                await Task.WhenAll(firstWriter, secondWriter, reader);
            }

            using (var reopened = CreateProvider(type))
            {
                Assert.That(await reopened.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var loaded = await reopened.LoadAsync<SavedRecord>("snapshot");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Id, Is.InRange(1, 32));
                AssertRecord(loaded, loaded.data.Id);
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(GetFilePath(type, "snapshot"))).Length, Is.EqualTo(1));
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task EmptyRawData_ShouldRoundTripButTypedLoadShouldReportCorruption(StorageType type)
        {
            using (var storage = CreateProvider(type))
            {
                Assert.That(await storage.InitializeAsync(CreateConfig()), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await storage.SaveAsync("empty", Array.Empty<byte>()), Is.EqualTo(DataOperationResult.Success));
                var raw = await storage.LoadAsync("empty");
                Assert.That(raw.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(raw.data, Is.Empty);
                var typed = await storage.LoadAsync<SavedRecord>("empty");
                Assert.That(typed.result, Is.EqualTo(DataOperationResult.Corrupted));
                Assert.That(typed.data, Is.Null);
            }
        }

        [TestCase(StorageType.JsonFile, false)]
        [TestCase(StorageType.JsonFile, true)]
        [TestCase(StorageType.BinaryFile, false)]
        [TestCase(StorageType.BinaryFile, true)]
        public async Task TruncatedOrInvalidUtf8Json_ShouldReportCorruption(StorageType type, bool invalidUtf8)
        {
            using (var storage = CreateProvider(type))
            {
                Assert.That(await storage.InitializeAsync(CreateConfig()), Is.EqualTo(DataOperationResult.Success));
                var payload = invalidUtf8 ? new byte[] { (byte)'"', 0xff, (byte)'"' } : Encoding.UTF8.GetBytes("{\"Id\":7");
                WriteTypedPayload(type, "invalid", payload);
                var loaded = await storage.LoadAsync<SavedRecord>("invalid");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Corrupted));
                Assert.That(loaded.data, Is.Null);
            }
        }

        [TestCase(0, 1, 1)]
        [TestCase(4, 1, 1)]
        [TestCase(8, 1, 1)]
        [TestCase(9, 2, 1)]
        [TestCase(9, 1, -1)]
        [TestCase(9, 1, 2)]
        public async Task InvalidBinaryEnvelope_ShouldReportCorruption(int fileLength, int version, int declaredLength)
        {
            using (var storage = new BinaryFileStorage())
            {
                Assert.That(await storage.InitializeAsync(CreateConfig()), Is.EqualTo(DataOperationResult.Success));
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                    {
                        writer.Write(version);
                        writer.Write(declaredLength);
                        writer.Write((byte)'0');
                    }
                    File.WriteAllBytes(GetFilePath(StorageType.BinaryFile, "invalid"), stream.ToArray().AsSpan(0, fileLength).ToArray());
                }
                var loaded = await storage.LoadAsync<SavedRecord>("invalid");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Corrupted));
                Assert.That(loaded.data, Is.Null);
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task MissingCompressionFooter_ShouldReportCorruption(StorageType type)
        {
            var config = CreateConfig();
            config.EnableCompression = true;
            using (var storage = CreateProvider(type))
            {
                Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await storage.SaveAsync("truncated", CreateRecord(9)), Is.EqualTo(DataOperationResult.Success));
                var filePath = GetFilePath(type, "truncated");
                var saved = File.ReadAllBytes(filePath);
                Array.Resize(ref saved, saved.Length - 8);
                if (type == StorageType.BinaryFile)
                    Buffer.BlockCopy(BitConverter.GetBytes(saved.Length - 8), 0, saved, 4, sizeof(int));
                File.WriteAllBytes(filePath, saved);
                var loaded = await storage.LoadAsync<SavedRecord>("truncated");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Corrupted));
                Assert.That(loaded.data, Is.Null);
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task CompressionEnabled_ShouldReadLegacyUncompressedFallback(StorageType type)
        {
            var config = CreateConfig();
            config.EnableCompression = true;
            using (var storage = CreateProvider(type))
            {
                Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                WriteTypedPayload(type, "legacy", Encoding.UTF8.GetBytes("{\"Id\":7,\"Payload\":\"legacy\"}"));
                var loaded = await storage.LoadAsync<SavedRecord>("legacy");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Id, Is.EqualTo(7));
                Assert.That(loaded.data.Payload, Is.EqualTo("legacy"));
            }
        }

        [TestCase(StorageType.JsonFile, true, false)]
        [TestCase(StorageType.JsonFile, false, true)]
        [TestCase(StorageType.JsonFile, true, true)]
        [TestCase(StorageType.BinaryFile, true, false)]
        [TestCase(StorageType.BinaryFile, false, true)]
        [TestCase(StorageType.BinaryFile, true, true)]
        public async Task CompressedAndEncryptedSave_ShouldRoundTripAfterReopening(StorageType type, bool compression, bool encryption)
        {
            var config = CreateConfig();
            config.EnableCompression = compression;
            config.EnableEncryption = encryption;
            using (var writer = CreateProvider(type))
            {
                Assert.That(await writer.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await writer.SaveAsync("snapshot", CreateRecord(6)), Is.EqualTo(DataOperationResult.Success));
            }
            using (var reader = CreateProvider(type))
            {
                Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                AssertRecord(await reader.LoadAsync<SavedRecord>("snapshot"), 6);
            }
        }

        [TestCase(65001)]
        [TestCase(1200)]
        [TestCase(1201)]
        [TestCase(12000)]
        [TestCase(12001)]
        public async Task JsonWithLegacyByteOrderMark_ShouldRemainReadable(int codePage)
        {
            using (var storage = new JsonFileStorage())
            {
                Assert.That(await storage.InitializeAsync(CreateConfig()), Is.EqualTo(DataOperationResult.Success));
                File.WriteAllText(GetFilePath(StorageType.JsonFile, "legacy"), "{\"Id\":7,\"Payload\":\"玩家\"}", Encoding.GetEncoding(codePage));
                var loaded = await storage.LoadAsync<SavedRecord>("legacy");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Payload, Is.EqualTo("玩家"));
            }
        }

        [Test]
        public async Task InvalidUtf8AfterByteOrderMark_ShouldReportCorruption()
        {
            using (var storage = new JsonFileStorage())
            {
                Assert.That(await storage.InitializeAsync(CreateConfig()), Is.EqualTo(DataOperationResult.Success));
                // Replacement decoding could silently turn this into valid but damaged JSON.
                File.WriteAllBytes(GetFilePath(StorageType.JsonFile, "invalid"), new byte[]
                {
                    0xef, 0xbb, 0xbf, (byte)'{', (byte)'"', (byte)'P', (byte)'a', (byte)'y', (byte)'l',
                    (byte)'o', (byte)'a', (byte)'d', (byte)'"', (byte)':', (byte)'"', 0xff, (byte)'"', (byte)'}'
                });
                var loaded = await storage.LoadAsync<SavedRecord>("invalid");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Corrupted));
                Assert.That(loaded.data, Is.Null);
            }
        }

        private DataPersistenceConfig CreateConfig() => new DataPersistenceConfig { DataRootPath = dataDirectory };

        private static IStorageProvider CreateProvider(StorageType type) =>
            type == StorageType.JsonFile ? (IStorageProvider)new JsonFileStorage() : new BinaryFileStorage();

        private string GetFilePath(StorageType type, string key) =>
            Path.Combine(type == StorageType.JsonFile ? dataDirectory : Path.Combine(dataDirectory, "BinaryData"), key + ".json");

        private void WriteTypedPayload(StorageType type, string key, byte[] payload)
        {
            var filePath = GetFilePath(type, key);
            if (type == StorageType.JsonFile)
            {
                File.WriteAllBytes(filePath, payload);
                return;
            }
            using (var writer = new BinaryWriter(File.Create(filePath)))
            {
                writer.Write(1);
                writer.Write(payload.Length);
                writer.Write(payload);
            }
        }

        private static SavedRecord CreateRecord(int id) => new SavedRecord
        {
            Id = id,
            Payload = "玩家" + id + ":" + new string((char)('A' + id % 26), 16 * 1024)
        };

        private static void AssertRecord((SavedRecord data, DataOperationResult result) loaded, int expectedId)
        {
            Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(loaded.data, Is.Not.Null);
            Assert.That(loaded.data.Id, Is.EqualTo(expectedId));
            Assert.That(loaded.data.Payload, Is.EqualTo(CreateRecord(expectedId).Payload));
        }

        public class SavedRecord
        {
            public int Id;
            public string Payload;
        }
    }
}
