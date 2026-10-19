using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>Portable JSON with support for public fields, properties, and existing Unity JSON data.</summary>
    public sealed class JsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings settings;
        public SerializationFormat Format => SerializationFormat.Json;
        public bool SupportsCompression => false;

        public JsonSerializer(SerializationConfig config = null)
        {
            config = (config ?? new SerializationConfig()).CreateSnapshot(false);
            settings = PortableJson.CreateSettings(config);
        }

        public Task<byte[]> SerializeAsync<T>(T obj) => Task.FromResult(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, settings)));
        public Task<T> DeserializeAsync<T>(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (IsLegacyEnvelope(data)) return new CompressedJsonSerializer().DeserializeAsync<T>(data);
            return DeserializeFromStringAsync<T>(Encoding.UTF8.GetString(data));
        }
        public Task<string> SerializeToStringAsync<T>(T obj) => Task.FromResult(JsonConvert.SerializeObject(obj, settings));
        public Task<T> DeserializeFromStringAsync<T>(string json)
        {
            try { return Task.FromResult(JsonConvert.DeserializeObject<T>(json, settings)); }
            catch (JsonException)
            {
                byte[] legacyData = null;
                try { legacyData = Convert.FromBase64String(json); }
                catch (FormatException) { }
                if (IsLegacyEnvelope(legacyData)) return new CompressedJsonSerializer().DeserializeAsync<T>(legacyData);
                throw;
            }
        }
        public bool IsValidData(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (IsLegacyEnvelope(data)) return true;
            try { Newtonsoft.Json.Linq.JToken.Parse(Encoding.UTF8.GetString(data)); return true; }
            catch (JsonException) { return false; }
        }
        public async Task<int> GetSerializedSizeAsync<T>(T obj) => (await SerializeAsync(obj)).Length;

        // The old Json factory returned the version-1 binary codec. Only recognize its
        // complete envelope signature; all newly written Json data remains plain JSON.
        private static bool IsLegacyEnvelope(byte[] data)
        {
            return data != null && data.Length >= 9 && BitConverter.ToInt32(data, 0) == 1
                && data[4] <= 1 && BitConverter.ToInt32(data, 5) == data.Length - 9;
        }
    }
}
