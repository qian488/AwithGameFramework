using System;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 存储配置
    /// 支持各种存储方式的配置
    /// </summary>
    [Serializable]
    public class StorageConfig
    {
        public string storagePath = "";
        public string keyPrefix = "";
        public bool enableCompression = true;
        public bool enableEncryption = false;
        public string encryptionKey = "";

        public string databasePath = "game_data.db";
        public string databaseHost = "localhost";
        public string databaseName = "game_data";
        public string databaseUser = "root";
        public string databasePassword = "";
        public int databasePort = 3306;
        public int connectionTimeout = 30;
        public int commandTimeout = 30;

        public string fileExtension = ".dat";
        public bool createBackup = true;
        public int maxBackupCount = 5;

        public int maxCacheSize = 1000;
        public bool enableAsyncOperations = true;
        public int batchSize = 100;

        /// <summary>
        /// 获取默认配置
        /// </summary>
        public static StorageConfig Default => new StorageConfig();

        internal StorageConfig CreateSnapshot() => (StorageConfig)MemberwiseClone();
    }
}
