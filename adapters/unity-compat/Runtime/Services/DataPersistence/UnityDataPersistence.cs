using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>设置 Unity 存储目录并显式安装 Unity 提供者。</summary>
    public static class UnityDataPersistence
    {
        public static DataPersistenceConfig CreateConfig()
        {
            return UseUnityPaths(new DataPersistenceConfig());
        }

        public static DataPersistenceConfig UseUnityPaths(DataPersistenceConfig config)
        {
            if (config == null) throw new System.ArgumentNullException(nameof(config));
            config.DataRootPath = Path.Combine(Application.persistentDataPath, "Data");
            config.BinaryDataRootPath = Path.Combine(Application.persistentDataPath, "BinaryData");
            return config;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallProviders()
        {
            if (!DataPersistenceAPI.IsInitialized)
                DataPersistenceAPI.RegisterStorageProvider(StorageType.PlayerPrefs, new PlayerPrefsStorage());
        }

        /// <summary>调用前可通过 config 指定自定义目录；数据库存储按需安装。</summary>
        public static Task<DataOperationResult> InitializeAsync(DataPersistenceConfig config = null, bool enableDatabase = false)
        {
            InstallProviders();
            if (enableDatabase && !DatabaseFactory.IsDatabaseTypeSupported(DatabaseType.SQLite))
                throw new System.InvalidOperationException("Install the Unity SQLite adapter and Mono.Data.Sqlite plugin, or register a SQLite provider before enabling database storage.");
            if (enableDatabase && DataPersistenceAPI.IsInitialized && DataPersistenceAPI.GetStorageProvider(StorageType.Database) == null)
                throw new System.InvalidOperationException("启用数据库前请先调用 DataPersistenceAPI.ShutdownAsync()，再重新初始化。");
            if (enableDatabase && !DataPersistenceAPI.IsInitialized)
                DataPersistenceAPI.RegisterStorageProvider(StorageType.Database, new DatabaseStorageProvider(DatabaseType.SQLite, "game_data"));
            return DataPersistenceAPI.InitializeAsync(config ?? CreateConfig());
        }
    }
}
