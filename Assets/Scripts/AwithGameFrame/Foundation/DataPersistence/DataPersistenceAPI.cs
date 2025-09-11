using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Foundation.Logging;
using AwithGameFrame.Foundation;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据持久化统一API入口
    /// 提供多种存储方式的管理和统一接口
    /// </summary>
    public static class DataPersistenceAPI
    {
        private static DataPersistenceConfig config;
        private static Dictionary<StorageType, IStorageProvider> storageProviders;
        private static bool isInitialized = false;
        private static readonly object lockObject = new object();

        /// <summary>
        /// 初始化数据持久化系统
        /// </summary>
        /// <param name="persistenceConfig">配置信息</param>
        /// <returns>初始化结果</returns>
        public static async UniTask<DataOperationResult> InitializeAsync(DataPersistenceConfig persistenceConfig = null)
        {
            if (isInitialized)
            {
                return DataOperationResult.Success;
            }

            lock (lockObject)
            {
                if (isInitialized)
                {
                    return DataOperationResult.Success;
                }

                config = persistenceConfig ?? new DataPersistenceConfig();
                storageProviders = new Dictionary<StorageType, IStorageProvider>();
                isInitialized = true;
            }

            try
            {
                // 确保Foundation包已初始化
                FoundationAPI.Initialize();
                
                // 初始化所有存储提供者
                await InitializeStorageProviders();
                
                FrameworkLogger.Info("数据持久化系统初始化完成", LogCategory.Core);
                return DataOperationResult.Success;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据持久化系统初始化失败: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        #region 基础CRUD操作

        /// <summary>
        /// 保存数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="data">数据</param>
        /// <param name="storageType">存储类型（可选，默认使用配置的默认类型）</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveAsync<T>(string key, T data, StorageType? storageType = null)
        {
            EnsureInitialized();
            
            var targetStorageType = storageType ?? config.DefaultStorageType;
            return await StorageHelper.SaveDataAsync(key, data, targetStorageType, storageProviders);
        }

        /// <summary>
        /// 加载数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型（可选，默认使用配置的默认类型）</param>
        /// <returns>数据和操作结果</returns>
        public static async UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key, StorageType? storageType = null)
        {
            EnsureInitialized();
            
            var targetStorageType = storageType ?? config.DefaultStorageType;
            return await StorageHelper.LoadDataAsync<T>(key, targetStorageType, storageProviders);
        }

        /// <summary>
        /// 删除数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型（可选，默认使用配置的默认类型）</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> DeleteAsync(string key, StorageType? storageType = null)
        {
            EnsureInitialized();
            
            var targetStorageType = storageType ?? config.DefaultStorageType;
            return await StorageHelper.DeleteDataAsync(key, targetStorageType, storageProviders);
        }

        /// <summary>
        /// 检查数据是否存在
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型（可选，默认使用配置的默认类型）</param>
        /// <returns>是否存在</returns>
        public static async UniTask<bool> ExistsAsync(string key, StorageType? storageType = null)
        {
            EnsureInitialized();
            
            var targetStorageType = storageType ?? config.DefaultStorageType;
            return await StorageHelper.ExistsDataAsync(key, targetStorageType, storageProviders);
        }

        #endregion

        #region 便捷方法

        /// <summary>
        /// 保存用户设置（使用PlayerPrefs）
        /// </summary>
        /// <typeparam name="T">设置类型</typeparam>
        /// <param name="key">设置键</param>
        /// <param name="settings">设置数据</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveSettingsAsync<T>(string key, T settings)
        {
            return await SaveAsync(key, settings, StorageType.PlayerPrefs);
        }

        /// <summary>
        /// 加载用户设置（使用PlayerPrefs）
        /// </summary>
        /// <typeparam name="T">设置类型</typeparam>
        /// <param name="key">设置键</param>
        /// <returns>设置数据和操作结果</returns>
        public static async UniTask<(T settings, DataOperationResult result)> LoadSettingsAsync<T>(string key)
        {
            return await LoadAsync<T>(key, StorageType.PlayerPrefs);
        }

        /// <summary>
        /// 保存游戏数据（使用JSON文件）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="gameData">游戏数据</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveGameDataAsync<T>(string key, T gameData)
        {
            return await SaveAsync(key, gameData, StorageType.JsonFile);
        }

        /// <summary>
        /// 加载游戏数据（使用JSON文件）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>游戏数据和操作结果</returns>
        public static async UniTask<(T gameData, DataOperationResult result)> LoadGameDataAsync<T>(string key)
        {
            return await LoadAsync<T>(key, StorageType.JsonFile);
        }

        /// <summary>
        /// 保存缓存数据（使用二进制文件）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="cacheData">缓存数据</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveCacheDataAsync<T>(string key, T cacheData)
        {
            return await SaveAsync(key, cacheData, StorageType.BinaryFile);
        }

        /// <summary>
        /// 加载缓存数据（使用二进制文件）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>缓存数据和操作结果</returns>
        public static async UniTask<(T cacheData, DataOperationResult result)> LoadCacheDataAsync<T>(string key)
        {
            return await LoadAsync<T>(key, StorageType.BinaryFile);
        }

        /// <summary>
        /// 保存复杂数据（使用数据库）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="complexData">复杂数据</param>
        /// <param name="databaseType">数据库类型（可选，默认SQLite）</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveComplexDataAsync<T>(string key, T complexData, DatabaseType databaseType = DatabaseType.SQLite)
        {
            var provider = GetDatabaseProvider(databaseType);
            if (provider == null)
            {
                FrameworkLogger.Error($"未找到数据库提供者: {databaseType}", LogCategory.Core);
                return DataOperationResult.Failed;
            }

            return await provider.SaveAsync(key, complexData);
        }

        /// <summary>
        /// 加载复杂数据（使用数据库）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="databaseType">数据库类型（可选，默认SQLite）</param>
        /// <returns>复杂数据和操作结果</returns>
        public static async UniTask<(T complexData, DataOperationResult result)> LoadComplexDataAsync<T>(string key, DatabaseType databaseType = DatabaseType.SQLite)
        {
            var provider = GetDatabaseProvider(databaseType);
            if (provider == null)
            {
                FrameworkLogger.Error($"未找到数据库提供者: {databaseType}", LogCategory.Core);
                return (default(T), DataOperationResult.Failed);
            }

            return await provider.LoadAsync<T>(key);
        }

        #endregion

        #region 管理功能

        /// <summary>
        /// 获取所有数据键
        /// </summary>
        /// <param name="storageType">存储类型（可选）</param>
        /// <returns>数据键数组</returns>
        public static async UniTask<string[]> GetAllKeysAsync(StorageType? storageType = null)
        {
            EnsureInitialized();
            
            if (storageType.HasValue)
            {
                var provider = GetStorageProvider(storageType.Value);
                return provider != null ? await provider.GetAllKeysAsync() : new string[0];
            }

            // 获取所有存储类型的数据键
            var allKeys = new List<string>();
            foreach (var provider in storageProviders.Values)
            {
                var keys = await provider.GetAllKeysAsync();
                allKeys.AddRange(keys);
            }
            
            return allKeys.ToArray();
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        /// <param name="storageType">存储类型（可选）</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> ClearAsync(StorageType? storageType = null)
        {
            EnsureInitialized();
            
            if (storageType.HasValue)
            {
                var provider = GetStorageProvider(storageType.Value);
                return provider != null ? await provider.ClearAsync() : DataOperationResult.Failed;
            }

            // 清空所有存储类型的数据
            var results = new List<DataOperationResult>();
            foreach (var provider in storageProviders.Values)
            {
                var result = await provider.ClearAsync();
                results.Add(result);
            }
            
            return results.Contains(DataOperationResult.Failed) ? DataOperationResult.Failed : DataOperationResult.Success;
        }

        /// <summary>
        /// 获取存储统计信息
        /// </summary>
        /// <param name="storageType">存储类型（可选）</param>
        /// <returns>统计信息</returns>
        public static async UniTask<StorageStatistics> GetStatisticsAsync(StorageType? storageType = null)
        {
            EnsureInitialized();
            
            if (storageType.HasValue)
            {
                var provider = GetStorageProvider(storageType.Value);
                return provider != null ? await provider.GetStatisticsAsync() : new StorageStatistics();
            }

            // 合并所有存储类型的统计信息
            var totalStats = new StorageStatistics();
            foreach (var provider in storageProviders.Values)
            {
                var stats = await provider.GetStatisticsAsync();
                totalStats.ItemCount += stats.ItemCount;
                totalStats.TotalSize += stats.TotalSize;
            }
            
            return totalStats;
        }

        #endregion

        #region 存储提供者管理

        /// <summary>
        /// 获取存储提供者
        /// </summary>
        /// <param name="storageType">存储类型</param>
        /// <returns>存储提供者</returns>
        public static IStorageProvider GetStorageProvider(StorageType storageType)
        {
            EnsureInitialized();
            return storageProviders.TryGetValue(storageType, out var provider) ? provider : null;
        }

        /// <summary>
        /// 获取数据库提供者
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>数据库存储提供者</returns>
        public static DatabaseStorageProvider GetDatabaseProvider(DatabaseType databaseType)
        {
            EnsureInitialized();
            return GetStorageProvider(StorageType.Database) as DatabaseStorageProvider;
        }

        /// <summary>
        /// 注册自定义存储提供者
        /// </summary>
        /// <param name="storageType">存储类型</param>
        /// <param name="provider">存储提供者</param>
        public static void RegisterStorageProvider(StorageType storageType, IStorageProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (lockObject)
            {
                storageProviders[storageType] = provider;
                FrameworkLogger.Info($"注册存储提供者: {storageType}", LogCategory.Core);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 确保系统已初始化
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!isInitialized || storageProviders == null)
            {
                throw new InvalidOperationException("DataPersistence未初始化，请先调用InitializeAsync");
            }
        }

        /// <summary>
        /// 初始化所有存储提供者
        /// </summary>
        private static async UniTask InitializeStorageProviders()
        {
            // 注册默认存储提供者
            RegisterStorageProvider(StorageType.PlayerPrefs, new PlayerPrefsStorage());
            RegisterStorageProvider(StorageType.JsonFile, new JsonFileStorage());
            RegisterStorageProvider(StorageType.BinaryFile, new BinaryFileStorage());
            RegisterStorageProvider(StorageType.Database, new DatabaseStorageProvider(DatabaseType.SQLite, "game_data"));

            // 初始化所有存储提供者
            foreach (var provider in storageProviders.Values)
            {
                await provider.InitializeAsync(config);
            }
        }

        #endregion
    }
}