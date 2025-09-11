using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Logging;
using AwithGameFrame.DataPersistence.Database;
using AwithGameFrame.DataPersistence.Serialization;

namespace AwithGameFrame.DataPersistence.Storage
{
    /// <summary>
    /// 数据库存储提供者
    /// 支持多种数据库的统一存储接口
    /// </summary>
    public class DatabaseStorageProvider : IStorageProvider
    {
        private readonly IDatabaseProvider _databaseProvider;
        private readonly string _tableName;
        private bool _isInitialized = false;

        public StorageType StorageType => StorageType.Database;
        public bool IsInitialized => _isInitialized;
        public string Name => $"Database_{_databaseProvider.DatabaseType}";
        public DatabaseType DatabaseType => _databaseProvider.DatabaseType;

        public DatabaseStorageProvider(DatabaseType databaseType, string tableName = "game_data")
        {
            _databaseProvider = DatabaseFactory.CreateProvider(databaseType);
            _tableName = tableName;
        }

        public async UniTask<DataOperationResult> InitializeAsync()
        {
            return await InitializeAsync((StorageConfig)null);
        }

        public async UniTask<DataOperationResult> InitializeAsync(object config)
        {
            var storageConfig = config as StorageConfig;
            return await InitializeAsync(storageConfig);
        }

        public async UniTask<DataOperationResult> InitializeAsync(StorageConfig config)
        {
            try
            {
                if (_isInitialized)
                {
                    return DataOperationResult.Success;
                }

                if (_databaseProvider == null)
                {
                    FrameworkLogger.Error("数据库提供者创建失败", LogCategory.Core);
                    return DataOperationResult.Failed;
                }

                // 构建连接字符串
                var connectionString = BuildConnectionString(config);
                
                // 初始化数据库连接
                var result = await _databaseProvider.InitializeAsync(connectionString);
                if (result != DataOperationResult.Success)
                {
                    FrameworkLogger.Error($"数据库初始化失败: {result}", LogCategory.Core);
                    return result;
                }

                // 创建数据表
                await CreateDataTableAsync();

                _isInitialized = true;
                FrameworkLogger.Info($"数据库存储提供者初始化完成: {_databaseProvider.DatabaseType}", LogCategory.Core);
                return DataOperationResult.Success;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库存储提供者初始化失败: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        public async UniTask<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            try
            {
                if (!_isInitialized)
                {
                    return DataOperationResult.NotInitialized;
                }

                var sql = GetUpsertSql();
                var parameters = new Dictionary<string, object>
                {
                    ["key"] = key,
                    ["data"] = data,
                    ["created_at"] = DateTime.Now,
                    ["updated_at"] = DateTime.Now
                };

                var result = await _databaseProvider.ExecuteAsync(sql, parameters);
                if (result == DataOperationResult.Success)
                {
                    FrameworkLogger.Debug($"数据库保存成功: {key}", LogCategory.Core);
                }
                else
                {
                    FrameworkLogger.Error($"数据库保存失败: {key}, 结果: {result}", LogCategory.Core);
                }

                return result;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库保存异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        public async UniTask<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            try
            {
                if (!_isInitialized)
                {
                    return (null, DataOperationResult.NotInitialized);
                }

                var sql = $"SELECT data FROM {_tableName} WHERE key = @key";
                var parameters = new Dictionary<string, object> { ["key"] = key };

                var (result, dataResult) = await _databaseProvider.QuerySingleAsync<byte[]>(sql, parameters);
                if (dataResult == DataOperationResult.Success)
                {
                    FrameworkLogger.Debug($"数据库加载成功: {key}", LogCategory.Core);
                    return (result, DataOperationResult.Success);
                }
                else if (dataResult == DataOperationResult.NotFound)
                {
                    return (null, DataOperationResult.NotFound);
                }
                else
                {
                    FrameworkLogger.Error($"数据库加载失败: {key}, 结果: {dataResult}", LogCategory.Core);
                    return (null, dataResult);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库加载异常: {ex.Message}", LogCategory.Core);
                return (null, DataOperationResult.Failed);
            }
        }

        public async UniTask<DataOperationResult> DeleteAsync(string key)
        {
            try
            {
                if (!_isInitialized)
                {
                    return DataOperationResult.NotInitialized;
                }

                var sql = $"DELETE FROM {_tableName} WHERE key = @key";
                var parameters = new Dictionary<string, object> { ["key"] = key };

                var result = await _databaseProvider.ExecuteAsync(sql, parameters);
                if (result == DataOperationResult.Success)
                {
                    FrameworkLogger.Debug($"数据库删除成功: {key}", LogCategory.Core);
                }
                else
                {
                    FrameworkLogger.Error($"数据库删除失败: {key}, 结果: {result}", LogCategory.Core);
                }

                return result;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库删除异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        public async UniTask<bool> ExistsAsync(string key)
        {
            try
            {
                if (!_isInitialized)
                {
                    return false;
                }

                var sql = $"SELECT COUNT(*) FROM {_tableName} WHERE key = @key";
                var parameters = new Dictionary<string, object> { ["key"] = key };

                var (result, dataResult) = await _databaseProvider.QuerySingleAsync<int>(sql, parameters);
                return dataResult == DataOperationResult.Success && result > 0;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库存在检查异常: {ex.Message}", LogCategory.Core);
                return false;
            }
        }

        public async UniTask<string[]> GetAllKeysAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    return new string[0];
                }

                var sql = $"SELECT key FROM {_tableName}";
                var (results, dataResult) = await _databaseProvider.QueryAsync<string>(sql);
                
                if (dataResult == DataOperationResult.Success)
                {
                    return results.ToArray();
                }
                else
                {
                    FrameworkLogger.Error($"获取所有键失败: {dataResult}", LogCategory.Core);
                    return new string[0];
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"获取所有键异常: {ex.Message}", LogCategory.Core);
                return new string[0];
            }
        }

        public async UniTask<DataOperationResult> ClearAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    return DataOperationResult.NotInitialized;
                }

                var sql = $"DELETE FROM {_tableName}";
                var result = await _databaseProvider.ExecuteAsync(sql);
                
                if (result == DataOperationResult.Success)
                {
                    FrameworkLogger.Info("数据库清空成功", LogCategory.Core);
                }
                else
                {
                    FrameworkLogger.Error($"数据库清空失败: {result}", LogCategory.Core);
                }

                return result;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库清空异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        public async UniTask<StorageStatistics> GetStatisticsAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    return new StorageStatistics();
                }

