using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 使用宿主提供的 ADO.NET 连接执行数据库操作。
    /// 保留已有 SQLite 表结构查询规则，其他 SQL 方言应提供对应的 IDatabaseProvider。
    /// </summary>
    public class AdoDatabaseProvider : IDatabaseProvider
    {
        private readonly Func<string, IDbConnection> connectionFactory;
        private IDbConnection _connection;
        private IDbTransaction _currentTransaction;

        public AdoDatabaseProvider(DatabaseType databaseType, Func<string, IDbConnection> connectionFactory)
        {
            DatabaseType = databaseType;
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public DatabaseType DatabaseType { get; }
        public bool IsConnected => _connection?.State == ConnectionState.Open;
        public string ConnectionString { get; private set; }

        public Task<DataOperationResult> InitializeAsync(string connectionString)
        {
            try
            {
                ConnectionString = connectionString;
                
                // 连接类型与驱动由宿主提供。
                _connection = connectionFactory(connectionString) ?? throw new InvalidOperationException("The connection factory returned null.");
                _connection.Open();
                
                FrameworkLogger.Info($"{DatabaseType}数据库连接已建立", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"{DatabaseType}数据库初始化失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> CloseAsync()
        {
            try
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Rollback();
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }

                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }

                FrameworkLogger.Info($"{DatabaseType}数据库连接已关闭", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"关闭{DatabaseType}数据库连接失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotInitialized);
                }

                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    
                    // 添加参数
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = param.Key;
                        parameter.Value = param.Value ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                        }
                    }
                    
                    command.ExecuteNonQuery();
                    FrameworkLogger.Info($"SQL执行成功: {sql}", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.Success);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"执行SQL命令失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(List<T> data, DataOperationResult result)> QueryAsync<T>(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult((new List<T>(), DataOperationResult.NotInitialized));
                }

                var results = new List<T>();
                
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    
                    // 添加参数
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            var parameter = command.CreateParameter();
                            parameter.ParameterName = param.Key;
                            parameter.Value = param.Value ?? DBNull.Value;
                            command.Parameters.Add(parameter);
                        }
                    }
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // 简单的对象映射 - 实际项目中可能需要更复杂的ORM
                            var obj = MapReaderToObject<T>(reader);
                            if (obj != null)
                            {
                                results.Add(obj);
                            }
                        }
                    }
                }
                
                FrameworkLogger.Info($"查询完成，返回 {results.Count} 条记录", LogCategory.Core);
                return Task.FromResult((results, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"查询数据失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult((new List<T>(), DataOperationResult.Failed));
            }
        }

        public Task<(T data, DataOperationResult result)> QuerySingleAsync<T>(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult((default(T), DataOperationResult.NotInitialized));
                }

                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    
                    // 添加参数
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            var parameter = command.CreateParameter();
                            parameter.ParameterName = param.Key;
                            parameter.Value = param.Value ?? DBNull.Value;
                            command.Parameters.Add(parameter);
                        }
                    }
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // 对于基本类型，直接转换
                            if (typeof(T) == typeof(string))
                            {
                                var value = reader.IsDBNull(0) ? default(T) : (T)(object)reader.GetString(0);
                                FrameworkLogger.Info("单条查询成功", LogCategory.Core);
                                return Task.FromResult((value, DataOperationResult.Success));
                            }
                            if (typeof(T) == typeof(int))
                            {
                                var value = reader.IsDBNull(0) ? default(T) : (T)(object)reader.GetInt32(0);
                                FrameworkLogger.Info("单条查询成功", LogCategory.Core);
                                return Task.FromResult((value, DataOperationResult.Success));
                            }
                            if (typeof(T) == typeof(float))
                            {
                                var value = reader.IsDBNull(0) ? default(T) : (T)(object)reader.GetFloat(0);
                                FrameworkLogger.Info("单条查询成功", LogCategory.Core);
                                return Task.FromResult((value, DataOperationResult.Success));
                            }
                            if (typeof(T) == typeof(bool))
                            {
                                var value = reader.IsDBNull(0) ? default(T) : (T)(object)reader.GetBoolean(0);
                                FrameworkLogger.Info("单条查询成功", LogCategory.Core);
                                return Task.FromResult((value, DataOperationResult.Success));
                            }
                            
                            // 对于复杂对象，使用映射方法
                            var obj = MapReaderToObject<T>(reader);
                            FrameworkLogger.Info("单条查询成功", LogCategory.Core);
                            return Task.FromResult((obj, DataOperationResult.Success));
                        }
                        else
                        {
                            FrameworkLogger.Info("未找到匹配的记录", LogCategory.Core);
                            return Task.FromResult((default(T), DataOperationResult.NotFound));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"查询单条数据失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync()
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult<IDatabaseTransaction>(null);
                }

                _currentTransaction = _connection.BeginTransaction();
                FrameworkLogger.Info("事务已开始", LogCategory.Core);
                return Task.FromResult<IDatabaseTransaction>(new SQLiteTransaction(_currentTransaction));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"开始事务失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult<IDatabaseTransaction>(null);
            }
        }

        public Task<bool> TableExistsAsync(string tableName)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult(false);
                }

                var sql = "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName";
                var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
                
                var (result, dataResult) = QuerySingleAsync<string>(sql, parameters).GetAwaiter().GetResult();
                var exists = dataResult == DataOperationResult.Success && !string.IsNullOrEmpty(result);
                
                FrameworkLogger.Info($"表 '{tableName}' 存在性检查: {exists}", LogCategory.Core);
                return Task.FromResult(exists);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"检查表是否存在失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(false);
            }
        }

        public Task<DataOperationResult> CreateTableAsync(string tableName, Dictionary<string, string> columns)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotInitialized);
                }

                // 构建CREATE TABLE SQL
                var columnDefinitions = new List<string>();
                foreach (var column in columns)
                {
                    columnDefinitions.Add($"{column.Key} {column.Value}");
                }
                
                var sql = $"CREATE TABLE IF NOT EXISTS {tableName} ({string.Join(", ", columnDefinitions)})";
                var result = ExecuteAsync(sql).GetAwaiter().GetResult();
                
                if (result == DataOperationResult.Success)
                {
                    FrameworkLogger.Info($"表 '{tableName}' 创建成功", LogCategory.Core);
                }
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"创建表失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> DropTableAsync(string tableName)
        {
            try
            {
                if (!IsConnected)
                {
                    FrameworkLogger.Error("数据库未连接", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotInitialized);
                }

                var sql = $"DROP TABLE IF EXISTS {tableName}";
                var result = ExecuteAsync(sql).GetAwaiter().GetResult();
                
                if (result == DataOperationResult.Success)
                {
                    FrameworkLogger.Info($"表 '{tableName}' 删除成功", LogCategory.Core);
                }
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"删除表失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        /// <summary>
        /// 将DataReader映射到对象
        /// 这是一个简单的实现，实际项目中可能需要更复杂的ORM
        /// </summary>
        private T MapReaderToObject<T>(IDataReader reader)
        {
            try
            {
                // 对于基本类型，直接返回
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)reader.GetString(0);
                }
                if (typeof(T) == typeof(int))
                {
                    return (T)(object)reader.GetInt32(0);
                }
                if (typeof(T) == typeof(float))
                {
                    return (T)(object)reader.GetFloat(0);
                }
                if (typeof(T) == typeof(double))
                {
                    return (T)(object)reader.GetDouble(0);
                }
                if (typeof(T) == typeof(bool))
                {
                    return (T)(object)reader.GetBoolean(0);
                }
                if (typeof(T) == typeof(long))
                {
                    return (T)(object)reader.GetInt64(0);
                }
                if (typeof(T) == typeof(ulong))
                {
                    return (T)(object)reader.GetInt64(0);
                }
                if (typeof(T) == typeof(byte[]))
                {
                    // 处理byte[]类型
                    var value = reader.GetValue(0);
                    if (value is byte[] byteArray)
                    {
                        return (T)(object)byteArray;
                    }
                    else if (value is string str)
                    {
                        // 如果是字符串，尝试转换为byte[]
                        return (T)(object)System.Text.Encoding.UTF8.GetBytes(str);
                    }
                    else
                    {
                        return default(T);
                    }
                }
                
                // 对于复杂对象，这里提供基础实现
                // 实际项目中建议使用专门的ORM库
                try
                {
                    // 检查是否有无参构造函数
                    var constructor = typeof(T).GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        FrameworkLogger.Error($"类型 {typeof(T).Name} 没有无参构造函数", LogCategory.Core);
                        return default(T);
                    }
                    
                    var obj = Activator.CreateInstance<T>();
                    var properties = typeof(T).GetProperties();
                    
                    for (int i = 0; i < reader.FieldCount && i < properties.Length; i++)
                    {
                        var property = properties[i];
                        if (property.CanWrite)
                        {
                            try
                            {
                                var value = reader.GetValue(i);
                                if (value != DBNull.Value)
                                {
                                    property.SetValue(obj, value);
                                }
                            }
                            catch (Exception ex)
                            {
                                FrameworkLogger.Warn($"映射属性 {property.Name} 失败: {ex.Message}", LogCategory.Core);
                            }
                        }
                    }
                    
                    return obj;
                }
                catch (Exception ex)
                {
                    FrameworkLogger.Error($"创建对象实例失败: {ex.Message}", LogCategory.Core);
                    return default(T);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"对象映射失败: {ex.Message}", LogCategory.Core);
                return default(T);
            }
        }
    }

    /// <summary>
    /// ADO.NET 事务实现；保留 SQLiteTransaction 名称以兼容已有调用。
    /// </summary>
    public class SQLiteTransaction : IDatabaseTransaction
    {
        private readonly IDbTransaction _transaction;
        private readonly IDbConnection _connection;

        public SQLiteTransaction(IDbTransaction transaction)
        {
            _transaction = transaction;
            _connection = transaction.Connection;
        }

        public Task<DataOperationResult> CommitAsync()
        {
            try
            {
                _transaction.Commit();
                FrameworkLogger.Info("事务提交成功", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"事务提交失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> RollbackAsync()
        {
            try
            {
                _transaction.Rollback();
                FrameworkLogger.Info("事务回滚成功", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"事务回滚失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = _transaction;
                    command.CommandText = sql;
                    
                    // 添加参数
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            var parameter = command.CreateParameter();
                            parameter.ParameterName = param.Key;
                            parameter.Value = param.Value ?? DBNull.Value;
                            command.Parameters.Add(parameter);
                        }
                    }
                    
                    command.ExecuteNonQuery();
                    FrameworkLogger.Info($"事务中SQL执行成功: {sql}", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.Success);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"事务中执行SQL命令失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
        }
    }
}
