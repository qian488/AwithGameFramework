using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class PersistenceTests
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

        [Test]
        public async Task JsonFileStorage_ShouldPreservePublicFieldsAfterReopening()
        {
            var config = new DataPersistenceConfig { DataRootPath = dataDirectory };
            using (var writer = new JsonFileStorage())
            {
                Assert.That(await writer.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var profile = new PlayerProfile { Level = 12, DisplayName = "玩家", Inventory = new[] { 3, 7, 11 } };
                Assert.That(await writer.SaveAsync("profile", profile), Is.EqualTo(DataOperationResult.Success));
            }

            using (var reader = new JsonFileStorage())
            {
                Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var loaded = await reader.LoadAsync<PlayerProfile>("profile");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Level, Is.EqualTo(12));
                Assert.That(loaded.data.DisplayName, Is.EqualTo("玩家"));
                Assert.That(loaded.data.Inventory, Is.EqualTo(new[] { 3, 7, 11 }));

                var missing = await reader.LoadAsync<PlayerProfile>("missing");
                Assert.That(missing.result, Is.EqualTo(DataOperationResult.NotFound));
                Assert.That(missing.data, Is.Null);
            }
        }

        [Test]
        public async Task BinaryFileStorage_ShouldReadLegacyVersionOneObjectFile()
        {
            var config = new DataPersistenceConfig { DataRootPath = dataDirectory, EnableCompression = true };
            using (var reader = new BinaryFileStorage())
            {
                Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var bytes = Encoding.UTF8.GetBytes("{\"Level\":7,\"DisplayName\":\"legacy\",\"Inventory\":[1,2]}");
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                        gzip.Write(bytes, 0, bytes.Length);
                    bytes = output.ToArray();
                }
                using (var stream = File.Create(Path.Combine(dataDirectory, "BinaryData", "legacy.json")))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(1);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }

                var loaded = await reader.LoadAsync<PlayerProfile>("legacy");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data.Level, Is.EqualTo(7));
                Assert.That(loaded.data.DisplayName, Is.EqualTo("legacy"));
                Assert.That(loaded.data.Inventory, Is.EqualTo(new[] { 1, 2 }));
            }
        }

        [Test]
        public async Task BinaryFileStorage_ShouldPreserveRawBytesAfterReopening()
        {
            var config = new DataPersistenceConfig { DataRootPath = dataDirectory };
            byte[] payload = { 0, 1, 10, 127, 128, 254, 255 };
            using (var writer = new BinaryFileStorage())
            {
                Assert.That(await writer.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await writer.SaveAsync("snapshot", payload), Is.EqualTo(DataOperationResult.Success));
            }

            using (var reader = new BinaryFileStorage())
            {
                Assert.That(await reader.InitializeAsync(config), Is.EqualTo(DataOperationResult.Success));
                var loaded = await reader.LoadAsync("snapshot");
                Assert.That(loaded.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(loaded.data, Is.EqualTo(payload));
            }
        }

        public class PlayerProfile
        {
            public int Level;
            public string DisplayName;
            public int[] Inventory;
        }
    }

    [TestFixture]
    public class OptionalProviderTests
    {
        [Test]
        public async Task DatabaseStorage_ShouldReportMissingDriver()
        {
            Assert.That(DatabaseFactory.CreateProvider(DatabaseType.SQLite), Is.Null);
            using (var storage = new DatabaseStorageProvider(DatabaseType.SQLite))
            {
                Assert.That(await storage.InitializeAsync(), Is.EqualTo(DataOperationResult.Failed));
                Assert.That(storage.IsInitialized, Is.False);
            }
        }

        [TestCase(SerializationFormat.MessagePack)]
        [TestCase(SerializationFormat.Protobuf)]
        public void OptionalSerializationFormat_ShouldRequireRegisteredCodec(SerializationFormat format)
        {
            Assert.That(SerializerFactory.IsFormatSupported(format), Is.False);
            Assert.Throws<NotSupportedException>(() => SerializerFactory.CreateSerializer(format));
        }
    }
}