                var countSql = $"SELECT COUNT(*) FROM {_tableName}";
                var sizeSql = $"SELECT SUM(LENGTH(data)) FROM {_tableName}";
                
                var (count, countResult) = await _databaseProvider.QuerySingleAsync<int>(countSql);
                var (size, sizeResult) = await _databaseProvider.QuerySingleAsync<long>(sizeSql);
                
                if (countResult == DataOperationResult.Success && sizeResult == DataOperationResult.Success)
                {
                    return new StorageStatistics
                    {
                        ItemCount = count,
                        TotalSize = size,
                        StorageType = StorageType.Database
                    };
                }
                else
                {
                    FrameworkLogger.Error($"获取数据库统计信息失败: countResult={countResult}, sizeResult={sizeResult}", LogCategory.Core);
                    return new StorageStatistics();
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"获取数据库统计信息异常: {ex.Message}", LogCategory.Core);
                return new StorageStatistics();
            }
        }

        public async UniTask<DataOperationResult> SaveAsync<T>(string key, T data)
        {
            try
            {
                // 序列化数据为字节数组
                var serializer = Serialization.SerializerFactory.CreateSerializer(SerializationFormat.Json);
                var bytes = await serializer.SerializeAsync(data);
                
                // 调用字节数组保存方法
                return await SaveAsync(key, bytes);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库泛型保存异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        public async UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key)
        {
            try
            {
                // 调用字节数组加载方法
                var (bytes, result) = await LoadAsync(key);
                if (result != DataOperationResult.Success)
                {
                    return (default(T), result);
                }

                // 反序列化数据
                var serializer = Serialization.SerializerFactory.CreateSerializer(SerializationFormat.Json);
                var data = await serializer.DeserializeAsync<T>(bytes);
                
                return (data, DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库泛型加载异常: {ex.Message}", LogCategory.Core);
                return (default(T), DataOperationResult.Failed);
            }
        }

        public void Dispose()
        {
            try
            {
                _databaseProvider?.CloseAsync();
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"数据库存储提供者释放异常: {ex.Message}", LogCategory.Core);
            }
        }

        #region 私有方法

        private string BuildConnectionString(StorageConfig config)
        {
            // 根据数据库类型构建连接字符串
            switch (_databaseProvider.DatabaseType)
            {
                case DatabaseType.SQLite:
                    return $"Data Source={config?.databasePath ?? "game_data.db"};Version=3;";
                case DatabaseType.MySQL:
                    return $"Server={config?.databaseHost ?? "localhost"};Database={config?.databaseName ?? "game_data"};Uid={config?.databaseUser ?? "root"};Pwd={config?.databasePassword ?? ""};";
                case DatabaseType.PostgreSQL:
                    return $"Host={config?.databaseHost ?? "localhost"};Database={config?.databaseName ?? "game_data"};Username={config?.databaseUser ?? "postgres"};Password={config?.databasePassword ?? ""};";
                default:
                    return "";
            }
        }

        private async UniTask CreateDataTableAsync()
        {
            try
            {
                var exists = await _databaseProvider.TableExistsAsync(_tableName);
                if (!exists)
                {
                    var columns = new Dictionary<string, string>
                    {
                        ["key"] = GetKeyColumnType(),
                        ["data"] = GetDataColumnType(),
                        ["created_at"] = GetDateTimeColumnType(),
                        ["updated_at"] = GetDateTimeColumnType()
                    };

                    await _databaseProvider.CreateTableAsync(_tableName, columns);
                    FrameworkLogger.Info($"数据表 {_tableName} 创建成功", LogCategory.Core);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"创建数据表失败: {ex.Message}", LogCategory.Core);
            }
        }

        private string GetUpsertSql()
        {
            // 根据数据库类型生成UPSERT SQL
            switch (_databaseProvider.DatabaseType)
            {
                case DatabaseType.SQLite:
                    return $"INSERT OR REPLACE INTO {_tableName} (key, data, created_at, updated_at) VALUES (@key, @data, @created_at, @updated_at)";
                case DatabaseType.MySQL:
                    return $"INSERT INTO {_tableName} (key, data, created_at, updated_at) VALUES (@key, @data, @created_at, @updated_at) ON DUPLICATE KEY UPDATE data = @data, updated_at = @updated_at";
                case DatabaseType.PostgreSQL:
                    return $"INSERT INTO {_tableName} (key, data, created_at, updated_at) VALUES (@key, @data, @created_at, @updated_at) ON CONFLICT (key) DO UPDATE SET data = @data, updated_at = @updated_at";
                default:
                    return $"INSERT OR REPLACE INTO {_tableName} (key, data, created_at, updated_at) VALUES (@key, @data, @created_at, @updated_at)";
            }
        }

        private string GetKeyColumnType()
        {
            switch (_databaseProvider.DatabaseType)
            {
                case DatabaseType.SQLite:
                    return "TEXT PRIMARY KEY";
                case DatabaseType.MySQL:
                    return "VARCHAR(255) PRIMARY KEY";
                case DatabaseType.PostgreSQL:
                    return "VARCHAR(255) PRIMARY KEY";
                default:
                    return "TEXT PRIMARY KEY";
            }
        }

        private string GetDataColumnType()
        {
            switch (_databaseProvider.DatabaseType)
            {
                case DatabaseType.SQLite:
                    return "BLOB";
                case DatabaseType.MySQL:
                    return "LONGBLOB";
                case DatabaseType.PostgreSQL:
                    return "BYTEA";
                default:
                    return "BLOB";
            }
        }

        private string GetDateTimeColumnType()
        {
            switch (_databaseProvider.DatabaseType)
            {
                case DatabaseType.SQLite:
                    return "DATETIME";
                case DatabaseType.MySQL:
                    return "DATETIME";
                case DatabaseType.PostgreSQL:
                    return "TIMESTAMP";
                default:
                    return "DATETIME";
            }
        }

        #endregion
    }
}
