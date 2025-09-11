using System;
using System.Collections.Generic;
using AwithGameFrame.DataPersistence.Serialization;

namespace AwithGameFrame.DataPersistence
{
    /// <summary>
    /// 数据持久化统一配置管理
    /// 集中管理所有数据持久化相关的配置
    /// </summary>
    [Serializable]
    public class DataPersistenceConfig
    {
        #region 基础配置

        /// <summary>默认存储类型</summary>
        public StorageType DefaultStorageType = StorageType.JsonFile;
        
        /// <summary>默认序列化格式</summary>
        public SerializationFormat DefaultSerializationFormat = SerializationFormat.Json;
        
        /// <summary>默认安全级别</summary>
        public SecurityLevel DefaultSecurityLevel = SecurityLevel.Basic;
        
        /// <summary>默认备份策略</summary>
        public BackupStrategy DefaultBackupStrategy = BackupStrategy.Local;
        
        /// <summary>数据根目录</summary>
        public string DataRootPath = "Data";
        
        /// <summary>备份目录</summary>
        public string BackupPath = "Backup";
        
        /// <summary>临时目录</summary>
        public string TempPath = "Temp";
        
        /// <summary>数据库名称</summary>
        public string DatabaseName = "GameData.db";
        
        /// <summary>文件扩展名</summary>
        public string FileExtension = ".json";
        
        /// <summary>键前缀</summary>
        public string KeyPrefix = "";
        
        /// <summary>加密密钥</summary>
        public string EncryptionKey = "DefaultEncryptionKey123456789012345678901234567890";
        
        /// <summary>序列化配置</summary>
        public SerializationConfig SerializationConfig = new SerializationConfig();
        /// <summary>首选序列化格式</summary>
        public SerializationFormat PreferredSerializationFormat = SerializationFormat.Json;

        #endregion

        #region 功能开关

        /// <summary>是否启用自动备份</summary>
        public bool EnableAutoBackup = true;
        
        /// <summary>是否启用数据验证</summary>
        public bool EnableDataValidation = true;
        
        /// <summary>是否启用性能监控</summary>
        public bool EnablePerformanceMonitoring = true;
        
        /// <summary>是否启用压缩</summary>
        public bool EnableCompression = false;
        
        /// <summary>JSON格式化输出</summary>
        public bool PrettyPrint = true;
        
        /// <summary>是否启用加密</summary>
        public bool EnableEncryption = false;

        #endregion

        #region 备份配置

        /// <summary>备份间隔（分钟）</summary>
        public int BackupIntervalMinutes = 30;
        
        /// <summary>最大备份数量</summary>
        public int MaxBackupCount = 10;

        #endregion

        #region 压缩配置

        /// <summary>压缩算法</summary>
        public CompressionAlgorithm CompressionAlgorithm = CompressionAlgorithm.GZip;
        
        /// <summary>压缩级别（1-9）</summary>
        public int CompressionLevel = 6;

        #endregion

        #region 加密配置

        /// <summary>加密算法</summary>
        public EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.AES256;
        
        /// <summary>密钥长度</summary>
        public int KeyLength = 256;
        
        /// <summary>是否启用密钥派生</summary>
        public bool EnableKeyDerivation = true;
        
        /// <summary>密钥派生迭代次数</summary>
        public int KeyDerivationIterations = 10000;
        
        /// <summary>是否启用完整性检查</summary>
        public bool EnableIntegrityCheck = true;

        #endregion

        #region 云存储配置

        /// <summary>云存储类型</summary>
        public CloudStorageType CloudStorageType = CloudStorageType.None;
        
        /// <summary>云存储端点</summary>
        public string CloudEndpoint = "";
        
        /// <summary>云存储访问密钥</summary>
        public string CloudAccessKey = "";
        
        /// <summary>云存储密钥</summary>
        public string CloudSecretKey = "";
        
        /// <summary>云存储桶名称</summary>
        public string CloudBucketName = "";

        #endregion

        #region 存储提供者配置

        /// <summary>存储提供者配置</summary>
        public Dictionary<StorageType, object> StorageConfigs = new Dictionary<StorageType, object>();

        #endregion

        #region 预设配置

        /// <summary>
        /// 获取预设配置
        /// </summary>
        /// <param name="preset">预设类型</param>
        /// <returns>配置对象</returns>
        public static DataPersistenceConfig GetPreset(DataPersistencePreset preset)
        {
            var config = new DataPersistenceConfig();
            
            switch (preset)
            {
                case DataPersistencePreset.Development:
                    config.DefaultStorageType = StorageType.PlayerPrefs;
                    config.EnableEncryption = false;
                    config.EnableCompression = false;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = true;
                    break;
                    
                case DataPersistencePreset.Debug:
                    config.DefaultStorageType = StorageType.JsonFile;
                    config.EnableEncryption = true;
                    config.EnableCompression = false;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = true;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.XOR;
                    break;
                    
                case DataPersistencePreset.Release:
                    config.DefaultStorageType = StorageType.PlayerPrefs;
                    config.EnableEncryption = true;
                    config.EnableCompression = true;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = false;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.AES256;
                    config.CompressionAlgorithm = CompressionAlgorithm.GZip;
                    break;
                    
                case DataPersistencePreset.Production:
                    config.DefaultStorageType = StorageType.Database;
                    config.EnableEncryption = true;
                    config.EnableCompression = true;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = false;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.AES256;
                    config.CompressionAlgorithm = CompressionAlgorithm.LZ4;
                    config.EnableKeyDerivation = true;
                    config.KeyDerivationIterations = 100000;
                    break;
            }
            
            return config;
        }

        #endregion
    }

}