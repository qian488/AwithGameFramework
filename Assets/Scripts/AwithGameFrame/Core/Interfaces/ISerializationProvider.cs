using Cysharp.Threading.Tasks;

namespace AwithGameFrame.Core.Interfaces
{
    /// <summary>
    /// 序列化操作提供者接口
    /// 为不同的序列化实现提供统一抽象
    /// </summary>
    public interface ISerializationProvider
    {
        /// <summary>
        /// 序列化对象为字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字符串</returns>
        UniTask<string> SerializeAsync<T>(T obj);

        /// <summary>
        /// 反序列化字符串为对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="json">序列化的字符串</param>
        /// <returns>反序列化后的对象</returns>
        UniTask<T> DeserializeAsync<T>(string json);

        /// <summary>
        /// 序列化对象为字节数组
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        UniTask<byte[]> SerializeToBytesAsync<T>(T obj);

        /// <summary>
        /// 反序列化字节数组为对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="bytes">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        UniTask<T> DeserializeFromBytesAsync<T>(byte[] bytes);

        /// <summary>
        /// 检查字符串是否为有效的序列化数据
        /// </summary>
        /// <param name="json">要检查的字符串</param>
        /// <returns>是否有效</returns>
        bool IsValidJson(string json);

        /// <summary>
        /// 获取序列化格式名称
        /// </summary>
        /// <returns>格式名称</returns>
        string GetFormatName();
    }
}
