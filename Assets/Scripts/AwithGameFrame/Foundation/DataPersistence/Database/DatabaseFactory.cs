using System;
using System.Collections.Generic;
using AwithGameFrame.Foundation.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据库工厂
    /// 根据数据库类型创建对应的数据库提供者
    /// </summary>
    public static class DatabaseFactory
    {
        private static readonly Dictionary<DatabaseType, Type> _databaseProviders = new Dictionary<DatabaseType, Type>();
        private static bool _isInitialized = false;

        /// <summary>
        /// 初始化数据库工厂
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized)
            {
                FrameworkLogger.Warn("DatabaseFactory已经初始化", LogCategory.Core);
                return;
            }

            // 注册默认的数据库提供者
            RegisterDefaultProviders();
            _isInitialized = true;
            
            FrameworkLogger.Info("数据库工厂初始化完成", LogCategory.Core);
        }

        /// <summary>
        /// 创建数据库提供者
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>数据库提供者实例</returns>
        public static IDatabaseProvider CreateProvider(DatabaseType databaseType)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (_databaseProviders.TryGetValue(databaseType, out var providerType))
            {
                try
                {
                    return Activator.CreateInstance(providerType) as IDatabaseProvider;
                }
                catch (Exception ex)
                {
                    FrameworkLogger.Error($"创建数据库提供者失败: {ex.Message}", LogCategory.Core);
                    return null;
                }
            }

            FrameworkLogger.Error($"不支持的数据库类型: {databaseType}", LogCategory.Core);
            return null;
        }

        /// <summary>
        /// 注册数据库提供者
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <param name="providerType">提供者类型</param>
        public static void RegisterProvider(DatabaseType databaseType, Type providerType)
        {
            if (!typeof(IDatabaseProvider).IsAssignableFrom(providerType))
            {
                FrameworkLogger.Error($"类型 {providerType.Name} 不实现 IDatabaseProvider 接口", LogCategory.Core);
                return;
            }

            _databaseProviders[databaseType] = providerType;
            FrameworkLogger.Info($"注册数据库提供者: {databaseType} -> {providerType.Name}", LogCategory.Core);
        }

        /// <summary>
        /// 获取支持的数据库类型
        /// </summary>
        /// <returns>支持的数据库类型列表</returns>
        public static DatabaseType[] GetSupportedDatabaseTypes()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            var types = new List<DatabaseType>();
            foreach (var kvp in _databaseProviders)
            {
                types.Add(kvp.Key);
            }
            return types.ToArray();
        }

        /// <summary>
        /// 检查数据库类型是否支持
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>是否支持</returns>
        public static bool IsDatabaseTypeSupported(DatabaseType databaseType)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            return _databaseProviders.ContainsKey(databaseType);
        }

        #region 私有方法

        /// <summary>
        /// 注册默认的数据库提供者
        /// </summary>
        private static void RegisterDefaultProviders()
        {
            // 注册SQLite提供者（默认支持）
            RegisterProvider(DatabaseType.SQLite, typeof(SQLiteDatabaseProvider));
            
            // 其他数据库提供者需要开发者自行注册
            // 例如：
            // RegisterProvider(DatabaseType.MySQL, typeof(MySQLDatabaseProvider));
            // RegisterProvider(DatabaseType.PostgreSQL, typeof(PostgreSQLDatabaseProvider));
            // RegisterProvider(DatabaseType.MongoDB, typeof(MongoDBDatabaseProvider));
        }

        #endregion
    }
}
