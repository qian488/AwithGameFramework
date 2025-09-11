using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using AwithGameFrame.DataPersistence.Storage;
using AwithGameFrame.Logging;

namespace AwithGameFrame.DataPersistence.Utils
{
    /// <summary>
    /// 存储操作辅助类
    /// 提供存储操作的通用逻辑
    /// </summary>
    public static class StorageHelper
    {
        /// <summary>
        /// 执行保存操作
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="data">数据</param>
        /// <param name="storageType">存储类型</param>
        /// <param name="storageProviders">存储提供者字典</param>
        /// <param name="context">上下文信息</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> SaveDataAsync<T>(
            string key, 
            T data, 
            StorageType storageType, 
            Dictionary<StorageType, IStorageProvider> storageProviders,
            string context = "DataPersistence")
        {
            try
            {
                // 验证输入参数
                if (!ValidationHelper.ValidateKey(key, context) || 
                    !ValidationHelper.ValidateData(data, context) || 
                    !ValidationHelper.ValidateStorageType(storageType, context))
                {
                    return DataOperationResult.Failed;
                }

                if (storageProviders.TryGetValue(storageType, out var provider))
                {
                    var result = await provider.SaveAsync(key, data);
                    if (result == DataOperationResult.Success)
                    {
                        FrameworkLogger.Debug($"[{context}] 数据保存成功: {key} -> {storageType}", LogCategory.Core);
                    }
                    else
                    {
                        FrameworkLogger.Warn($"[{context}] 数据保存失败: {key}, 错误: {result}", LogCategory.Core);
                    }
                    return result;
                }

                FrameworkLogger.Error($"[{context}] 不支持的存储类型: {storageType}", LogCategory.Core);
                return DataOperationResult.UnsupportedStorageType;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"[{context}] 保存数据时发生异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        /// <summary>
        /// 执行加载操作
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型</param>
        /// <param name="storageProviders">存储提供者字典</param>
        /// <param name="context">上下文信息</param>
        /// <returns>数据和操作结果</returns>
        public static async UniTask<(T data, DataOperationResult result)> LoadDataAsync<T>(
            string key, 
            StorageType storageType, 
            Dictionary<StorageType, IStorageProvider> storageProviders,
            string context = "DataPersistence")
        {
            try
            {
                // 验证输入参数
                if (!ValidationHelper.ValidateKey(key, context) || 
                    !ValidationHelper.ValidateStorageType(storageType, context))
                {
                    return (default(T), DataOperationResult.Failed);
                }

                if (storageProviders.TryGetValue(storageType, out var provider))
                {
                    var (data, result) = await provider.LoadAsync<T>(key);
                    if (result == DataOperationResult.Success)
                    {
                        FrameworkLogger.Debug($"[{context}] 数据加载成功: {key} -> {storageType}", LogCategory.Core);
                    }
                    else
                    {
                        FrameworkLogger.Warn($"[{context}] 数据加载失败: {key}, 错误: {result}", LogCategory.Core);
                    }
                    return (data, result);
                }

                FrameworkLogger.Error($"[{context}] 不支持的存储类型: {storageType}", LogCategory.Core);
                return (default(T), DataOperationResult.UnsupportedStorageType);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"[{context}] 加载数据时发生异常: {ex.Message}", LogCategory.Core);
                return (default(T), DataOperationResult.Failed);
            }
        }

        /// <summary>
        /// 执行删除操作
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型</param>
        /// <param name="storageProviders">存储提供者字典</param>
        /// <param name="context">上下文信息</param>
        /// <returns>操作结果</returns>
        public static async UniTask<DataOperationResult> DeleteDataAsync(
            string key, 
            StorageType storageType, 
            Dictionary<StorageType, IStorageProvider> storageProviders,
            string context = "DataPersistence")
        {
            try
            {
                // 验证输入参数
                if (!ValidationHelper.ValidateKey(key, context) || 
                    !ValidationHelper.ValidateStorageType(storageType, context))
                {
                    return DataOperationResult.Failed;
                }

                if (storageProviders.TryGetValue(storageType, out var provider))
                {
                    var result = await provider.DeleteAsync(key);
                    if (result == DataOperationResult.Success)
                    {
                        FrameworkLogger.Debug($"[{context}] 数据删除成功: {key} -> {storageType}", LogCategory.Core);
                    }
                    else
                    {
                        FrameworkLogger.Warn($"[{context}] 数据删除失败: {key}, 错误: {result}", LogCategory.Core);
                    }
                    return result;
                }

                FrameworkLogger.Error($"[{context}] 不支持的存储类型: {storageType}", LogCategory.Core);
                return DataOperationResult.UnsupportedStorageType;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"[{context}] 删除数据时发生异常: {ex.Message}", LogCategory.Core);
                return DataOperationResult.Failed;
            }
        }

        /// <summary>
        /// 检查数据是否存在
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="storageType">存储类型</param>
        /// <param name="storageProviders">存储提供者字典</param>
        /// <param name="context">上下文信息</param>
        /// <returns>是否存在</returns>
        public static async UniTask<bool> ExistsDataAsync(
            string key, 
            StorageType storageType, 
            Dictionary<StorageType, IStorageProvider> storageProviders,
            string context = "DataPersistence")
        {
            try
            {
                // 验证输入参数
                if (!ValidationHelper.ValidateKey(key, context) || 
                    !ValidationHelper.ValidateStorageType(storageType, context))
                {
                    return false;
                }

                if (storageProviders.TryGetValue(storageType, out var provider))
                {
                    var exists = await provider.ExistsAsync(key);
                    FrameworkLogger.Debug($"[{context}] 数据存在检查: {key} -> {exists}", LogCategory.Core);
                    return exists;
                }

                FrameworkLogger.Error($"[{context}] 不支持的存储类型: {storageType}", LogCategory.Core);
                return false;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"[{context}] 检查数据存在时发生异常: {ex.Message}", LogCategory.Core);
                return false;
            }
        }
    }
}
