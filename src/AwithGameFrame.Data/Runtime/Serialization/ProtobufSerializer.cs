using System.Threading.Tasks;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>Compatibility facade for a host-registered Protobuf implementation.</summary>
    public sealed class ProtobufSerializer : IProtobufSerializer
    {
        private readonly ISerializer serializer;
        public ProtobufSerializer(ProtobufSerializationConfig config = null)
        {
            serializer = SerializerFactory.CreateSerializer(SerializationFormat.Protobuf, config);
        }
        public SerializationFormat Format => serializer.Format;
        public bool SupportsCompression => serializer.SupportsCompression;
        Task<byte[]> ISerializer.SerializeAsync<T>(T obj) => serializer.SerializeAsync(obj);
        Task<T> ISerializer.DeserializeAsync<T>(byte[] data) => serializer.DeserializeAsync<T>(data);
        Task<string> ISerializer.SerializeToStringAsync<T>(T obj) => serializer.SerializeToStringAsync(obj);
        Task<T> ISerializer.DeserializeFromStringAsync<T>(string data) => serializer.DeserializeFromStringAsync<T>(data);
        Task<int> ISerializer.GetSerializedSizeAsync<T>(T obj) => serializer.GetSerializedSizeAsync(obj);
        public Task<byte[]> SerializeAsync<T>(T obj) where T : class => serializer.SerializeAsync(obj);
        public Task<T> DeserializeAsync<T>(byte[] data) where T : class => serializer.DeserializeAsync<T>(data);
        public Task<string> SerializeToStringAsync<T>(T obj) where T : class => serializer.SerializeToStringAsync(obj);
        public Task<T> DeserializeFromStringAsync<T>(string data) where T : class => serializer.DeserializeFromStringAsync<T>(data);
        public Task<int> GetSerializedSizeAsync<T>(T obj) where T : class => serializer.GetSerializedSizeAsync(obj);
        public bool IsValidData(byte[] data) => serializer.IsValidData(data);
    }
}
