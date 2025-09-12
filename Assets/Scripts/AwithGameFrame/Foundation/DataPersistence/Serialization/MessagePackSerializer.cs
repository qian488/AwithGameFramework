using System;
using Cysharp.Threading.Tasks;
using MessagePack;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// MessagePack序列化器
    /// 提供高性能的二进制序列化，比JSON快3-10倍，体积更小
    /// 支持LZ4压缩，提供最佳的性能和存储效率
    /// </summary>
    public class MessagePackSerializer : IMessagePackSerializer
    {
        private readonly MessagePackSerializerOptions _options;

        public SerializationFormat Format => SerializationFormat.MessagePack;
        public bool SupportsCompression => true;

        public MessagePackSerializer(SerializationConfig config = null)
        {
            var messagePackConfig = config as MessagePackSerializationConfig ?? new MessagePackSerializationConfig();
            
            _options = MessagePackSerializerOptions.Standard
                .WithCompression(messagePackConfig.EnableLZ4Compression ? MessagePackCompression.Lz4Block : MessagePackCompression.None);
        }

        public UniTask<byte[]> SerializeAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return UniTask.FromResult(new byte[0]);
                }

                var bytes = MessagePack.MessagePackSerializer.Serialize(obj, _options);
                FrameworkLogger.Debug($"MessagePack serialized {typeof(T).Name}, size: {bytes.Length} bytes", LogCategory.Core);
                return UniTask.FromResult(bytes);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to serialize with MessagePack: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public UniTask<T> DeserializeAsync<T>(byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    return UniTask.FromResult(default(T));
                }

                var obj = MessagePack.MessagePackSerializer.Deserialize<T>(data, _options);
                FrameworkLogger.Debug($"MessagePack deserialized {typeof(T).Name}, size: {data.Length} bytes", LogCategory.Core);
                return UniTask.FromResult(obj);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to deserialize with MessagePack: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public UniTask<string> SerializeToStringAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return UniTask.FromResult(string.Empty);
                }

                var bytes = MessagePack.MessagePackSerializer.Serialize(obj, _options);
                var base64 = Convert.ToBase64String(bytes);
                FrameworkLogger.Debug($"MessagePack serialized to string: {typeof(T).Name}, size: {base64.Length} chars", LogCategory.Core);
                return UniTask.FromResult(base64);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to serialize to string with MessagePack: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public UniTask<T> DeserializeFromStringAsync<T>(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    return UniTask.FromResult(default(T));
                }

                var bytes = Convert.FromBase64String(data);
                var obj = MessagePack.MessagePackSerializer.Deserialize<T>(bytes, _options);
                FrameworkLogger.Debug($"MessagePack deserialized from string: {typeof(T).Name}, size: {data.Length} chars", LogCategory.Core);
                return UniTask.FromResult(obj);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to deserialize from string with MessagePack: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public bool IsValidData(byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                    return false;

                // 尝试解析MessagePack数据
                MessagePack.MessagePackSerializer.Deserialize<object>(data, _options);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public UniTask<int> GetSerializedSizeAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return UniTask.FromResult(0);
                }

                var bytes = MessagePack.MessagePackSerializer.Serialize(obj, _options);
                return UniTask.FromResult(bytes.Length);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to get serialized size with MessagePack: {ex.Message}", LogCategory.Core);
                return UniTask.FromResult(0);
            }
        }
    }
}
