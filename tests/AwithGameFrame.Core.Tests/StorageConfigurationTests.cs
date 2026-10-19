using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture, NonParallelizable]
    public class StorageConfigurationTests
    {
        private string directory;

        [SetUp]
        public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "AwithStorageOptions-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private static IEnumerable<TestCaseData> UnsupportedOptions()
        {
            foreach (var type in new[] { StorageType.JsonFile, StorageType.BinaryFile })
                foreach (var option in new[] { "AES", "LZ4", "Deflate", "NoEncryptionAlgorithm", "NoCompressionAlgorithm",
                    "Backup", "BackupPolicy", "Integrity", "SecurityPolicy", "Cloud", "Monitoring", "DisableValidation",
                    "KeyDerivation", "KeyLength", "CompressionLevel", "DefaultFormat", "PreferredFormat", "TypeInfo", "NestedCompression" })
                    yield return new TestCaseData(type, option).SetName(type + "_RejectsRequested_" + option);
        }

        [TestCaseSource(nameof(UnsupportedOptions))]
        public async Task UnsupportedRequestsFailBeforeCreatingFiles(StorageType type, string option)
        {
            var config = NewConfig();
            switch (option)
            {
                case "AES": config.EnableEncryption = true; config.EncryptionAlgorithm = EncryptionAlgorithm.AES256; break;
                case "LZ4": config.EnableCompression = true; config.CompressionAlgorithm = CompressionAlgorithm.LZ4; break;
                case "Deflate": config.EnableCompression = true; config.CompressionAlgorithm = CompressionAlgorithm.Deflate; break;
                case "NoEncryptionAlgorithm": config.EnableEncryption = true; config.EncryptionAlgorithm = EncryptionAlgorithm.None; break;
                case "NoCompressionAlgorithm": config.EnableCompression = true; config.CompressionAlgorithm = CompressionAlgorithm.None; break;
                case "Backup": config.EnableAutoBackup = true; break;
                case "BackupPolicy": config.DefaultBackupStrategy = BackupStrategy.Local; break;
                case "Integrity": config.EnableIntegrityCheck = true; break;
                case "SecurityPolicy": config.DefaultSecurityLevel = SecurityLevel.Advanced; break;
                case "Cloud": config.CloudStorageType = CloudStorageType.Custom; break;
                case "Monitoring": config.EnablePerformanceMonitoring = true; break;
                case "DisableValidation": config.EnableDataValidation = false; break;
                case "KeyDerivation": config.EnableEncryption = true; config.EnableKeyDerivation = true; break;
                case "KeyLength": config.EnableEncryption = true; config.KeyLength = 256; break;
                case "CompressionLevel": config.EnableCompression = true; config.CompressionLevel = 1; break;
                case "DefaultFormat": config.DefaultSerializationFormat = SerializationFormat.MessagePack; break;
                case "PreferredFormat": config.PreferredSerializationFormat = SerializationFormat.Protobuf; break;
                case "TypeInfo": config.SerializationConfig.IncludeTypeInfo = true; break;
                case "NestedCompression": config.SerializationConfig.EnableCompression = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(option));
            }
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.NotImplemented));
            Assert.That(storage.IsInitialized, Is.False);
            Assert.That(await storage.SaveAsync("blocked", new Record { Id = 7 }), Is.Not.EqualTo(DataOperationResult.Success));
            Assert.That(Directory.Exists(directory), Is.False, "Unsupported options must fail before touching the filesystem.");
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task EveryPresetIsUsableAndReleasePresetsDeclareActualCapabilities(StorageType type)
        {
            foreach (DataPersistencePreset preset in Enum.GetValues(typeof(DataPersistencePreset)))
            {
                var config = DataPersistenceConfig.GetPreset(preset);
                config.DataRootPath = directory;
                if (preset == DataPersistencePreset.Release || preset == DataPersistencePreset.Production)
                {
                    Assert.That(config.EnableEncryption, Is.False);
                    Assert.That(config.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.None));
                    Assert.That(config.EnableCompression, Is.True);
                    Assert.That(config.CompressionAlgorithm, Is.EqualTo(CompressionAlgorithm.GZip));
                }
                using (var writer = Provider(type))
                {
                    Assert.That(await writer.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                    Assert.That(await writer.SaveAsync(preset.ToString(), new Record { Id = 7, Payload = "玩家" }), Is.EqualTo(DataOperationResult.Success));
                }
                if (preset == DataPersistencePreset.Release || preset == DataPersistencePreset.Production)
                {
                    var bytes = File.ReadAllBytes(FilePath(type, preset.ToString()));
                    var offset = type == StorageType.JsonFile ? 0 : 8;
                    Assert.That(bytes[offset], Is.EqualTo(0x1f));
                    Assert.That(bytes[offset + 1], Is.EqualTo(0x8b), "The selected GZip policy must produce an actual GZip payload.");
                }
                using var reader = Provider(type);
                Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var loaded = await reader.LoadAsync<Record>(preset.ToString());
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Payload, Is.EqualTo("玩家"));
            }
        }

        [TestCase(StorageType.JsonFile, false)]
        [TestCase(StorageType.JsonFile, true)]
        [TestCase(StorageType.BinaryFile, false)]
        [TestCase(StorageType.BinaryFile, true)]
        public async Task ExistingXorAndGzipFilesRemainReadable(StorageType type, bool compressed)
        {
            var config = NewConfig();
            config.EnableEncryption = true; config.EncryptionAlgorithm = EncryptionAlgorithm.XOR;
            config.EncryptionKey = "legacy-key"; config.EnableCompression = compressed;
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
            const string xor = "F0cuBUFDGkdHKQ0cCw4CHQ9RRxUJAgYCGltQ";
            var bytes = compressed
                ? Convert.FromBase64String(type == StorageType.JsonFile
                    ? "H4sIAAAAAAACCnMzSC51CnVzcc9O8fAONEh2Ljdx9gi0DAqqCPVyTI90ds8pCQQA2VDVeSQAAAA="
                    : "H4sIAAAAAAACChN312N1dJZyd9fkleHmY5LlD3QX5WRiY5KKDgAAA8m4JRsAAAA=")
                : type == StorageType.JsonFile ? Encoding.UTF8.GetBytes(xor) : Convert.FromBase64String(xor);
            var path = FilePath(type, "legacy");
            if (type == StorageType.JsonFile) File.WriteAllBytes(path, bytes);
            else using (var writer = new BinaryWriter(File.Create(path))) { writer.Write(1); writer.Write(bytes.Length); writer.Write(bytes); }
            var loaded = await storage.LoadAsync<Record>("legacy");
            Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(loaded.data.Id, Is.EqualTo(7));
            Assert.That(loaded.data.Payload, Is.EqualTo("legacy"));
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task ExplicitXorWritesTheExistingWireFormat(StorageType type)
        {
            var config = NewConfig(); config.EnableEncryption = true; config.EncryptionKey = "legacy-key"; config.PrettyPrint = false;
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
            Assert.That(await storage.SaveAsync("xor", new LegacyRecord { Id = 7, Payload = "legacy" }), Is.EqualTo(DataOperationResult.Success));
            var bytes = File.ReadAllBytes(FilePath(type, "xor"));
            const string golden = "F0cuBUFDGkdHKQ0cCw4CHQ9RRxUJAgYCGltQ";
            if (type == StorageType.JsonFile) Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(golden));
            else
            {
                using var reader = new BinaryReader(new MemoryStream(bytes));
                Assert.That(reader.ReadInt32(), Is.EqualTo(1));
                var length = reader.ReadInt32();
                Assert.That(reader.ReadBytes(length), Is.EqualTo(Convert.FromBase64String(golden)));
            }
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task EmptyXorKeyFailsInitializationWithoutWriting(StorageType type)
        {
            var config = NewConfig(); config.EnableEncryption = true; config.EncryptionKey = "";
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Failed));
            Assert.That(storage.IsInitialized, Is.False);
            Assert.That(Directory.Exists(directory), Is.False);
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task DisabledFeaturesIgnoreInactiveAlgorithmFields(StorageType type)
        {
            var config = NewConfig();
            config.EncryptionAlgorithm = EncryptionAlgorithm.AES256; config.EnableKeyDerivation = true; config.KeyLength = 256;
            config.CompressionAlgorithm = CompressionAlgorithm.LZ4; config.CompressionLevel = -1;
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
            Assert.That(await storage.SaveAsync("plain", new Record { Id = 7 }), Is.EqualTo(DataOperationResult.Success));
            Assert.That((await storage.LoadAsync<Record>("plain")).data.Id, Is.EqualTo(7));
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task OpenProviderCapturesOptionsAndFailedReinitializeCannotKeepWriting(StorageType type)
        {
            var config = NewConfig();
            config.EnableEncryption = true; config.EncryptionKey = "saved-key"; config.EnableCompression = true;
            using (var storage = Provider(type))
            {
                Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                config.EncryptionAlgorithm = EncryptionAlgorithm.AES256; config.CompressionAlgorithm = CompressionAlgorithm.LZ4;
                config.EncryptionKey = "changed-key";
                Assert.That(await storage.SaveAsync("captured", new Record { Id = 7 }), Is.EqualTo(DataOperationResult.Success));
                var bytes = File.ReadAllBytes(FilePath(type, "captured"));
                Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.NotImplemented));
                Assert.That(storage.IsInitialized, Is.False);
                Assert.That(await storage.SaveAsync("captured", new Record { Id = 8 }), Is.Not.EqualTo(DataOperationResult.Success));
                Assert.That(File.ReadAllBytes(FilePath(type, "captured")), Is.EqualTo(bytes));
            }
            config.EncryptionAlgorithm = EncryptionAlgorithm.XOR; config.CompressionAlgorithm = CompressionAlgorithm.GZip; config.EncryptionKey = "saved-key";
            using var reader = Provider(type);
            Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
            Assert.That((await reader.LoadAsync<Record>("captured")).data.Id, Is.EqualTo(7));
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task TypedFileStorageHonorsJsonDateAndNullOptions(StorageType type)
        {
            var config = NewConfig(); config.SerializationConfig.IgnoreNullValues = true; config.SerializationConfig.DateFormat = "yyyy/MM/dd";
            using var storage = Provider(type);
            Assert.That(await storage.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
            Assert.That(await storage.SaveAsync("options", new Record { Id = 7, SavedAt = new DateTime(2026, 10, 19) }), Is.EqualTo(DataOperationResult.Success));
            var bytes = File.ReadAllBytes(FilePath(type, "options"));
            var json = Encoding.UTF8.GetString(bytes, type == StorageType.JsonFile ? 0 : 8, bytes.Length - (type == StorageType.JsonFile ? 0 : 8));
            Assert.That(json, Does.Contain("2026/10/19").And.Not.Contain("Payload"));
            Assert.That((await storage.LoadAsync<Record>("options")).data.SavedAt, Is.EqualTo(new DateTime(2026, 10, 19)));
        }

        [TestCase(StorageType.JsonFile)]
        [TestCase(StorageType.BinaryFile)]
        public async Task DefaultDateFormatPreservesTimestampPrecisionAndKind(StorageType type)
        {
            var timestamp = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
            using (var writer = Provider(type))
            {
                Assert.That(await writer.InitializeAsync(NewConfig()), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await writer.SaveAsync("timestamp", new Record { SavedAt = timestamp }), Is.EqualTo(DataOperationResult.Success));
            }
            using var reader = Provider(type);
            Assert.That(await reader.InitializeAsync(NewConfig()), Is.EqualTo(DataOperationResult.Success));
            var loaded = await reader.LoadAsync<Record>("timestamp");
            Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(loaded.data.SavedAt, Is.EqualTo(timestamp));
            Assert.That(loaded.data.SavedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [TestCase(SerializationFormat.Json)]
        [TestCase(SerializationFormat.Binary)]
        public void BuiltInCodecsRejectClrTypeMetadata(SerializationFormat format)
            => Assert.Throws<NotSupportedException>(() => SerializerFactory.CreateSerializer(format, new SerializationConfig { IncludeTypeInfo = true }));

        [Test]
        public void CodecsRejectUnsupportedCompressionRequests()
        {
            Assert.Throws<NotSupportedException>(() => new JsonSerializer(new SerializationConfig { EnableCompression = true }));
            Assert.Throws<NotSupportedException>(() => new CompressedJsonSerializer(new SerializationConfig { EnableCompression = true, CompressionLevel = 1 }));
        }

        [Test]
        public async Task BinaryCodecHonorsJsonOptionsAndCapturesMutableConfiguration()
        {
            var config = new SerializationConfig { EnableCompression = true, IgnoreNullValues = true, DateFormat = "yyyy/MM/dd" };
            var codec = new CompressedJsonSerializer(config);
            config.EnableCompression = false; config.IgnoreNullValues = false; config.DateFormat = "yyyy";
            var bytes = await codec.SerializeAsync(new Record { Id = 7, SavedAt = new DateTime(2026, 10, 19) });
            Assert.That(bytes[4], Is.EqualTo(1));
            using var stream = new MemoryStream(bytes, 9, bytes.Length - 9);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var text = new StreamReader(gzip);
            Assert.That(await text.ReadToEndAsync(), Does.Contain("2026/10/19").And.Not.Contain("Payload"));
            Assert.That((await codec.DeserializeAsync<Record>(bytes)).SavedAt, Is.EqualTo(new DateTime(2026, 10, 19)));
        }

        [Test]
        public async Task StaticApiPropagatesUnsupportedConfigurationFailure()
        {
            await DataPersistenceAPI.ShutdownAsync();
            try
            {
                var config = NewConfig(); config.EnableEncryption = true; config.EncryptionAlgorithm = EncryptionAlgorithm.AES256;
                Assert.That(await DataPersistenceAPI.InitializeAsync(config), Is.EqualTo(DataOperationResult.Failed));
                Assert.That(DataPersistenceAPI.IsInitialized, Is.False);
                Assert.That(Directory.Exists(directory), Is.False);
            }
            finally { await DataPersistenceAPI.ShutdownAsync(); }
        }

        private DataPersistenceConfig NewConfig() => new DataPersistenceConfig { DataRootPath = directory };
        private static IStorageProvider Provider(StorageType type) => type == StorageType.JsonFile ? (IStorageProvider)new JsonFileStorage() : new BinaryFileStorage();
        private string FilePath(StorageType type, string key) => Path.Combine(type == StorageType.JsonFile ? directory : Path.Combine(directory, "BinaryData"), key + ".json");
        public sealed class Record { public int Id; public string Payload; public DateTime SavedAt; }
        public sealed class LegacyRecord { public int Id; public string Payload; }
    }
}
