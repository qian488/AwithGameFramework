using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 二进制文件存储提供者
    /// </summary>
    public class BinaryFileStorage : IStorageProvider
    {
        private DataPersistenceConfig _config;
        private bool _isInitialized = false;
        private string _dataPath;

        public StorageType StorageType => StorageType.BinaryFile;
        public bool IsInitialized => _isInitialized;

        public Task<DataOperationResult> InitializeAsync()
        {
            return InitializeAsync((DataPersistenceConfig)null);
        }

        public Task<DataOperationResult> InitializeAsync(object config)
        {
            try
            {
                _config = config as DataPersistenceConfig ?? new DataPersistenceConfig();
                _dataPath = Path.GetFullPath(_config.BinaryDataRootPath ?? Path.Combine(_config.DataRootPath, "BinaryData"));
                
                if (!Directory.Exists(_dataPath))
                {
                    Directory.CreateDirectory(_dataPath);
                }
                
                _isInitialized = true;
                
                FrameworkLogger.Info($"BinaryFileStorage initialized at: {_dataPath}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to initialize BinaryFileStorage: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<DataOperationResult> SaveAsync(string key, byte[] data)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                File.WriteAllBytes(filePath, data);
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
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                var json = JsonConvert.SerializeObject(data, PortableJson.CreateSettings());
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                
                if (_config.EnableEncryption)
                {
                    bytes = EncryptBytes(bytes);
                }
                
                if (_config.EnableCompression)
                {
                    bytes = CompressBytes(bytes);
                }
                
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fileStream))
                {
                    // 写入版本信息
                    writer.Write(1); // 数据版本
                    
                    // 写入数据长度
                    writer.Write(bytes.Length);
                    
                    // 写入数据
                    writer.Write(bytes);
                }
                
                FrameworkLogger.Debug($"Data saved to binary file: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to save data to binary file: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<(byte[] data, DataOperationResult result)> LoadAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Binary file not found: {key}", LogCategory.Core);
                    return Task.FromResult<(byte[], DataOperationResult)>((null, DataOperationResult.NotFound));
                }

                var data = File.ReadAllBytes(filePath);
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
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult((default(T), DataOperationResult.Failed));
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Binary file not found: {key}", LogCategory.Core);
                    return Task.FromResult((default(T), DataOperationResult.NotFound));
                }
                
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fileStream))
                {
                    // 读取版本信息
                    var version = reader.ReadInt32();
                    if (version != 1)
                        throw new InvalidDataException($"Unsupported binary file version: {version}");
                    
                    // 读取数据长度
                    var dataLength = reader.ReadInt32();
                    if (dataLength < 0 || dataLength != fileStream.Length - fileStream.Position)
                        throw new InvalidDataException("Invalid binary file content length.");
                    
                    // 读取数据
                    var bytes = reader.ReadBytes(dataLength);
                    
                    // 解压缩
                    if (_config.EnableCompression)
                    {
                        bytes = DecompressBytes(bytes);
                    }
                    
                    // 解密
                    if (_config.EnableEncryption)
                    {
                        bytes = DecryptBytes(bytes);
                    }
                    
                    var json = System.Text.Encoding.UTF8.GetString(bytes);
                    var data = JsonConvert.DeserializeObject<T>(json, PortableJson.CreateSettings());
                    
                    FrameworkLogger.Debug($"Data loaded from binary file: {key}", LogCategory.Core);
                    return Task.FromResult((data, DataOperationResult.Success));
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to load data from binary file: {ex.Message}", LogCategory.Core);
                return Task.FromResult((default(T), DataOperationResult.Failed));
            }
        }

        public Task<DataOperationResult> DeleteAsync(string key)
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var filePath = GetFilePath(key);
                
                if (!File.Exists(filePath))
                {
                    FrameworkLogger.Debug($"Binary file not found: {key}", LogCategory.Core);
                    return Task.FromResult(DataOperationResult.NotFound);
                }
                
                File.Delete(filePath);
                
                FrameworkLogger.Debug($"Binary file deleted: {key}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to delete binary file: {ex.Message}", LogCategory.Core);
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
                FrameworkLogger.Error($"Failed to check binary file existence: {ex.Message}", LogCategory.Core);
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
                FrameworkLogger.Error($"Failed to get all binary file keys: {ex.Message}", LogCategory.Core);
                return Task.FromResult(new string[0]);
            }
        }

        public Task<DataOperationResult> ClearAsync()
        {
            if (!_isInitialized)
            {
                FrameworkLogger.Warn("BinaryFileStorage not initialized", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }

            try
            {
                var files = Directory.GetFiles(_dataPath, "*" + _config.FileExtension);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                
                FrameworkLogger.Info("All binary files cleared", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Success);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to clear binary files: {ex.Message}", LogCategory.Core);
                return Task.FromResult(DataOperationResult.Failed);
            }
        }

        public Task<StorageStatistics> GetStatisticsAsync()
        {
            if (!_isInitialized)
            {
                return Task.FromResult(new StorageStatistics
                {
                    StorageType = StorageType.BinaryFile,
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
                    StorageType = StorageType.BinaryFile,
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
                FrameworkLogger.Error($"Failed to get binary file statistics: {ex.Message}", LogCategory.Core);
                return Task.FromResult(new StorageStatistics
                {
                    StorageType = StorageType.BinaryFile,
                    IsHealthy = false
                });
            }
        }

        public void Dispose()
        {
            _isInitialized = false;
            FrameworkLogger.Debug("BinaryFileStorage disposed", LogCategory.Core);
        }

        private string GetFilePath(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key == "." || key == ".." || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Storage keys must be file names without directory separators.", nameof(key));
            if (string.IsNullOrEmpty(_config.FileExtension) || _config.FileExtension.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("FileExtension must not contain a directory path.");
            return Path.Combine(_dataPath, key + _config.FileExtension);
        }

        private byte[] EncryptBytes(byte[] data)
        {
            // 简单的XOR加密实现
            if (data == null || data.Length == 0)
                return data;

            var key = _config.EncryptionKey ?? "DefaultKey";
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            var result = new byte[data.Length];
            
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
            }
            
            return result;
        }

        private byte[] DecryptBytes(byte[] encryptedData)
        {
            // 简单的XOR解密实现
            if (encryptedData == null || encryptedData.Length == 0)
                return encryptedData;

            var key = _config.EncryptionKey ?? "DefaultKey";
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            var result = new byte[encryptedData.Length];
            
            for (int i = 0; i < encryptedData.Length; i++)
            {
                result[i] = (byte)(encryptedData[i] ^ keyBytes[i % keyBytes.Length]);
            }
            
            return result;
        }

        private byte[] CompressBytes(byte[] data)
        {
            // 使用GZip压缩
            if (data == null || data.Length == 0)
                return data;

            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Compress))
                    {
                        gzipStream.Write(data, 0, data.Length);
                    }
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to compress bytes: {ex.Message}", LogCategory.Core);
                return data;
            }
        }

        private byte[] DecompressBytes(byte[] compressedData)
        {
            // 使用GZip解压缩
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;

            try
            {
                using (var memoryStream = new MemoryStream(compressedData))
                {
                    using (var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress))
                    {
                        using (var resultStream = new MemoryStream())
                        {
                            gzipStream.CopyTo(resultStream);
                            return resultStream.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to decompress bytes: {ex.Message}", LogCategory.Core);
                return compressedData;
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
