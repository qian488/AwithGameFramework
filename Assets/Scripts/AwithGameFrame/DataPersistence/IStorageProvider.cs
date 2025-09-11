using System;
using Cysharp.Threading.Tasks;

namespace AwithGameFrame.DataPersistence
{
    /// <summary>
    /// 存储提供者接口
    /// </summary>
    public interface IStorageProvider
    {
        /// <summary>
        /// 存储类型
        /// </summary>
        StorageType StorageType { get; }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 初始化存储提供者（无配置）
        /// </summary>
        /// <returns>初始化结果</returns>
        UniTask<DataOperationResult> InitializeAsync();

        /// <summary>
        /// 初始化存储提供者（带配置）
        /// </summary>
        /// <param name="config">配置参数</param>
        /// <returns>初始化结果</returns>
        UniTask<DataOperationResult> InitializeAsync(object config);

        /// <summary>
        /// 保存数据（字节数组）
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="data">数据字节数组</param>
        /// <returns>操作结果</returns>
        UniTask<DataOperationResult> SaveAsync(string key, byte[] data);

        /// <summary>
        /// 加载数据（字节数组）
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>数据字节数组和操作结果</returns>
        UniTask<(byte[] data, DataOperationResult result)> LoadAsync(string key);

        /// <summary>
        /// 保存数据（泛型）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="data">数据</param>
        /// <returns>操作结果</returns>
        UniTask<DataOperationResult> SaveAsync<T>(string key, T data);

        /// <summary>
        /// 加载数据（泛型）
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>数据和操作结果</returns>
        UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key);

        /// <summary>
        /// 删除数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>操作结果</returns>
        UniTask<DataOperationResult> DeleteAsync(string key);

        /// <summary>
        /// 检查数据是否存在
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>是否存在</returns>
        UniTask<bool> ExistsAsync(string key);

        /// <summary>
        /// 获取所有数据键
        /// </summary>
        /// <returns>数据键列表</returns>
        UniTask<string[]> GetAllKeysAsync();

        /// <summary>
        /// 清空所有数据
        /// </summary>
        /// <returns>操作结果</returns>
        UniTask<DataOperationResult> ClearAsync();

        /// <summary>
        /// 获取存储统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        UniTask<StorageStatistics> GetStatisticsAsync();

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// 存储统计信息
    /// </summary>
    [Serializable]
    public class StorageStatistics
    {
        /// <summary>存储类型</summary>
        public StorageType StorageType;
        /// <summary>数据项数量</summary>
        public int ItemCount;
        /// <summary>总大小（字节）</summary>
        public long TotalSize;
        /// <summary>可用空间（字节）</summary>
        public long AvailableSpace;
        /// <summary>最后访问时间</summary>
        public DateTime LastAccessTime;
        /// <summary>最后修改时间</summary>
        public DateTime LastModifiedTime;
        /// <summary>是否健康</summary>
        public bool IsHealthy;
    }
}
