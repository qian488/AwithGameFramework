using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 序列化器接口
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 序列化格式
        /// </summary>
        SerializationFormat Format { get; }

        /// <summary>
        /// 是否支持压缩
        /// </summary>
        bool SupportsCompression { get; }

        /// <summary>
        /// 序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        Task<byte[]> SerializeAsync<T>(T obj);

        /// <summary>
        /// 反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="data">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        Task<T> DeserializeAsync<T>(byte[] data);

        /// <summary>
        /// 序列化对象到字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字符串</returns>
        Task<string> SerializeToStringAsync<T>(T obj);

        /// <summary>
        /// 从字符串反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="json">序列化的字符串</param>
        /// <returns>反序列化后的对象</returns>
        Task<T> DeserializeFromStringAsync<T>(string json);

        /// <summary>
        /// 检查数据是否有效
        /// </summary>
        /// <param name="data">要检查的数据</param>
        /// <returns>是否有效</returns>
        bool IsValidData(byte[] data);

        /// <summary>
        /// 获取序列化后的数据大小
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的大小</returns>
        Task<int> GetSerializedSizeAsync<T>(T obj);
    }

    /// <summary>
    /// 序列化配置
    /// </summary>
    [Serializable]
    public class SerializationConfig
    {
        /// <summary>是否启用压缩</summary>
        public bool EnableCompression = false;
        /// <summary>内置 GZip 仅接受 6，使用运行时的 Optimal 策略。</summary>
        public int CompressionLevel = 6;
        /// <summary>是否格式化输出</summary>
        public bool PrettyPrint = false;
        /// <summary>内置 JSON 编码器不支持 CLR 类型信息；true 会抛出 NotSupportedException。</summary>
        public bool IncludeTypeInfo = false;
        /// <summary>是否忽略空值</summary>
        public bool IgnoreNullValues = false;
        /// <summary>日期格式；null 使用保留精度和时区的默认 ISO 8601 格式。</summary>
        public string DateFormat;

        internal SerializationConfig CreateSnapshot(bool supportsCompression)
        {
            if (IncludeTypeInfo)
                throw new NotSupportedException("Built-in JSON codecs do not persist or resolve CLR type metadata.");
            if (EnableCompression && !supportsCompression)
                throw new NotSupportedException("This JSON payload does not support compression; use the binary codec or the file provider's EnableCompression option.");
            if (EnableCompression && CompressionLevel != 6)
                throw new NotSupportedException("Built-in GZip accepts CompressionLevel=6 (runtime Optimal) only.");
            return (SerializationConfig)MemberwiseClone();
        }
    }

    /// <summary>
    /// 序列化器工厂
    /// 根据配置创建合适的序列化器实例
    /// </summary>
    public static class SerializerFactory
    {
        private static readonly Dictionary<SerializationFormat, Func<SerializationConfig, ISerializer>> Factories = new Dictionary<SerializationFormat, Func<SerializationConfig, ISerializer>>();
        private static readonly object Sync = new object();

        /// <summary>Register an optional codec. The codec must actually implement the requested wire format.</summary>
        public static void RegisterSerializer(SerializationFormat format, Func<SerializationConfig, ISerializer> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (Sync) Factories[format] = factory;
        }

        public static ISerializer CreateSerializer(SerializationFormat format, SerializationConfig config = null)
        {
            Func<SerializationConfig, ISerializer> factory;
            lock (Sync) Factories.TryGetValue(format, out factory);
            if (factory != null)
            {
                var serializer = factory(config ?? new SerializationConfig());
                if (serializer == null || serializer.Format != format)
                    throw new InvalidOperationException($"The registered serializer does not implement {format}.");
                return serializer;
            }
            switch (format)
            {
                case SerializationFormat.Json: return new JsonSerializer(config);
                case SerializationFormat.Binary: return new CompressedJsonSerializer(config);
                default: throw new NotSupportedException($"Register a serializer implementation for {format} before using it.");
            }
        }

        public static ISerializer CreateDefaultSerializer(SerializationConfig config = null)
        {
            return CreateSerializer(SerializationFormat.Json, config);
        }

        public static bool IsFormatSupported(SerializationFormat format)
        {
            lock (Sync) return format == SerializationFormat.Json || format == SerializationFormat.Binary || Factories.ContainsKey(format);
        }

        public static SerializationFormat[] GetSupportedFormats()
        {
            var formats = new List<SerializationFormat>();
            foreach (SerializationFormat format in Enum.GetValues(typeof(SerializationFormat)))
                if (IsFormatSupported(format)) formats.Add(format);
            return formats.ToArray();
        }
    }
}
