using System;
using System.Collections.Generic;

namespace AwithGameFrame.Foundation.DataPersistence
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
        
        /// <summary>旧安全策略选项；内置文件存储要求 None。</summary>
        public SecurityLevel DefaultSecurityLevel = SecurityLevel.None;
        
        /// <summary>旧备份策略选项；内置文件存储要求 None。</summary>
        public BackupStrategy DefaultBackupStrategy = BackupStrategy.None;
        
        /// <summary>数据根目录</summary>
        public string DataRootPath = "Data";

        /// <summary>二进制目录；默认使用 DataRootPath/BinaryData，宿主可以指定已有目录。</summary>
        public string BinaryDataRootPath;
        
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
        
        /// <summary>旧 XOR 混淆密钥，不提供密码学保密性。</summary>
        public string EncryptionKey = "DefaultEncryptionKey123456789012345678901234567890";
        
        /// <summary>JSON 选项；文件压缩使用外层 EnableCompression，禁止内层再次请求压缩。</summary>
        public SerializationConfig SerializationConfig = new SerializationConfig();
        /// <summary>首选序列化格式</summary>
        public SerializationFormat PreferredSerializationFormat = SerializationFormat.Json;

        #endregion

        #region 功能开关

        /// <summary>保留旧选项；内置文件存储未实现，启用时拒绝初始化。</summary>
        public bool EnableAutoBackup = false;
        
        /// <summary>内置文件存储始终验证键与格式，要求为 true。</summary>
        public bool EnableDataValidation = true;
        
        /// <summary>保留旧选项；内置文件存储未实现，启用时拒绝初始化。</summary>
        public bool EnablePerformanceMonitoring = false;
        
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
        
        /// <summary>兼容级别；内置 GZip 仅接受 6，使用运行时的 Optimal 策略。</summary>
        public int CompressionLevel = 6;

        #endregion

        #region 加密配置

        /// <summary>内置文件存储仅支持旧 XOR 混淆；启用 AES256 会明确拒绝。</summary>
        public EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.XOR;
        
        /// <summary>XOR 不使用密码学密钥位数；启用 XOR 时要求为 0。</summary>
        public int KeyLength = 0;
        
        /// <summary>内置文件存储不提供密钥派生；启用 XOR 时要求为 false。</summary>
        public bool EnableKeyDerivation = false;
        
        /// <summary>密钥派生迭代次数</summary>
        public int KeyDerivationIterations = 10000;
        
        /// <summary>保留旧选项；文件存储未实现可配置完整性策略，启用时拒绝初始化。</summary>
        public bool EnableIntegrityCheck = false;

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
                    config.DefaultStorageType = StorageType.JsonFile;
                    config.EnableEncryption = false;
                    config.EnableCompression = false;
                    config.EnableDataValidation = true;
                    break;
                    
                case DataPersistencePreset.Debug:
                    config.DefaultStorageType = StorageType.JsonFile;
                    config.EnableEncryption = true;
                    config.EnableCompression = false;
                    config.EnableDataValidation = true;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.XOR;
                    break;
                    
                case DataPersistencePreset.Release:
                    config.DefaultStorageType = StorageType.JsonFile;
                    config.EnableEncryption = false;
                    config.EnableCompression = true;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = false;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.None;
                    config.CompressionAlgorithm = CompressionAlgorithm.GZip;
                    break;
                    
                case DataPersistencePreset.Production:
                    config.DefaultStorageType = StorageType.JsonFile;
                    config.EnableEncryption = false;
                    config.EnableCompression = true;
                    config.EnableDataValidation = true;
                    config.EnablePerformanceMonitoring = false;
                    config.EncryptionAlgorithm = EncryptionAlgorithm.None;
                    config.CompressionAlgorithm = CompressionAlgorithm.GZip;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
            
            return config;
        }

        #endregion

        // Session routing and framework-owned nested options do not retain caller-owned containers.
        // Custom provider options are opaque; their provider owns capturing any mutable contents.
        internal DataPersistenceConfig CreateSnapshot()
        {
            var snapshot = (DataPersistenceConfig)MemberwiseClone();
            snapshot.SerializationConfig = (SerializationConfig ?? new SerializationConfig()).Copy();
            snapshot.StorageConfigs = new Dictionary<StorageType, object>();
            if (StorageConfigs != null)
                foreach (var pair in StorageConfigs)
                    snapshot.StorageConfigs.Add(pair.Key, pair.Value is StorageConfig storage
                        ? storage.CreateSnapshot()
                        : pair.Value is SerializationConfig serialization ? serialization.Copy() : pair.Value);
            return snapshot;
        }

        // Capture validated options once. Later edits to a caller's mutable config must not
        // bypass algorithm validation or change how an already opened file is decoded.
        internal DataPersistenceConfig CreateFileStorageSnapshot()
        {
            if (EnableEncryption)
            {
                if (EncryptionAlgorithm != EncryptionAlgorithm.XOR)
                    throw new NotSupportedException("Built-in file storage supports legacy XOR obfuscation only; AES256 is not implemented.");
                if (EnableKeyDerivation || KeyLength != 0)
                    throw new NotSupportedException("Legacy XOR does not implement key derivation or cryptographic key lengths.");
                if (string.IsNullOrEmpty(EncryptionKey))
                    throw new ArgumentException("An explicit nonempty XOR key is required.", nameof(EncryptionKey));
            }
            if (EnableCompression)
            {
                if (CompressionAlgorithm != CompressionAlgorithm.GZip)
                    throw new NotSupportedException("Built-in file storage supports GZip compression only.");
                if (CompressionLevel != 6)
                    throw new NotSupportedException("Built-in GZip accepts CompressionLevel=6 (runtime Optimal) only.");
            }
            if (EnableAutoBackup || DefaultBackupStrategy != BackupStrategy.None)
                throw new NotSupportedException("Built-in file storage does not implement automatic backups; use a versioned save service or a custom provider.");
            if (EnableIntegrityCheck || DefaultSecurityLevel != SecurityLevel.None)
                throw new NotSupportedException("Built-in file storage does not implement authenticated encryption or a configurable integrity policy.");
            if (CloudStorageType != CloudStorageType.None)
                throw new NotSupportedException("Built-in file storage does not implement cloud synchronization.");
            if (EnablePerformanceMonitoring)
                throw new NotSupportedException("Built-in file storage does not implement performance monitoring.");
            if (!EnableDataValidation)
                throw new NotSupportedException("Built-in file storage always validates keys and file formats.");
            if (DefaultSerializationFormat != SerializationFormat.Json || PreferredSerializationFormat != SerializationFormat.Json)
                throw new NotSupportedException("Built-in typed file storage persists JSON payloads; use an external serializer with the raw byte API for other formats.");

            var snapshot = CreateSnapshot();
            snapshot.SerializationConfig = (SerializationConfig ?? new SerializationConfig()).CreateSnapshot(false);
            snapshot.SerializationConfig.PrettyPrint = PrettyPrint;
            return snapshot;
        }
    }

}
