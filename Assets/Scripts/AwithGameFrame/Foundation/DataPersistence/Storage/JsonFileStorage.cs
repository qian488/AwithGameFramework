using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// JSON文件存储提供者
    /// </summary>
    public class JsonFileStorage : IStorageProvider
    {
        private DataPersistenceConfig _config;
        private bool _isInitialized = false;
        private string _dataPath;

        public StorageType StorageType => StorageType.JsonFile;
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
                _dataPath = Path.Combine(Application.persistentDataPath, "Data");
                
                if (!Directory.Exists(_dataPath))
                {
                    Directory.CreateDirectory(_dataPath);
                }
                
                _isInitialized = true;
                
                FrameworkLogger.Info($"JsonFileStorage initialized at: {_dataPath}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to initialize JsonFileStorage: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                File.WriteAllBytes(filePath, data);
                FrameworkLogger.Debug($"Saved binary data to: {filePath}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save binary data: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<DataOperationResult> SaveAsync<T>(string key, T data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                var json = JsonUtility.ToJson(data, _config.PrettyPrint);
                
                if (_config.EnableEncryption)
                {
                    json = EncryptString(json);
                }
                
                if (_config.EnableCompression)
                {
                    var compressed = CompressString(json);
                    File.WriteAllBytes(filePath, compressed);
                }
                else
                {
                    File.WriteAllText(filePath, json);
                }
                
                FrameworkLogger.Debug($"Data saved to JSON file: {key}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save data to JSON file: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Warn($"File not found: {filePath}", LogCategory.Core);
                    return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotFound));
                }

                var data = File.ReadAllBytes(filePath);
                FrameworkLogger.Debug($"Loaded binary data from: {filePath}", LogCategory.Core);
                return UniTask.FromResult<(byte[], DataOperationResult)>((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load binary data: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }
        }

        public UniTask<(T data, DataOperationResult result)> LoadAsync<T>(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult<(T, DataOperationResult)>((default(T), DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Data file not found: {key}", LogCategory.Core);
                    return UniTask.FromResult<(T, DataOperationResult)>((default(T), DataOperationResult.NotFound));
                }
                
                string json;
                
                if (_config.EnableCompression)
                {
                    var compressed = File.ReadAllBytes(filePath);
                    json = DecompressString(compressed);
                }
                else
                {
                    json = File.ReadAllText(filePath);
                }
                
                if (_config.EnableEncryption)
                {
                    json = DecryptString(json);
                }
                
                var data = JsonUtility.FromJson<T>(json);
                
                FrameworkLogger.Debug($"Data loaded from JSON file: {key}", LogCategory.Core);
                return UniTask.FromResult((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load data from JSON file: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public UniTask<DataOperationResult> DeleteAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Data file not found: {key}", LogCategory.Core);
                    return UniTask.FromResult(DataOperationResult.NotFound);
                }
                
                File.Delete(filePath);
                
                FrameworkLogger.Debug($"Data file deleted: {key}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to delete data file: {ex.Message}", LogCategory.Core);
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
                var filePath = GetFilePath(key);
                return UniTask.FromResult(File.Exists(filePath));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to check file existence: {ex.Message}", LogCategory.Core);
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
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                return UniTask.FromResult(files.Select(Path.GetFileNameWithoutExtension).ToArray());
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get all keys: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(new string[0]);
            }
        }

        public UniTask<DataOperationResult> ClearAsync()
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                
                FrameworkLogger.Info("All JSON files cleared", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to clear JSON files: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(DataOperationResult.Failed);
            }
        }

        public UniTask<StorageStatistics> GetStatisticsAsync()
        {
            if (!_isInitialized)
            {
                return UniTask.FromResult(new StorageStatistics
                {
                    StorageType = StorageType.JsonFile,
                    IsHealthy = false
                });
            }

            try
            {
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                var totalSize = files.Sum(f => new FileInfo(f).Length);
                var lastModified = files.Length > 0 ? files.Max(f => File.GetLastWriteTime(f)) : DateTime.MinValue;
                
                return UniTask.FromResult(new StorageStatistics
                {
                    StorageType = StorageType.JsonFile,
                    ItemCount = files.Length,
                    TotalSize = totalSize,
                    AvailableSpace = GetAvailableSpace(),
                    LastAccessTime = DateTime.Now,
                    LastModifiedTime = lastModified,
                    IsHealthy = true
                });
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get statistics: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(new StorageStatistics
                {
                    StorageType = StorageType.JsonFile,
                    IsHealthy = false
                });
            }
        }

        public void Dispose()
        {
            _isInitialized = false;
            FrameworkLogger.Debug("JsonFileStorage disposed", LogCategory.Core);
        }

        private string GetFilePath(string key)
        {
            return Path.Combine(_dataPath, key + _config.FileExtension);
        }

        private string EncryptString(string plainText)
        {
            // 简单的XOR加密实现
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            var key = _config.EncryptionKey ?? "DefaultKey";
            var result = new char[plainText.Length];
            
            for (int i = 0; i < plainText.Length; i++)
            {
                result[i] = (char)(plainText[i] ^ key[i % key.Length]);
            }
            
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string(result)));
        }

        private string DecryptString(string encryptedText)
        {
            // 简单的XOR解密实现
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                var bytes = Convert.FromBase64String(encryptedText);
                var encrypted = System.Text.Encoding.UTF8.GetString(bytes);
                var key = _config.EncryptionKey ?? "DefaultKey";
                var result = new char[encrypted.Length];
                
                for (int i = 0; i < encrypted.Length; i++)
                {
                    result[i] = (char)(encrypted[i] ^ key[i % key.Length]);
                }
                
                return new string(result);
            }
            catch
            {
                return encryptedText; // 如果解密失败，返回原文本
            }
        }

        private byte[] CompressString(string text)
        {
            // 使用GZip压缩
            if (string.IsNullOrEmpty(text))
                return new byte[0];

            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Compress))
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                        gzipStream.Write(bytes, 0, bytes.Length);
                    }
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to compress string: {ex.Message}", LogCategory.Core);
                return System.Text.Encoding.UTF8.GetBytes(text);
            }
        }

        private string DecompressString(byte[] compressed)
        {
            // 使用GZip解压缩
            if (compressed == null || compressed.Length == 0)
                return string.Empty;

            try
            {
                using (var memoryStream = new MemoryStream(compressed))
                {
                    using (var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress))
                    {
                        using (var reader = new StreamReader(gzipStream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to decompress string: {ex.Message}", LogCategory.Core);
                return System.Text.Encoding.UTF8.GetString(compressed);
            }
        }

        private long GetAvailableSpace()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(_dataPath));
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return long.MaxValue; // 如果无法获取，返回最大值
            }
        }
    }
}