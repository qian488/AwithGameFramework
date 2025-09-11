using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Logging;
using AwithGameFrame.DataPersistence.Serialization;

namespace AwithGameFrame.DataPersistence.Serialization
{
    /// <summary>
    /// Protobuf序列化器实现
    /// 提供高性能的二进制序列化能力
    /// 注意：需要安装Google.Protobuf包才能使用
    /// </summary>
    public class ProtobufSerializer : IProtobufSerializer
    {
        private readonly ProtobufSerializationConfig _config;

        public SerializationFormat Format => SerializationFormat.Protobuf;
        public bool SupportsCompression => true;

        public ProtobufSerializer(ProtobufSerializationConfig config = null)
        {
            _config = config ?? new ProtobufSerializationConfig();
        }

        // 显式实现ISerializer接口（无约束）
        UniTask<byte[]> ISerializer.SerializeAsync<T>(T obj)
        {
            return SerializeAsyncInternal(obj);
        }

        // 实现IProtobufSerializer接口（有约束）
        public UniTask<byte[]> SerializeAsync<T>(T obj) where T : class
        {
            return SerializeAsyncInternal(obj);
        }

        private UniTask<byte[]> SerializeAsyncInternal<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return UniTask.FromResult(new byte[0]);
                }

                // 检查是否安装了Google.Protobuf
                if (!HasProtobufSupport())
                {
                    throw new InvalidOperationException("Protobuf支持未安装，请安装Google.Protobuf包");
                }

                // 检查类型约束
                if (obj != null && !typeof(T).IsClass)
                {
                    throw new InvalidOperationException($"Protobuf序列化要求类型必须是引用类型，但 {typeof(T).Name} 是值类型");
                }

                // 使用反射调用Google.Protobuf的序列化方法
                var bytes = SerializeWithProtobufInternal(obj);
                
                // 如果启用压缩，进行LZ4压缩
                if (_config.EnableCompression)
                {
                    bytes = CompressBytes(bytes);
                }

                // 添加版本头和压缩标志
                var result = CreateProtobufFormat(bytes, _config.EnableCompression);
                
                FrameworkLogger.Debug($"Protobuf序列化完成，大小: {result.Length} bytes", LogCategory.Core);
                return UniTask.FromResult(result);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Protobuf序列化失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        // 显式实现ISerializer接口（无约束）
        UniTask<T> ISerializer.DeserializeAsync<T>(byte[] data)
        {
            return DeserializeAsyncInternal<T>(data);
        }

        // 实现IProtobufSerializer接口（有约束）
        public UniTask<T> DeserializeAsync<T>(byte[] data) where T : class
        {
            return DeserializeAsyncInternal<T>(data);
        }

        private UniTask<T> DeserializeAsyncInternal<T>(byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    return UniTask.FromResult(default(T));
                }

                // 检查是否安装了Google.Protobuf
                if (!HasProtobufSupport())
                {
                    throw new InvalidOperationException("Protobuf支持未安装，请安装Google.Protobuf包");
                }

                // 解析Protobuf格式
                var (content, isCompressed) = ParseProtobufFormat(data);
                
                // 如果启用压缩，进行LZ4解压缩
                if (isCompressed)
                {
                    content = DecompressBytes(content);
                }

                // 使用Google.Protobuf反序列化
                var obj = DeserializeWithProtobufInternal<T>(content);
                
                FrameworkLogger.Debug($"Protobuf反序列化完成", LogCategory.Core);
                return UniTask.FromResult(obj);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Protobuf反序列化失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        // 显式实现ISerializer接口（无约束）
        UniTask<string> ISerializer.SerializeToStringAsync<T>(T obj)
        {
            return SerializeToStringAsyncInternal(obj);
        }

        // 实现IProtobufSerializer接口（有约束）
        public async UniTask<string> SerializeToStringAsync<T>(T obj) where T : class
        {
            return await SerializeToStringAsyncInternal(obj);
        }

