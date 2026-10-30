using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// PlayerPrefs存储提供者
    /// </summary>
    public class PlayerPrefsStorage : IStorageProvider
    {
        private const string IndexKeyPrefix = "__AwithGameFrame.PlayerPrefs.Keys.v1__:";

        [Serializable]
        private sealed class ManagedKeyIndex
        {
            public int version = 1;
            public string[] keys;
        }

        private DataPersistenceConfig _config;
        private bool _isInitialized = false;
        private readonly HashSet<string> _managedKeys = new HashSet<string>();

        public StorageType StorageType => StorageType.PlayerPrefs;
        public bool IsInitialized => _isInitialized;

        public Task<DataOperationResult> InitializeAsync()
        {
            return InitializeAsync((DataPersistenceConfig)null);
        }

        public Task<DataOperationResult> InitializeAsync(object config)
        {
            try
            {
                _isInitialized = false;
                var source = config as DataPersistenceConfig ?? new DataPersistenceConfig();
                _config = new DataPersistenceConfig
                {
                    KeyPrefix = source.KeyPrefix,
                    PrettyPrint = source.PrettyPrint,
                    EnableEncryption = source.EnableEncryption,
                    EncryptionKey = source.EncryptionKey
                };
                if (_config.EnableEncryption && string.IsNullOrEmpty(_config.EncryptionKey))
                    throw new ArgumentException("PlayerPrefs encryption requires a nonempty key.", nameof(config));
                ReloadManagedKeys();
                _isInitialized = true;
                
                // 记录初始化日志
                FrameworkLogger.Info($"PlayerPrefsStorage initialized", LogCategory.Core);
                
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                _managedKeys.Clear();
                FrameworkLogger.Error($"Failed to initialize PlayerPrefsStorage: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            try
            {
                if (!_isInitialized)
                {
                    return Task.FromResult(DataOperationResult.NotInitialized);
                }

                // 将字节数组转换为Base64字符串存储
                var base64String = Convert.ToBase64String(data);
                var fullKey = GetFullKey(key);
                ReloadManagedKeys();
                PlayerPrefs.SetString(fullKey, base64String);
                _managedKeys.Add(key);
                SaveManagedKeys();
                PlayerPrefs.Save();
                
                FrameworkLogger.Debug($"PlayerPrefs保存成功: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"PlayerPrefs保存失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            try
            {
                if (!_isInitialized)
                {
                    return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotInitialized));
                }

                var fullKey = GetFullKey(key);
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotFound));
                }

                RememberKey(key);
                var base64String = PlayerPrefs.GetString(fullKey);
                var data = Convert.FromBase64String(base64String);
                
                FrameworkLogger.Debug($"PlayerPrefs加载成功: {key}", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"PlayerPrefs加载失败: {ex.Message}", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }
        }

        public Task<DataOperationResult> SaveAsync<T>(string key, T data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.NotInitialized);
            }

            try
            {
                var fullKey = GetFullKey(key);
                ReloadManagedKeys();
                if (typeof(T) == typeof(int))
                {
                    PlayerPrefs.SetInt(fullKey, (int)(object)data);
                }
                else if (typeof(T) == typeof(float))
                {
                    PlayerPrefs.SetFloat(fullKey, (float)(object)data);
                }
                else if (typeof(T) == typeof(bool))
                {
                    PlayerPrefs.SetInt(fullKey, (bool)(object)data ? 1 : 0);
                }
                else
                {
                    var json = typeof(T) == typeof(string) ? (string)(object)data : JsonUtility.ToJson(data, _config.PrettyPrint);
                    if (_config.EnableEncryption) json = EncryptString(json);
                    PlayerPrefs.SetString(fullKey, json);
                }
                
                // 添加到管理的键列表
                _managedKeys.Add(key);
                SaveManagedKeys();
                PlayerPrefs.Save();
                
                FrameworkLogger.Debug($"Data saved to PlayerPrefs: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save data to PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(T data, DataOperationResult result)> LoadAsync<T>(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return Task.FromResult((default(T), DataOperationResult.NotInitialized));
            }

            try
            {
                var fullKey = GetFullKey(key);
                
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    FrameworkLogger.Debug($"Data not found in PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult((default(T), DataOperationResult.NotFound));
                }
                
                RememberKey(key);
                // 对于基本类型，直接使用PlayerPrefs的Get方法
                if (typeof(T) == typeof(string))
                {
                    var value = PlayerPrefs.GetString(fullKey);
                    if (_config.EnableEncryption)
                    {
                        value = DecryptString(value);
                    }
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(int))
                {
                    var value = PlayerPrefs.GetInt(fullKey);
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(float))
                {
                    var value = PlayerPrefs.GetFloat(fullKey);
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult(((T)(object)value, DataOperationResult.Success));
                }
                else if (typeof(T) == typeof(bool))
                {
                    var value = PlayerPrefs.GetInt(fullKey) == 1;
                    FrameworkLogger.Debug($"Data loaded from PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult(((T)(object)value, DataOperationResult.Success));
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
                    return Task.FromResult((data, DataOperationResult.Success));
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load data from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public Task<DataOperationResult> DeleteAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.NotInitialized);
            }

            try
            {
                var fullKey = GetFullKey(key);
                ReloadManagedKeys();
                
                if (!PlayerPrefs.HasKey(fullKey))
                {
                    FrameworkLogger.Debug($"Data not found in PlayerPrefs: {key}", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotFound);
                }
                
                PlayerPrefs.DeleteKey(fullKey);
                
                // 从管理的键列表中移除
                _managedKeys.Remove(key);
                SaveManagedKeys();
                PlayerPrefs.Save();
                
                FrameworkLogger.Debug($"Data deleted from PlayerPrefs: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to delete data from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<bool> ExistsAsync(string key)
        {
            if (!_isInitialized)
            {
                return Task.FromResult(false);
            }

            try
            {
                var fullKey = GetFullKey(key);
                var exists = PlayerPrefs.HasKey(fullKey);
                if (exists) RememberKey(key);
                FrameworkLogger.Debug($"Data exists in PlayerPrefs: {key} = {exists}", LogCategory.Core);
                return Task.FromResult(exists);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to check data existence in PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult(false);
            }
        }

        public Task<string[]> GetAllKeysAsync()
        {
            if (!_isInitialized)
            {
                return Task.FromResult(new string[0]);
            }

            try
            {
                ReloadManagedKeys();
                var keys = _managedKeys.ToArray();
                FrameworkLogger.Debug($"PlayerPrefs returning {keys.Length} managed keys", LogCategory.Core);
                return Task.FromResult(keys);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get all keys from PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult(new string[0]);
            }
        }

        public Task<DataOperationResult> ClearAsync()
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("PlayerPrefsStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.NotInitialized);
            }

            try
            {
                ReloadManagedKeys();
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
                SaveManagedKeys();
                PlayerPrefs.Save();
                
                FrameworkLogger.Info("All data cleared from PlayerPrefs", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to clear PlayerPrefs: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<StorageStatistics> GetStatisticsAsync()
        {
            if (!_isInitialized)
            {
                return Task.FromResult(new StorageStatistics());
            }

            try
            {
                ReloadManagedKeys();
                var stats = new StorageStatistics
                {
                    StorageType = StorageType.PlayerPrefs,
                    ItemCount = _managedKeys.Count,
                    IsHealthy = _isInitialized,
                    LastAccessTime = DateTime.Now
                };

                FrameworkLogger.Debug($"PlayerPrefs statistics: {stats.ItemCount} keys", LogCategory.Core);
                return Task.FromResult(stats);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get PlayerPrefs statistics: {ex.Message}", LogCategory.Core);
                return Task.FromResult(new StorageStatistics());
            }
        }

        #region 私有方法

        private string GetFullKey(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var fullKey = string.IsNullOrEmpty(_config.KeyPrefix) ? key : $"{_config.KeyPrefix}_{key}";
            if (fullKey.StartsWith(IndexKeyPrefix, StringComparison.Ordinal))
                throw new ArgumentException("The PlayerPrefs key uses the reserved framework index namespace.", nameof(key));
            return fullKey;
        }

        private string GetIndexKey()
        {
            return IndexKeyPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(_config.KeyPrefix ?? string.Empty));
        }

        private void ReloadManagedKeys()
        {
            _managedKeys.Clear();
            var indexKey = GetIndexKey();
            if (!PlayerPrefs.HasKey(indexKey)) return;
            var index = JsonUtility.FromJson<ManagedKeyIndex>(PlayerPrefs.GetString(indexKey));
            if (index == null || index.version != 1 || index.keys == null)
                throw new InvalidOperationException("Invalid PlayerPrefs managed key index.");
            foreach (var key in index.keys)
                if (PlayerPrefs.HasKey(GetFullKey(key))) _managedKeys.Add(key);
        }

        private void SaveManagedKeys()
        {
            var indexKey = GetIndexKey();
            if (_managedKeys.Count == 0) PlayerPrefs.DeleteKey(indexKey);
            else PlayerPrefs.SetString(indexKey, JsonUtility.ToJson(new ManagedKeyIndex
            {
                keys = _managedKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray()
            }));
        }

        private void RememberKey(string key)
        {
            ReloadManagedKeys();
            if (!_managedKeys.Add(key)) return;
            SaveManagedKeys();
            PlayerPrefs.Save();
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
                throw;
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
                throw;
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
