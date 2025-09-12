using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        UniTask<byte[]> SerializeAsync<T>(T obj);

        /// <summary>
        /// 反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="data">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        UniTask<T> DeserializeAsync<T>(byte[] data);

        /// <summary>
        /// 序列化对象到字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字符串</returns>
        UniTask<string> SerializeToStringAsync<T>(T obj);

        /// <summary>
        /// 从字符串反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="json">序列化的字符串</param>
        /// <returns>反序列化后的对象</returns>
        UniTask<T> DeserializeFromStringAsync<T>(string json);

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
        UniTask<int> GetSerializedSizeAsync<T>(T obj);
    }

    /// <summary>
    /// 序列化配置
    /// </summary>
    [Serializable]
    public class SerializationConfig
    {
        /// <summary>是否启用压缩</summary>
        public bool EnableCompression = false;
        /// <summary>压缩级别（1-9）</summary>
        public int CompressionLevel = 6;
        /// <summary>是否格式化输出</summary>
        public bool PrettyPrint = false;
        /// <summary>是否包含类型信息</summary>
        public bool IncludeTypeInfo = true;
        /// <summary>是否忽略空值</summary>
        public bool IgnoreNullValues = false;
        /// <summary>日期格式</summary>
        public string DateFormat = "yyyy-MM-dd HH:mm:ss";
    }

    /// <summary>
    /// 序列化器工厂
    /// 根据配置创建合适的序列化器实例
    /// </summary>
    public static class SerializerFactory
    {
        /// <summary>
        /// 创建序列化器
        /// </summary>
        /// <param name="format">序列化格式</param>
        /// <param name="config">序列化配置</param>
        /// <returns>序列化器实例</returns>
        public static ISerializer CreateSerializer(SerializationFormat format, SerializationConfig config = null)
        {
            config ??= new SerializationConfig();

            return format switch
            {
                SerializationFormat.Json => new CompressedJsonSerializer(config),
                SerializationFormat.Binary => new CompressedJsonSerializer(config),
                SerializationFormat.MessagePack => CreateMessagePackSerializer(config),
                SerializationFormat.Protobuf => CreateProtobufSerializer(config),
                _ => throw new ArgumentException($"不支持的序列化格式: {format}")
            };
        }

        /// <summary>
        /// 创建MessagePack序列化器（支持降级）
        /// </summary>
        private static ISerializer CreateMessagePackSerializer(SerializationConfig config)
        {
            if (HasMessagePackSupport())
            {
                var messagePackConfig = config as MessagePackSerializationConfig ?? new MessagePackSerializationConfig();
                return new MessagePackSerializer(messagePackConfig);
            }
            else
            {
                // 降级到压缩JSON序列化
                FrameworkLogger.Warn("MessagePack包未安装，降级到压缩JSON序列化", LogCategory.Core);
                return new CompressedJsonSerializer(config);
            }
        }

        /// <summary>
        /// 创建Protobuf序列化器（支持降级）
        /// </summary>
        private static ISerializer CreateProtobufSerializer(SerializationConfig config)
        {
            if (HasProtobufSupport())
            {
                var protobufConfig = config as ProtobufSerializationConfig ?? new ProtobufSerializationConfig();
                return new ProtobufSerializer(protobufConfig);
            }
            else
            {
                // 降级到压缩JSON序列化
                FrameworkLogger.Warn("Google.Protobuf包未安装，降级到压缩JSON序列化", LogCategory.Core);
                return new CompressedJsonSerializer(config);
            }
        }

        /// <summary>
        /// 检查是否支持MessagePack
        /// </summary>
        private static bool HasMessagePackSupport()
        {
            try
            {
                // 尝试反射查找MessagePack类型
                var assembly = System.Reflection.Assembly.GetAssembly(typeof(object));
                var messagePackType = assembly.GetType("MessagePack.MessagePackSerializer");
                return messagePackType != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查是否支持Protobuf
        /// </summary>
        private static bool HasProtobufSupport()
        {
            try
            {
                // 尝试反射查找Google.Protobuf类型
                var assembly = System.Reflection.Assembly.GetAssembly(typeof(object));
                var protobufType = assembly.GetType("Google.Protobuf.IMessage");
                return protobufType != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 创建默认序列化器（JSON格式）
        /// </summary>
        /// <param name="config">序列化配置</param>
        /// <returns>默认序列化器实例</returns>
        public static ISerializer CreateDefaultSerializer(SerializationConfig config = null)
        {
            return CreateSerializer(SerializationFormat.Json, config);
        }

        /// <summary>
        /// 检查序列化格式是否支持
        /// </summary>
        /// <param name="format">序列化格式</param>
        /// <returns>是否支持</returns>
        public static bool IsFormatSupported(SerializationFormat format)
        {
            return format switch
            {
                SerializationFormat.Json => true,
                SerializationFormat.Binary => true,
                SerializationFormat.MessagePack => HasMessagePackSupport(),
                SerializationFormat.Protobuf => HasProtobufSupport(),
                _ => false
            };
        }

        /// <summary>
        /// 获取支持的序列化格式列表
        /// </summary>
        /// <returns>支持的格式数组</returns>
        public static SerializationFormat[] GetSupportedFormats()
        {
            var formats = new List<SerializationFormat>
            {
                SerializationFormat.Json,
                SerializationFormat.Binary
            };

            if (HasMessagePackSupport())
            {
                formats.Add(SerializationFormat.MessagePack);
            }

            if (HasProtobufSupport())
            {
                formats.Add(SerializationFormat.Protobuf);
            }

            return formats.ToArray();
        }
    }
}
