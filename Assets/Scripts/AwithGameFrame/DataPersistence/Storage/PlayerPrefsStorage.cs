using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using AwithGameFrame.Logging;

namespace AwithGameFrame.DataPersistence.Storage
{
    /// <summary>
    /// PlayerPrefs存储提供者
    /// </summary>
    public class PlayerPrefsStorage : IStorageProvider
    {
        private DataPersistenceConfig _config;
        private bool _isInitialized = false;
        private readonly HashSet<string> _managedKeys = new HashSet<string>();

        public StorageType StorageType => StorageType.PlayerPrefs;
        public bool IsInitialized => _isInitialized;

        public UniTask<DataOperationResult> InitializeAsync()
        {
            return InitializeAsync((DataPersistenceConfig)null);
        }

        public UniTask<DataOperationResult> InitializeAsync(object config)
        {
            try
            {
                _config = config as DataPersistenceConfig ?? new DataPersistenceConfig();
                _isInitialized = true;
                
                // 记录初始化日志
                FrameworkLogger.Info($"PlayerPrefsStorage initialized", LogCategory.Core);
                
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to initialize PlayerPrefsStorage: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            try
            {
                if (!_isInitialized)
                {
                    return UniTask.FromResult(DataOperationResult.NotInitialized);
                }

                // 将字节数组转换为Base64字符串存储
                var base64String = Convert.ToBase64String(data);
                PlayerPrefs.SetString(GetFullKey(key), base64String);
                PlayerPrefs.Save();
                
                _managedKeys.Add(key);
                
                FrameworkLogger.Debug($"PlayerPrefs保存成功: {key}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"PlayerPrefs保存失败: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            try
            {
                if (!_isInitialized)
                {
                    return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotInitialized));
                }

                var fullKey = GetFullKey(key);
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotFound));
                }

                var base64String = PlayerPrefs.GetString(fullKey);
                var data = Convert.FromBase64String(base64String);
                
