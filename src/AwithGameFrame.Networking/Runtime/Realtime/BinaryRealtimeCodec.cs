using System;
using System.IO;
using System.Text;

namespace AwithGameFrame.Networking.Realtime
{
    /// <summary>Explicit AWRT v1 wire format; replace this codec when integrating an existing server.</summary>
    public sealed class BinaryRealtimeCodec : IRealtimeCodec
    {
        public const int HeaderSize = 26;

        public byte[] Encode(RealtimeMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                writer.Write(new byte[] { 65, 87, 82, 84 });
                writer.Write((byte)1);
                writer.Write((byte)message.Kind);
                writer.Write(message.Sequence);
                writer.Write(message.HeartbeatId);
                writer.Write(message.Payload.Length);
                writer.Write(message.Payload);
                return output.ToArray();
            }
        }

        public RealtimeMessage Decode(byte[] message)
        {
            if (message == null || message.Length < HeaderSize)
                throw new RealtimeProtocolException("The realtime message is shorter than its header.");
            try
            {
                using (var input = new MemoryStream(message, false))
                using (var reader = new BinaryReader(input, Encoding.UTF8, true))
                {
                    if (reader.ReadByte() != 65 || reader.ReadByte() != 87 || reader.ReadByte() != 82 ||
                        reader.ReadByte() != 84 || reader.ReadByte() != 1)
                        throw new RealtimeProtocolException("The realtime wire magic or version is invalid.");
                    var kind = (RealtimeMessageKind)reader.ReadByte();
                    long sequence = reader.ReadInt64();
                    long heartbeatId = reader.ReadInt64();
                    int length = reader.ReadInt32();
                    if (length < 0 || length != message.Length - HeaderSize)
                        throw new RealtimeProtocolException("The realtime payload length is invalid.");
                    return new RealtimeMessage(kind, reader.ReadBytes(length), sequence, heartbeatId);
                }
            }
            catch (ArgumentException error) { throw new RealtimeProtocolException("The realtime message fields are invalid.", error); }
        }
    }
}
