using System;
using Cysharp.Threading.Tasks;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// MessagePack序列化器接口
    /// 提供高性能的二进制序列化能力
    /// </summary>
    public interface IMessagePackSerializer : ISerializer
    {
        /// <summary>
        /// 序列化对象到字节数组（MessagePack格式）
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        new UniTask<byte[]> SerializeAsync<T>(T obj);

        /// <summary>
        /// 从字节数组反序列化对象（MessagePack格式）
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="data">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        new UniTask<T> DeserializeAsync<T>(byte[] data);

        /// <summary>
        /// 序列化对象到Base64字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>Base64编码的字符串</returns>
        new UniTask<string> SerializeToStringAsync<T>(T obj);

        /// <summary>
        /// 从Base64字符串反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="data">Base64编码的字符串</param>
        /// <returns>反序列化后的对象</returns>
        new UniTask<T> DeserializeFromStringAsync<T>(string data);

        /// <summary>
        /// 验证数据是否为有效的MessagePack格式
        /// </summary>
        /// <param name="data">要验证的数据</param>
        /// <returns>是否为有效格式</returns>
        new bool IsValidData(byte[] data);

        /// <summary>
        /// 获取序列化后的大小
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节大小</returns>
        new UniTask<int> GetSerializedSizeAsync<T>(T obj);
    }

    /// <summary>
    /// MessagePack序列化配置
    /// </summary>
    [Serializable]
    public class MessagePackSerializationConfig : SerializationConfig
    {
        /// <summary>
        /// 是否启用LZ4压缩
        /// </summary>
        public bool EnableLZ4Compression = true;

        /// <summary>
        /// 是否启用字符串压缩
        /// </summary>
        public bool EnableStringCompression = true;

        /// <summary>
        /// 是否启用调试模式
        /// </summary>
        public bool EnableDebugMode = false;

        /// <summary>
        /// 最大消息大小限制（字节）
        /// </summary>
        public int MaxMessageSize = 1024 * 1024; // 1MB
    }
}
