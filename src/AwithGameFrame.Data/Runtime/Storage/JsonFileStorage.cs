using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

        public Task<DataOperationResult> InitializeAsync()
        {
            return InitializeAsync((DataPersistenceConfig)null);
        }

        public Task<DataOperationResult> InitializeAsync(object config)
        {
            _isInitialized = false;
            try
            {
                if (config != null && !(config is DataPersistenceConfig)) throw new ArgumentException("Expected DataPersistenceConfig.", nameof(config));
                _config = (config as DataPersistenceConfig ?? new DataPersistenceConfig()).CreateFileStorageSnapshot();
                _dataPath = Path.GetFullPath(_config.DataRootPath);
                
                if (!Directory.Exists(_dataPath))
                {
                    Directory.CreateDirectory(_dataPath);
                }
                
                _isInitialized = true;
                
                FrameworkLogger.Info($"JsonFileStorage initialized at: {_dataPath}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (NotSupportedException ex)
            {
                FrameworkLogger.Error($"Unsupported JsonFileStorage configuration: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.NotImplemented);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to initialize JsonFileStorage: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                FileStorageIO.WriteAllBytes(filePath, data);
                FrameworkLogger.Debug($"Saved binary data to: {filePath}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save binary data: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> SaveAsync<T>(string key, T data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                var json = JsonConvert.SerializeObject(data, PortableJson.CreateSettings(_config.SerializationConfig));
                
                if (_config.EnableEncryption)
                {
                    json = EncryptString(json);
                }
                
                if (_config.EnableCompression)
                {
                    var compressed = CompressString(json);
                    FileStorageIO.WriteAllBytes(filePath, compressed);
                }
                else
                {
                    FileStorageIO.WriteAllText(filePath, json);
                }
                
                FrameworkLogger.Debug($"Data saved to JSON file: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save data to JSON file: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Warn($"File not found: {filePath}", LogCategory.Core);
                    return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotFound));
                }

                var data = FileStorageIO.ReadAllBytes(filePath);
                FrameworkLogger.Debug($"Loaded binary data from: {filePath}", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load binary data: {ex.Message}", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }
        }

        public Task<(T data, DataOperationResult result)> LoadAsync<T>(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult<(T, DataOperationResult)>((default(T), DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Data file not found: {key}", LogCategory.Core);
                    return Task.FromResult<(T, DataOperationResult)>((default(T), DataOperationResult.NotFound));
                }
                
                string json;
                
                if (_config.EnableCompression)
                {
                    var compressed = FileStorageIO.ReadAllBytes(filePath);
                    json = DecompressString(compressed);
                }
                else
                {
                    json = FileStorageIO.ReadAllText(filePath);
                }
                
                if (_config.EnableEncryption)
                {
                    json = DecryptString(json);
                }
                
                var data = FileStorageIO.DeserializeJson<T>(json, _config.SerializationConfig);
                
                FrameworkLogger.Debug($"Data loaded from JSON file: {key}", LogCategory.Core);
                return Task.FromResult((data, DataOperationResult.Success));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load data from JSON file: {ex.Message}", LogCategory.Core);
                return Task.FromResult((default(T), FileStorageIO.IsCorruptData(ex) ? DataOperationResult.Corrupted : DataOperationResult.Failed));
            }
        }

        public Task<DataOperationResult> DeleteAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Data file not found: {key}", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotFound);
                }
                
                File.Delete(filePath);
                
                FrameworkLogger.Debug($"Data file deleted: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to delete data file: {ex.Message}", LogCategory.Core);
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
                var filePath = GetFilePath(key);
                return Task.FromResult(File.Exists(filePath));
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to check file existence: {ex.Message}", LogCategory.Core);
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
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                return Task.FromResult(files.Select(Path.GetFileNameWithoutExtension).ToArray());
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get all keys: {ex.Message}", LogCategory.Core);
                return Task.FromResult(new string[0]);
            }
        }

        public Task<DataOperationResult> ClearAsync()
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("JsonFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                
                FrameworkLogger.Info("All JSON files cleared", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to clear JSON files: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<StorageStatistics> GetStatisticsAsync()
        {
            if (!_isInitialized)
            {
                return Task.FromResult(new StorageStatistics
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
                
                return Task.FromResult(new StorageStatistics
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
                return Task.FromResult(new StorageStatistics
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
            if (string.IsNullOrWhiteSpace(key) || key == "." || key == ".." || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Storage keys must be file names without directory separators.", nameof(key));
            if (string.IsNullOrEmpty(_config.FileExtension) || _config.FileExtension.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("FileExtension must not contain a directory path.");
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

            var bytes = Convert.FromBase64String(encryptedText);
            var encrypted = FileStorageIO.DecodeJson(bytes);
            var key = _config.EncryptionKey ?? "DefaultKey";
            var result = new char[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
            {
                result[i] = (char)(encrypted[i] ^ key[i % key.Length]);
            }
            return new string(result);
        }

        private byte[] CompressString(string text)
        {
            return FileStorageIO.Compress(System.Text.Encoding.UTF8.GetBytes(text));
        }

        private string DecompressString(byte[] compressed)
        {
            return FileStorageIO.DecodeJson(FileStorageIO.Decompress(compressed));
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
