using System;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Core.Interfaces;
using Newtonsoft.Json;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>
    /// Newtonsoft.Json序列化操作提供者
    /// 基于Newtonsoft.Json的高性能序列化实现
    /// </summary>
    public class NewtonsoftJsonProvider : ISerializationProvider
    {
        private readonly JsonSerializerSettings _settings;

        public NewtonsoftJsonProvider(JsonSerializerSettings settings = null)
        {
            _settings = settings ?? new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
        }

        public UniTask<string> SerializeAsync<T>(T obj)
        {
            try
            {
                var json = JsonConvert.SerializeObject(obj, _settings);
                return UniTask.FromResult(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"序列化失败: {ex.Message}", ex);
            }
        }

        public UniTask<T> DeserializeAsync<T>(string json)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<T>(json, _settings);
                return UniTask.FromResult(obj);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"反序列化失败: {ex.Message}", ex);
            }
        }

        public UniTask<byte[]> SerializeToBytesAsync<T>(T obj)
        {
            return SerializeAsync(obj).ContinueWith(json => System.Text.Encoding.UTF8.GetBytes(json));
        }

        public UniTask<T> DeserializeFromBytesAsync<T>(byte[] bytes)
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return DeserializeAsync<T>(json);
        }

        public bool IsValidJson(string json)
        {
            try
            {
                JsonConvert.DeserializeObject(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetFormatName()
        {
            return "Newtonsoft.Json";
        }
    }
}
