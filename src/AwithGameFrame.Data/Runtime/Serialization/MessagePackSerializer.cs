using System.Threading.Tasks;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>Compatibility facade for a host-registered MessagePack implementation.</summary>
    public sealed class MessagePackSerializer : IMessagePackSerializer
    {
        private readonly ISerializer serializer;
        public MessagePackSerializer(SerializationConfig config = null)
        {
            serializer = SerializerFactory.CreateSerializer(SerializationFormat.MessagePack, config);
        }
        public SerializationFormat Format => serializer.Format;
        public bool SupportsCompression => serializer.SupportsCompression;
        public Task<byte[]> SerializeAsync<T>(T obj) => serializer.SerializeAsync(obj);
        public Task<T> DeserializeAsync<T>(byte[] data) => serializer.DeserializeAsync<T>(data);
        public Task<string> SerializeToStringAsync<T>(T obj) => serializer.SerializeToStringAsync(obj);
        public Task<T> DeserializeFromStringAsync<T>(string data) => serializer.DeserializeFromStringAsync<T>(data);
        public bool IsValidData(byte[] data) => serializer.IsValidData(data);
        public Task<int> GetSerializedSizeAsync<T>(T obj) => serializer.GetSerializedSizeAsync(obj);
    }
}
