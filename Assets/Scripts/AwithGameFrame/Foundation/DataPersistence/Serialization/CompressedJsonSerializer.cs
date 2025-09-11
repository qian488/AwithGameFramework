using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Foundation.Logging;
using UnityEngine;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 压缩JSON序列化器
    /// 使用JSON+压缩的现代序列化方案，替代已弃用的BinaryFormatter
    /// 提供更好的性能、安全性和压缩效果
    /// </summary>
    public class CompressedJsonSerializer : ISerializer
    {
        private readonly SerializationConfig _config;

        public SerializationFormat Format => SerializationFormat.Binary;
        public bool SupportsCompression => true;

        public CompressedJsonSerializer(SerializationConfig config = null)
        {
            _config = config ?? new SerializationConfig();
        }

        public UniTask<byte[]> SerializeAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return UniTask.FromResult(new byte[0]);
                }

                // 对于基本类型，直接转换为字符串
                string json;
                if (obj is string str)
                {
                    json = $"\"{str}\"";
                }
                else if (obj is int || obj is float || obj is double || obj is bool || obj is long || obj is ulong)
                {
                    json = obj.ToString();
                }
                else
                {
                    // 对于复杂对象，使用JSON序列化
                    json = JsonUtility.ToJson(obj, _config.PrettyPrint);
                }
                
                var bytes = Encoding.UTF8.GetBytes(json);
                
                // 如果启用压缩，进行GZip压缩
                if (_config.EnableCompression)
                {
                    bytes = CompressBytes(bytes);
                }
                
                // 添加版本头和压缩标志
                var result = CreateBinaryFormat(bytes, _config.EnableCompression);
                
                FrameworkLogger.Debug($"Binary序列化完成，大小: {result.Length} bytes", LogCategory.Core);
                return UniTask.FromResult(result);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Binary序列化失败: {ex.Message}", LogCategory.Core);
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

                // 解析二进制格式
                var (content, isCompressed) = ParseBinaryFormat(data);
                
                // 如果启用压缩，进行GZip解压缩
                if (isCompressed)
                {
                    content = DecompressBytes(content);
                }

                // 反序列化
                var json = Encoding.UTF8.GetString(content);
                T obj;
                
                // 对于基本类型，直接解析
                if (typeof(T) == typeof(string))
                {
                    // 移除JSON字符串的引号
                    obj = (T)(object)json.Trim('"');
                }
                else if (typeof(T) == typeof(int))
                {
                    obj = (T)(object)int.Parse(json);
                }
                else if (typeof(T) == typeof(float))
                {
                    obj = (T)(object)float.Parse(json);
                }
                else if (typeof(T) == typeof(double))
                {
                    obj = (T)(object)double.Parse(json);
                }
                else if (typeof(T) == typeof(bool))
                {
                    obj = (T)(object)bool.Parse(json);
                }
                else if (typeof(T) == typeof(long))
                {
                    obj = (T)(object)long.Parse(json);
                }
                else if (typeof(T) == typeof(ulong))
                {
                    obj = (T)(object)ulong.Parse(json);
                }
                else
                {
                    // 对于复杂对象，使用JSON反序列化
                    obj = JsonUtility.FromJson<T>(json);
                }
                
                FrameworkLogger.Debug($"Binary反序列化完成", LogCategory.Core);
                return UniTask.FromResult(obj);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Binary反序列化失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public async UniTask<string> SerializeToStringAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return string.Empty;
                }

                var bytes = await SerializeAsync(obj);
                var base64 = Convert.ToBase64String(bytes);
                
                FrameworkLogger.Debug($"Binary序列化为字符串完成，长度: {base64.Length} 字符", LogCategory.Core);
                return base64;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Binary序列化为字符串失败: {ex.Message}", LogCategory.Core);
                throw;
            }
        }

        public async UniTask<T> DeserializeFromStringAsync<T>(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    return default(T);
                }

                var bytes = Convert.FromBase64String(data);
                var obj = await DeserializeAsync<T>(bytes);
                
                FrameworkLogger.Debug($"Binary从字符串反序列化完成", LogCategory.Core);
                return obj;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Binary从字符串反序列化失败: {ex.Message}", LogCategory.Core);
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

        public async UniTask<int> GetSerializedSizeAsync<T>(T obj)
        {
            try
            {
                if (obj == null)
                {
                    return 0;
                }

                var bytes = await SerializeAsync(obj);
                return bytes.Length;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Binary获取序列化大小失败: {ex.Message}", LogCategory.Core);
                return 0;
            }
        }

        #region 私有方法

        /// <summary>
        /// 创建二进制格式数据
        /// 格式: [版本(4字节)][压缩标志(1字节)][内容长度(4字节)][内容数据]
        /// </summary>
        /// <param name="content">内容数据</param>
        /// <param name="isCompressed">是否压缩</param>
        /// <returns>二进制格式数据</returns>
        private byte[] CreateBinaryFormat(byte[] content, bool isCompressed)
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
        /// 解析二进制格式数据
        /// </summary>
        /// <param name="data">二进制格式数据</param>
        /// <returns>内容数据和压缩标志</returns>
        private (byte[] content, bool isCompressed) ParseBinaryFormat(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);
            
            // 读取版本号
            var version = reader.ReadInt32();
            if (version != 1)
            {
                throw new InvalidDataException($"不支持的二进制格式版本: {version}");
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
        /// 压缩字节数组
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <returns>压缩后的数据</returns>
        private byte[] CompressBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            try
            {
                using var memoryStream = new MemoryStream();
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                {
                    gzipStream.Write(data, 0, data.Length);
                }
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"GZip压缩失败: {ex.Message}", LogCategory.Core);
                return data; // 压缩失败时返回原始数据
            }
        }

        /// <summary>
        /// 解压缩字节数组
        /// </summary>
        /// <param name="compressedData">压缩的数据</param>
        /// <returns>解压缩后的数据</returns>
        private byte[] DecompressBytes(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;

            try
            {
                using var memoryStream = new MemoryStream(compressedData);
                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                using var resultStream = new MemoryStream();
                gzipStream.CopyTo(resultStream);
                return resultStream.ToArray();
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"GZip解压缩失败: {ex.Message}", LogCategory.Core);
                return compressedData; // 解压缩失败时返回原始数据
            }
        }

        #endregion
    }
}