                FrameworkLogger.Debug($"PlayerPrefs加载成功: {key}", LogCategory.Core);
                return UniTask.FromResult<(byte[], DataOperationResult)>((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"PlayerPrefs加载失败: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }
        }

        public UniTask<DataOperationResult> SaveAsync<T>(string key, T data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var fullKey = GetFullKey(key);
                var json = JsonUtility.ToJson(data, _config.PrettyPrint);
                
                if (_config.EnableEncryption)
                {
                    json = EncryptString(json);
                }
                
                PlayerPrefs.SetString(fullKey, json);
                PlayerPrefs.Save();
                
                // 添加到管理的键列表
                _managedKeys.Add(key);
                
                FrameworkLogger.Debug($"Data saved to PlayerPrefs: {key}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save data to PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return UniTask.FromResult((default(T), DataOperationResult.Failed));
            }

            try
            {
                var fullKey = GetFullKey(key);
                
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    FrameworkLogger.Debug($"Data not found in PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult((default(T), DataOperationResult.NotFound));
                }
                
                // 对于基本类型，直接使用PlayerPrefs的Get方法
                if (typeof(T) == typeof(string))
                {
                    var value = PlayerPrefs.GetString(fullKey);
                    if (_config.EnableEncryption)
                    {
                        value = DecryptString(value);
                    }
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(int))
                {
                    var value = PlayerPrefs.GetInt(fullKey);
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(float))
                {
                    var value = PlayerPrefs.GetFloat(fullKey);
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(bool))
                {
                    var value = PlayerPrefs.GetInt(fullKey) == 1;
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else
                {
                    // 对于复杂对象，使用JSON反序列化
                    var json = PlayerPrefs.GetString(fullKey);
                    
                    if (_config.EnableEncryption)
                    {
                        json = DecryptString(json);
                    }
                    
                    var data = JsonUtility.FromJson<T>(json);
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult((data, DataOperationResult.Success));
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load data from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public UniTask<DataOperationResult> DeleteAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var fullKey = GetFullKey(key);
                
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    FrameworkLogger.Debug($"Data not found in PlayerPrefs: {key}", LogCategory.Core);
                    return UniTask.FromResult(DataOperationResult.NotFound);
                }
                
                PlayerPrefs.DeleteKey(fullKey);
                PlayerPrefs.Save();
                
                // 从管理的键列表中移除
                _managedKeys.Remove(key);
                
                FrameworkLogger.Debug($"Data deleted from PlayerPrefs: {key}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to delete data from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<bool> ExistsAsync(string key)
        {
            if (!_isInitialized)
            {
                return UniTask.FromResult(false);
            }

            try
            {
                var fullKey = GetFullKey(key);
                var exists = PlayerPrefs.HasKey(fullKey);
                FrameworkLogger.Debug($"Data exists in PlayerPrefs: {key} = {exists}", LogCategory.Core);
                return UniTask.FromResult(exists);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to check data existence in PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(false);
            }
        }

        public UniTask<string[]> GetAllKeysAsync()
        {
            if (!_isInitialized)
            {
                return UniTask.FromResult(new string[0]);
            }

            try
            {
                // PlayerPrefs没有直接获取所有键的方法，这里返回管理的键列表
                // 通过维护一个键列表来跟踪所有保存的键
                var keys = _managedKeys.ToArray();
                FrameworkLogger.Debug($"PlayerPrefs returning {keys.Length} managed keys", LogCategory.Core);
                return UniTask.FromResult(keys);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get all keys from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(new string[0]);
            }
        }

        public UniTask<DataOperationResult> ClearAsync()
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                // 清除所有管理的键
                foreach (var key in _managedKeys)
                {
                    var fullKey = GetFullKey(key);
                    if (PlayerPrefs.HasKey(fullKey))
                    {
                        PlayerPrefs.DeleteKey(fullKey);
                    }
                }
                
                _managedKeys.Clear();
                PlayerPrefs.Save();
                
                FrameworkLogger.Info("All data cleared from PlayerPrefs", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to clear PlayerPrefs: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<StorageStatistics> GetStatisticsAsync()
        {
            if (!_isInitialized)
            {
                return UniTask.FromResult(new StorageStatistics());
            }

            try
            {
                var stats = new StorageStatistics
                {
                    StorageType = StorageType.PlayerPrefs,
                    ItemCount = _managedKeys.Count,
                    IsHealthy = _isInitialized,
                    LastAccessTime = DateTime.Now
                };

                FrameworkLogger.Debug($"PlayerPrefs statistics: {stats.ItemCount} keys", LogCategory.Core);
                return UniTask.FromResult(stats);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get PlayerPrefs statistics: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(new StorageStatistics());
            }
        }

        #region 私有方法

        private string GetFullKey(string key)
        {
            return string.IsNullOrEmpty(_config.KeyPrefix) ? key : $"{_config.KeyPrefix}_{key}";
        }

        private string EncryptString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            try
            {
                var key = _config.EncryptionKey ?? "DefaultKey";
                var result = "";
                for (int i = 0; i < input.Length; i++)
                {
                    result += (char)(input[i] ^ key[i % key.Length]);
                }
                return result;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to encrypt string: {ex.Message}", LogCategory.Core);
                return input;
            }
        }

        private string DecryptString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            try
            {
                var key = _config.EncryptionKey ?? "DefaultKey";
                var result = "";
                for (int i = 0; i < input.Length; i++)
                {
                    result += (char)(input[i] ^ key[i % key.Length]);
                }
                return result;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to decrypt string: {ex.Message}", LogCategory.Core);
                return input;
            }
        }

        #endregion

        public void Dispose()
        {
            _managedKeys?.Clear();
            _isInitialized = false;
            FrameworkLogger.Debug("PlayerPrefsStorage disposed", LogCategory.Core);
        }
    }
}