        private async UniTask<string> SerializeToStringAsyncInternal<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return string.Empty;
                }

                var bytes = await SerializeAsyncInternal(obj);
                var base64 = Convert.ToBase64String(bytes);
                
                FrameworkLogger.Debug($"Protobuf序列化为字符串完成，长度: {base64.Length} 字符", LogCategory.Core);
                return base64;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Protobuf序列化为字符串失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        // 显式实现ISerializer接口（无约束）
        UniTask<T> ISerializer.DeserializeFromStringAsync<T>(string data)
        {
            return DeserializeFromStringAsyncInternal<T>(data);
        }

        // 实现IProtobufSerializer接口（有约束）
        public async UniTask<T> DeserializeFromStringAsync<T>(string data) where T : class
        {
            return await DeserializeFromStringAsyncInternal<T>(data);
        }

        private async UniTask<T> DeserializeFromStringAsyncInternal<T>(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    return default(T);
                }

                var bytes = Convert.FromBase64String(data);
                var obj = await DeserializeAsyncInternal<T>(bytes);
                
                FrameworkLogger.Debug($"Protobuf从字符串反序列化完成", LogCategory.Core);
                return obj;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Protobuf从字符串反序列化失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public bool IsValidData(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 8) // 至少需要版本头
                    return false;

                // 检查版本头
                var version = BitConverter.ToInt32(data, 0);
                if (version != 1) return false;

                var isCompressed = BitConverter.ToBoolean(data, 4);
                var contentLength = BitConverter.ToInt32(data, 5);
                
                if (contentLength <= 0 || contentLength > data.Length - 9)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // 显式实现ISerializer接口（无约束）
        UniTask<int> ISerializer.GetSerializedSizeAsync<T>(T obj)
        {
            return GetSerializedSizeAsyncInternal(obj);
        }

        // 实现IProtobufSerializer接口（有约束）
        public UniTask<int> GetSerializedSizeAsync<T>(T obj) where T : class
        {
            return GetSerializedSizeAsyncInternal(obj);
        }

        private async UniTask<int> GetSerializedSizeAsyncInternal<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return 0;
                }

                var bytes = await SerializeAsyncInternal(obj);
                return bytes.Length;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Protobuf获取序列化大小失败: {ex.Message}", LogCategory.Core);
                return 0;
            }
        }

        #region 私有方法

        /// <summary>
        /// 检查是否安装了Protobuf支持
        /// </summary>
        private bool HasProtobufSupport()
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
        /// 使用Google.Protobuf进行序列化（无约束版本）
        /// </summary>
        private byte[] SerializeWithProtobufInternal<T>(T obj)
        {
            // 这里需要实际的Google.Protobuf实现
            // 由于是可选扩展包，这里提供占位实现
            throw new NotImplementedException("需要安装Google.Protobuf包才能使用Protobuf序列化");
        }

        /// <summary>
        /// 使用Google.Protobuf进行序列化（有约束版本）
        /// </summary>
        private byte[] SerializeWithProtobuf<T>(T obj) where T : class
        {
            // 这里需要实际的Google.Protobuf实现
            // 由于是可选扩展包，这里提供占位实现
            throw new NotImplementedException("需要安装Google.Protobuf包才能使用Protobuf序列化");
        }

        /// <summary>
        /// 使用Google.Protobuf进行反序列化（无约束版本）
        /// </summary>
        private T DeserializeWithProtobufInternal<T>(byte[] data)
        {
            // 这里需要实际的Google.Protobuf实现
            // 由于是可选扩展包，这里提供占位实现
            throw new NotImplementedException("需要安装Google.Protobuf包才能使用Protobuf序列化");
        }

        /// <summary>
        /// 使用Google.Protobuf进行反序列化（有约束版本）
        /// </summary>
        private T DeserializeWithProtobuf<T>(byte[] data) where T : class
        {
            // 这里需要实际的Google.Protobuf实现
            // 由于是可选扩展包，这里提供占位实现
            throw new NotImplementedException("需要安装Google.Protobuf包才能使用Protobuf序列化");
        }

        /// <summary>
        /// 创建Protobuf格式数据
        /// 格式: [版本(4字节)][压缩标志(1字节)][内容长度(4字节)][内容数据]
        /// </summary>
        private byte[] CreateProtobufFormat(byte[] content, bool isCompressed)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            // 写入版本号
            writer.Write(1);
            
            // 写入压缩标志
            writer.Write(isCompressed);
            
            // 写入内容长度
            writer.Write(content.Length);
            
            // 写入内容数据
            writer.Write(content);
            
            return stream.ToArray();
        }

        /// <summary>
        /// 解析Protobuf格式数据
        /// </summary>
        private (byte[] content, bool isCompressed) ParseProtobufFormat(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);
            
            // 读取版本号
            var version = reader.ReadInt32();
            if (version != 1)
            {
                throw new InvalidDataException($"不支持的Protobuf格式版本: {version}");
            }
            
            // 读取压缩标志
            var isCompressed = reader.ReadBoolean();
            
            // 读取内容长度
            var contentLength = reader.ReadInt32();
            
            // 读取内容数据
            var content = reader.ReadBytes(contentLength);
            
            return (content, isCompressed);
        }

        /// <summary>
        /// 压缩字节数组（LZ4）
        /// </summary>
        private byte[] CompressBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            try
            {
                // 这里需要LZ4压缩实现
                // 由于是可选扩展包，这里提供占位实现
                return data; // 暂时返回原始数据
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"LZ4压缩失败: {ex.Message}", LogCategory.Core);
                return data; // 压缩失败时返回原始数据
            }
        }

        /// <summary>
        /// 解压缩字节数组（LZ4）
        /// </summary>
        private byte[] DecompressBytes(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;

            try
            {
                // 这里需要LZ4解压缩实现
                // 由于是可选扩展包，这里提供占位实现
                return compressedData; // 暂时返回原始数据
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"LZ4解压缩失败: {ex.Message}", LogCategory.Core);
                return compressedData; // 解压缩失败时返回原始数据
            }
        }

        #endregion
    }
}
