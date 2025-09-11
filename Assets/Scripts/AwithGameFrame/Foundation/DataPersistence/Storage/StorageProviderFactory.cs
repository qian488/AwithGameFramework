
namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 存储提供者工厂
    /// 提供便捷的存储提供者创建方法
    /// </summary>
    public static class StorageProviderFactory
    {
        /// <summary>
        /// 创建PlayerPrefs存储提供者
        /// </summary>
        public static PlayerPrefsStorage CreatePlayerPrefsStorage()
        {
            return new PlayerPrefsStorage();
        }

        /// <summary>
        /// 创建JSON文件存储提供者
        /// </summary>
        public static JsonFileStorage CreateJsonFileStorage()
        {
            return new JsonFileStorage();
        }

        /// <summary>
        /// 创建二进制文件存储提供者
        /// </summary>
        public static BinaryFileStorage CreateBinaryFileStorage()
        {
            return new BinaryFileStorage();
        }

        /// <summary>
        /// 创建SQLite数据库存储提供者
        /// </summary>
        public static DatabaseStorageProvider CreateSQLiteStorage(string tableName = "game_data")
        {
            return new DatabaseStorageProvider(DatabaseType.SQLite, tableName);
        }

        /// <summary>
        /// 创建MySQL数据库存储提供者
        /// </summary>
        public static DatabaseStorageProvider CreateMySQLStorage(string tableName = "game_data")
        {
            return new DatabaseStorageProvider(DatabaseType.MySQL, tableName);
        }

        /// <summary>
        /// 创建PostgreSQL数据库存储提供者
        /// </summary>
        public static DatabaseStorageProvider CreatePostgreSQLStorage(string tableName = "game_data")
        {
            return new DatabaseStorageProvider(DatabaseType.PostgreSQL, tableName);
        }

        /// <summary>
        /// 创建指定类型的数据库存储提供者
        /// </summary>
        public static DatabaseStorageProvider CreateDatabaseStorage(DatabaseType databaseType, string tableName = "game_data")
        {
            return new DatabaseStorageProvider(databaseType, tableName);
        }
    }
}
