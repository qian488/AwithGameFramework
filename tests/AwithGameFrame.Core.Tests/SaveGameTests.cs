using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class SaveGameTests
    {
        private string dataDirectory;
        private SaveGameManager manager;

        [SetUp]
        public async Task SetUp()
        {
            await DataPersistenceAPI.ShutdownAsync();
            dataDirectory = Path.Combine(Path.GetTempPath(), "AwithGameFrame.Tests", Guid.NewGuid().ToString("N"));
            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = dataDirectory }),
                Is.EqualTo(DataOperationResult.Success));
            manager = new SaveGameManager();
            manager.Initialize();
            await manager.Initialization;
        }

        [TearDown]
        public async Task TearDown()
        {
            manager?.Shutdown();
            ServiceLocator.Clear();
            await DataPersistenceAPI.ShutdownAsync();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }

        [Test]
        public async Task DeleteSlot_ShouldDeleteJsonPayloadWhenMetadataUsesBinaryDefault()
        {
            manager.Shutdown();
            await DataPersistenceAPI.ShutdownAsync();
            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig
            {
                DataRootPath = dataDirectory,
                DefaultStorageType = StorageType.BinaryFile
            }), Is.EqualTo(DataOperationResult.Success));
            manager = new SaveGameManager();
            manager.Initialize();
            await manager.Initialization;

            Assert.That(await manager.SaveToSlot(2, new PersistenceTests.PlayerProfile { Level = 23 }), Is.True);
            Assert.That(await DataPersistenceAPI.ExistsAsync("SaveSlot_2_Meta", StorageType.BinaryFile), Is.True);
            Assert.That(await manager.DeleteSlot(2), Is.True);
            Assert.That((await manager.LoadFromSlot<PersistenceTests.PlayerProfile>(2)).success, Is.False);
            Assert.That(await DataPersistenceAPI.ExistsAsync("SaveSlot_2_Meta", StorageType.BinaryFile), Is.False);
        }

        [Test]
        public async Task SlotAndMetadata_ShouldSurviveRestartAndDeleteTogether()
        {
            Assert.That(await manager.SaveToSlot(3, new PersistenceTests.PlayerProfile { Level = 17 }, "Before the boss"), Is.True);
            var savedMetadata = manager.GetAllSlots()[0];
            manager.Shutdown();
            await DataPersistenceAPI.ShutdownAsync();
            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = dataDirectory }),
                Is.EqualTo(DataOperationResult.Success));

            manager = new SaveGameManager();
            manager.Initialize();
            await manager.Initialization;

            Assert.That(manager.HasSlot(3), Is.True);
            Assert.That(manager.GetAllSlots()[0].Description, Is.EqualTo("Before the boss"));
            Assert.That(manager.GetAllSlots()[0].SaveTime, Is.EqualTo(savedMetadata.SaveTime));
            var loaded = await manager.LoadFromSlot<PersistenceTests.PlayerProfile>(3);
            Assert.That(loaded.success, Is.True);
            Assert.That(loaded.data.Level, Is.EqualTo(17));

            Assert.That(await manager.DeleteSlot(3), Is.True);
            await manager.ReloadSlotsAsync();
            Assert.That(manager.HasSlot(3), Is.False);
            Assert.That((await manager.LoadFromSlot<PersistenceTests.PlayerProfile>(3)).success, Is.False);
        }
    }
}
