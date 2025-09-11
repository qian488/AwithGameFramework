using System;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据存储类型
    /// </summary>
    public enum StorageType
    {
        /// <summary>简单配置存储</summary>
        PlayerPrefs,
        /// <summary>JSON文件存储</summary>
        JsonFile,
        /// <summary>二进制文件存储</summary>
        BinaryFile,
        /// <summary>数据库存储（支持多种数据库）</summary>
        Database,
        /// <summary>云端存储</summary>
        Cloud
    }

    /// <summary>
    /// 序列化格式
    /// </summary>
    public enum SerializationFormat
    {
        /// <summary>JSON格式</summary>
        Json,
        /// <summary>MessagePack格式</summary>
        MessagePack,
        /// <summary>Unity内置序列化</summary>
        Unity,
        /// <summary>二进制格式</summary>
        Binary,
        /// <summary>Protobuf格式</summary>
        Protobuf
    }

    /// <summary>
    /// 数据安全级别
    /// </summary>
    public enum SecurityLevel
    {
        /// <summary>无加密</summary>
        None,
        /// <summary>基础加密</summary>
        Basic,
        /// <summary>高级加密</summary>
        Advanced,
        /// <summary>军用级加密</summary>
        Military
    }

    /// <summary>
    /// 数据备份策略
    /// </summary>
    public enum BackupStrategy
    {
        /// <summary>无备份</summary>
        None,
        /// <summary>本地备份</summary>
        Local,
        /// <summary>云端备份</summary>
        Cloud,
        /// <summary>混合备份</summary>
        Hybrid
    }

    /// <summary>
    /// 数据操作结果
    /// </summary>
    public enum DataOperationResult
    {
        /// <summary>成功</summary>
        Success,
        /// <summary>失败</summary>
        Failed,
        /// <summary>数据不存在</summary>
        NotFound,
        /// <summary>权限不足</summary>
        Unauthorized,
        /// <summary>数据损坏</summary>
        Corrupted,
        /// <summary>网络错误</summary>
        NetworkError,
        /// <summary>存储空间不足</summary>
        InsufficientSpace,
        /// <summary>其他错误</summary>
        OtherError,
        /// <summary>无效数据</summary>
        InvalidData,
        /// <summary>加密错误</summary>
        EncryptionError,
        /// <summary>压缩错误</summary>
        CompressionError,
        /// <summary>部分成功（用于批量操作）</summary>
        PartialSuccess,
        /// <summary>未实现</summary>
        NotImplemented,
        /// <summary>未初始化</summary>
        NotInitialized,
        /// <summary>不支持的存储类型</summary>
        UnsupportedStorageType
    }

    /// <summary>
    /// 压缩算法枚举
    /// </summary>
    public enum CompressionAlgorithm
    {
        /// <summary>无压缩</summary>
        None,
        /// <summary>GZip压缩</summary>
        GZip,
        /// <summary>Deflate压缩</summary>
        Deflate,
        /// <summary>LZ4压缩</summary>
        LZ4
    }

    /// <summary>
    /// 数据版本管理
    /// </summary>
    public enum DataVersion
    {
        /// <summary>版本1.0</summary>
        V1_0 = 100,
        /// <summary>版本1.1</summary>
        V1_1 = 110,
        /// <summary>版本1.2</summary>
        V1_2 = 120,
        /// <summary>当前版本</summary>
        Current = V1_2
    }

    /// <summary>
    /// 数据持久化预设
    /// </summary>
    public enum DataPersistencePreset
    {
        /// <summary>开发环境</summary>
        Development,
        
        /// <summary>调试环境</summary>
        Debug,
        
        /// <summary>发布环境</summary>
        Release,
        
        /// <summary>生产环境</summary>
        Production
    }

    /// <summary>
    /// 加密算法
    /// </summary>
    public enum EncryptionAlgorithm
    {
        /// <summary>无加密</summary>
        None,
        
        /// <summary>XOR加密</summary>
        XOR,
        
        /// <summary>AES-256加密</summary>
        AES256
    }

    /// <summary>
    /// 云存储类型
    /// </summary>
    public enum CloudStorageType
    {
        /// <summary>无云存储</summary>
        None,
        
        /// <summary>自建服务器</summary>
        Custom,
        
        /// <summary>Amazon S3</summary>
        AmazonS3,
        
        /// <summary>阿里云OSS</summary>
        AliyunOSS,
        
        /// <summary>腾讯云COS</summary>
        TencentCOS
    }
}
