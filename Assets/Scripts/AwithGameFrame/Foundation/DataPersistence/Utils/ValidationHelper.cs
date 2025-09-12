using System;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据验证辅助类
    /// 提供数据验证和边界检查功能
    /// </summary>
    public static class ValidationHelper
    {
        /// <summary>
        /// 验证数据键
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="context">上下文信息</param>
        /// <returns>是否有效</returns>
        public static bool ValidateKey(string key, string context = "DataPersistenceAPI")
        {
            if (string.IsNullOrEmpty(key))
            {
                FrameworkLogger.Error($"[{context}] 数据键不能为空", LogCategory.Core);
                return false;
            }

            if (key.Length > 255)
            {
                FrameworkLogger.Error($"[{context}] 数据键长度不能超过255个字符: {key}", LogCategory.Core);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证数据对象
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="data">数据对象</param>
        /// <param name="context">上下文信息</param>
        /// <returns>是否有效</returns>
        public static bool ValidateData<T>(T data, string context = "DataPersistenceAPI")
        {
            if (data == null)
            {
                FrameworkLogger.Error($"[{context}] 数据对象不能为null", LogCategory.Core);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证存储类型
        /// </summary>
        /// <param name="storageType">存储类型</param>
        /// <param name="context">上下文信息</param>
        /// <returns>是否有效</returns>
        public static bool ValidateStorageType(StorageType storageType, string context = "DataPersistenceAPI")
        {
            if (!Enum.IsDefined(typeof(StorageType), storageType))
            {
                FrameworkLogger.Error($"[{context}] 无效的存储类型: {storageType}", LogCategory.Core);
                return false;
            }

            return true;
        }
    }
}
