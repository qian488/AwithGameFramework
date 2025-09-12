using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据库提供者接口
    /// 支持多种数据库的统一抽象
    /// </summary>
    public interface IDatabaseProvider
    {
        /// <summary>
        /// 数据库类型
        /// </summary>
        DatabaseType DatabaseType { get; }
        
        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// 连接字符串
        /// </summary>
        string ConnectionString { get; }
        
        /// <summary>
        /// 初始化数据库连接
        /// </summary>
        /// <param name="connectionString">连接字符串</param>
        /// <returns>初始化结果</returns>
        UniTask<DataOperationResult> InitializeAsync(string connectionString);
        
        /// <summary>
        /// 关闭数据库连接
        /// </summary>
        /// <returns>关闭结果</returns>
        UniTask<DataOperationResult> CloseAsync();
        
        /// <summary>
        /// 执行SQL命令
        /// </summary>
        /// <param name="sql">SQL命令</param>
        /// <param name="parameters">参数</param>
        /// <returns>执行结果</returns>
        UniTask<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null);
        
        /// <summary>
        /// 查询数据
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="sql">查询SQL</param>
        /// <param name="parameters">参数</param>
        /// <returns>查询结果</returns>
        UniTask<(List<T> data, DataOperationResult result)> QueryAsync<T>(string sql, Dictionary<string, object> parameters = null);
        
        /// <summary>
        /// 查询单条数据
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="sql">查询SQL</param>
        /// <param name="parameters">参数</param>
        /// <returns>查询结果</returns>
        UniTask<(T data, DataOperationResult result)> QuerySingleAsync<T>(string sql, Dictionary<string, object> parameters = null);
        
        /// <summary>
        /// 开始事务
        /// </summary>
        /// <returns>事务对象</returns>
        UniTask<IDatabaseTransaction> BeginTransactionAsync();
        
        /// <summary>
        /// 检查表是否存在
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>是否存在</returns>
        UniTask<bool> TableExistsAsync(string tableName);
        
        /// <summary>
        /// 创建表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="columns">列定义</param>
        /// <returns>创建结果</returns>
        UniTask<DataOperationResult> CreateTableAsync(string tableName, Dictionary<string, string> columns);
        
        /// <summary>
        /// 删除表
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <returns>删除结果</returns>
        UniTask<DataOperationResult> DropTableAsync(string tableName);
    }
    
    /// <summary>
    /// 数据库事务接口
    /// </summary>
    public interface IDatabaseTransaction : IDisposable
    {
        /// <summary>
        /// 提交事务
        /// </summary>
        /// <returns>提交结果</returns>
        UniTask<DataOperationResult> CommitAsync();
        
        /// <summary>
        /// 回滚事务
        /// </summary>
        /// <returns>回滚结果</returns>
        UniTask<DataOperationResult> RollbackAsync();
        
        /// <summary>
        /// 执行SQL命令
        /// </summary>
        /// <param name="sql">SQL命令</param>
        /// <param name="parameters">参数</param>
        /// <returns>执行结果</returns>
        UniTask<DataOperationResult> ExecuteAsync(string sql, Dictionary<string, object> parameters = null);
    }
    
    /// <summary>
    /// 数据库类型枚举
    /// </summary>
    public enum DatabaseType
    {
        /// <summary>SQLite</summary>
        SQLite,
        /// <summary>MySQL</summary>
        MySQL,
        /// <summary>PostgreSQL</summary>
        PostgreSQL,
        /// <summary>MongoDB</summary>
        MongoDB,
        /// <summary>Redis</summary>
        Redis,
        /// <summary>SQL Server</summary>
        SQLServer
    }
}
