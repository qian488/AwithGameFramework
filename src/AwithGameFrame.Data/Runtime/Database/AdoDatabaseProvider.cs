using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 使用宿主提供的 ADO.NET 连接执行数据库操作。
    /// 表结构辅助方法使用 SQLite 方言，其他 SQL 方言应提供对应的 IDatabaseProvider。
    /// 实例及其连接不支持并发操作。
    /// </summary>
    public class AdoDatabaseProvider : IDatabaseProvider
    {
        private readonly Func<string, IDbConnection> connectionFactory;
        private IDbConnection connection;
        private SQLiteTransaction currentTransaction;

        public AdoDatabaseProvider(DatabaseType databaseType, Func<string, IDbConnection> connectionFactory)
        {
            DatabaseType = databaseType;
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public DatabaseType DatabaseType { get; }
        public bool IsConnected => connection?.State == ConnectionState.Open;
        public string ConnectionString { get; private set; }

        public Task<DataOperationResult> InitializeAsync(string connectionString)
        {
            if (ReleaseResources() != DataOperationResult.Success)
                return Task.FromResult(DataOperationResult.Failed);

            IDbConnection candidate = null;
            try
            {
                ConnectionString = connectionString;
                candidate = connectionFactory(connectionString) ?? throw new InvalidOperationException("The connection factory returned null.");
                candidate.Open();
                connection = candidate;
                FrameworkLogger.Info($"{DatabaseType}数据库连接已建立", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                connection = null;
                try { candidate?.Dispose(); }
                catch (Exception disposeError) { LogFailure("释放未成功打开的连接失败", disposeError); }
                LogFailure($"{DatabaseType}数据库初始化失败", ex);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> CloseAsync() => Task.FromResult(ReleaseResources());

        private DataOperationResult ReleaseResources()
        {
            var transaction = currentTransaction;
            currentTransaction = null;
            var ownedConnection = connection;
            connection = null;
            var result = DataOperationResult.Success;

            // Each resource gets a cleanup attempt even if an earlier operation fails.
            if (transaction != null && transaction.RollbackAsync().GetAwaiter().GetResult() != DataOperationResult.Success)
                result = DataOperationResult.Failed;
            if (ownedConnection != null)
            {
                try { ownedConnection.Close(); }
                catch (Exception ex) { LogFailure("关闭数据库连接失败", ex); result = DataOperationResult.Failed; }
                try { ownedConnection.Dispose(); }
                catch (Exception ex) { LogFailure("释放数据库连接失败", ex); result = DataOperationResult.Failed; }
            }
            return result;
        }

        public Task<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected) return Task.FromResult(DataOperationResult.NotInitialized);
                using (var command = CreateCommand(connection, currentTransaction?.NativeTransaction, sql, parameters))
                    command.ExecuteNonQuery();
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                LogFailure("执行SQL命令失败", ex);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(List<T> data, DataOperationResult result)> QueryAsync<T>(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected) return Task.FromResult((new List<T>(), DataOperationResult.NotInitialized));
                var results = new List<T>();
                using (var command = CreateCommand(connection, currentTransaction?.NativeTransaction, sql, parameters))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) results.Add(MapReaderToObject<T>(reader));
                }
                return Task.FromResult((results, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                LogFailure("查询数据失败", ex);
                return Task.FromResult((new List<T>(), DataOperationResult.Failed));
            }
        }

        public Task<(T data, DataOperationResult result)> QuerySingleAsync<T>(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                if (!IsConnected) return Task.FromResult((default(T), DataOperationResult.NotInitialized));
                using (var command = CreateCommand(connection, currentTransaction?.NativeTransaction, sql, parameters))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return Task.FromResult((default(T), DataOperationResult.NotFound));
                    return Task.FromResult((MapReaderToObject<T>(reader), DataOperationResult.Success));
                }
            }
            catch (Exception ex)
            {
                LogFailure("查询单条数据失败", ex);
                return Task.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync()
        {
            try
            {
                if (!IsConnected || currentTransaction != null) return Task.FromResult<IDatabaseTransaction>(null);
                currentTransaction = new SQLiteTransaction(connection.BeginTransaction(), completed =>
                {
                    if (ReferenceEquals(currentTransaction, completed)) currentTransaction = null;
                });
                return Task.FromResult<IDatabaseTransaction>(currentTransaction);
            }
            catch (Exception ex)
            {
                LogFailure("开始事务失败", ex);
                return Task.FromResult<IDatabaseTransaction>(null);
            }
        }

        /// <summary>使用 SQLite sqlite_master 查询；不是跨 SQL 方言的结构探测。</summary>
        public async Task<bool> TableExistsAsync(string tableName)
        {
            var result = await QuerySingleAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName",
                new Dictionary<string, object> { ["@tableName"] = tableName });
            return result.result == DataOperationResult.Success && !string.IsNullOrEmpty(result.data);
        }

        public Task<DataOperationResult> CreateTableAsync(string tableName, Dictionary<string, string> columns)
        {
            if (!IsConnected) return Task.FromResult(DataOperationResult.NotInitialized);
            try
            {
                var definitions = new List<string>();
                foreach (var column in columns) definitions.Add($"{column.Key} {column.Value}");
                return ExecuteAsync($"CREATE TABLE IF NOT EXISTS {tableName} ({string.Join(", ", definitions)})");
            }
            catch (Exception ex)
            {
                LogFailure("创建表失败", ex);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> DropTableAsync(string tableName) => ExecuteAsync($"DROP TABLE IF EXISTS {tableName}");

        internal static IDbCommand CreateCommand(IDbConnection connection, IDbTransaction transaction,
            string sql, Dictionary<string, object> parameters)
        {
            var command = connection.CreateCommand();
            try
            {
                command.CommandText = sql;
                if (transaction != null) command.Transaction = transaction;
                if (parameters != null)
                {
                    foreach (var entry in parameters)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = entry.Key;
                        parameter.Value = entry.Value ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                    }
                }
                return command;
            }
            catch
            {
                command.Dispose();
                throw;
            }
        }

        private static T MapReaderToObject<T>(IDataRecord reader)
        {
            var type = typeof(T);
            var valueType = Nullable.GetUnderlyingType(type) ?? type;
            if (IsScalar(valueType)) return (T)ConvertValue(reader.GetValue(0), type);

            var instance = Activator.CreateInstance(type);
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetSetMethod() != null && property.GetIndexParameters().Length == 0)
                    properties[property.Name] = property;
            }
            var fields = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.IsInitOnly && !field.IsLiteral) fields[field.Name] = field;
            }
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var name = reader.GetName(ordinal);
                if (properties.TryGetValue(name, out var property))
                    property.SetValue(instance, ConvertValue(reader.GetValue(ordinal), property.PropertyType));
                else if (fields.TryGetValue(name, out var field))
                    field.SetValue(instance, ConvertValue(reader.GetValue(ordinal), field.FieldType));
            }
            return (T)instance;
        }

        private static bool IsScalar(Type type)
        {
            return type == typeof(object) || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(byte[]);
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null || value == DBNull.Value)
                return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null ? Activator.CreateInstance(targetType) : null;
            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (type.IsInstanceOfType(value)) return value;
            if (type.IsEnum)
            {
                if (value is string name) return Enum.Parse(type, name, ignoreCase: true);
                return Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture));
            }
            if (type == typeof(Guid)) return value is byte[] guidBytes ? new Guid(guidBytes) : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type == typeof(DateTimeOffset)) return value is DateTime date ? new DateTimeOffset(date) : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (type == typeof(TimeSpan)) return TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (type == typeof(DateTime) && value is string timestamp) return DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (type == typeof(byte[]) && value is string text) return System.Text.Encoding.UTF8.GetBytes(text);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        internal static void LogFailure(string operation, Exception exception)
        {
            try { FrameworkLogger.Error($"{operation}: {exception.Message}", LogCategory.Core); }
            catch
            {
                // Host diagnostics must not interrupt rollback or resource cleanup.
            }
        }
    }

    /// <summary>ADO.NET 事务实现；保留 SQLiteTransaction 名称与构造函数以兼容已有调用。</summary>
    public class SQLiteTransaction : IDatabaseTransaction
    {
        private IDbTransaction transaction;
        private IDbConnection connection;
        private Action<SQLiteTransaction> onCompleted;

        public SQLiteTransaction(IDbTransaction transaction) : this(transaction, null) { }

        internal SQLiteTransaction(IDbTransaction transaction, Action<SQLiteTransaction> onCompleted)
        {
            this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            connection = transaction.Connection;
            this.onCompleted = onCompleted;
        }

        internal IDbTransaction NativeTransaction => transaction;

        public Task<DataOperationResult> CommitAsync() => Task.FromResult(Complete(commit: true));
        public Task<DataOperationResult> RollbackAsync() => Task.FromResult(Complete(commit: false));

        private DataOperationResult Complete(bool commit)
        {
            var pending = transaction;
            if (pending == null) return DataOperationResult.NotInitialized;
            transaction = null;
            connection = null;
            var completion = onCompleted;
            onCompleted = null;
            var result = DataOperationResult.Success;
            try
            {
                if (commit) pending.Commit();
                else pending.Rollback();
            }
            catch (Exception ex)
            {
                AdoDatabaseProvider.LogFailure(commit ? "事务提交失败" : "事务回滚失败", ex);
                result = DataOperationResult.Failed;
                if (commit)
                {
                    try { pending.Rollback(); }
                    catch (Exception rollbackError) { AdoDatabaseProvider.LogFailure("提交失败后回滚事务失败", rollbackError); }
                }
            }
            finally
            {
                try { pending.Dispose(); }
                catch (Exception ex) { AdoDatabaseProvider.LogFailure("释放事务失败", ex); result = DataOperationResult.Failed; }
                finally { completion?.Invoke(this); }
            }
            return result;
        }

        public Task<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null)
        {
            if (transaction == null) return Task.FromResult(DataOperationResult.NotInitialized);
            try
            {
                using (var command = AdoDatabaseProvider.CreateCommand(connection, transaction, sql, parameters))
                    command.ExecuteNonQuery();
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                AdoDatabaseProvider.LogFailure("事务中执行SQL命令失败", ex);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public void Dispose() { Complete(commit: false); }
    }
}
