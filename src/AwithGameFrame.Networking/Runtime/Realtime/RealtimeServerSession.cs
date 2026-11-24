using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking.Realtime
{
    /// <summary>Runs one accepted binary WebSocket session. The host owns authentication and the socket lifetime.</summary>
    public static class RealtimeServerSession
    {
        public static async Task RunAsync(WebSocket socket, IRealtimeCodec codec,
            Func<byte[], CancellationToken, Task<RealtimePush>> onCommand, CancellationToken cancellationToken,
            int maxMessageBytes = 1024 * 1024)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            if (onCommand == null) throw new ArgumentNullException(nameof(onCommand));
            if (maxMessageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));

            var buffer = new byte[Math.Min(maxMessageBytes, 8192)];
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadBinaryAsync(socket, buffer, maxMessageBytes, cancellationToken).ConfigureAwait(false);
                if (frame == null)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session closed", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var message = codec.Decode(frame);
                RealtimeMessage reply;
                switch (message.Kind)
                {
                    case RealtimeMessageKind.Ping:
                        reply = new RealtimeMessage(RealtimeMessageKind.Pong, heartbeatId: message.HeartbeatId);
                        break;
                    case RealtimeMessageKind.Business:
                        var push = await onCommand(message.Payload, cancellationToken).ConfigureAwait(false);
                        if (push == null) continue;
                        reply = new RealtimeMessage(RealtimeMessageKind.Push, push.Payload, sequence: push.Sequence);
                        break;
                    default:
                        throw new RealtimeProtocolException("The server accepts client commands and Ping messages.");
                }
                var payload = codec.Encode(reply);
                if (payload.Length > maxMessageBytes) throw new RealtimeProtocolException("The outgoing message exceeds the size limit.");
                await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task<byte[]> ReadBinaryAsync(WebSocket socket, byte[] buffer, int maxMessageBytes, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            while (true)
            {
                var part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close) return null;
                if (part.MessageType != WebSocketMessageType.Binary)
                    throw new RealtimeProtocolException("Only binary WebSocket messages are supported.");
                if (part.Count > maxMessageBytes - output.Length)
                    throw new RealtimeProtocolException("The incoming message exceeds the size limit.");
                output.Write(buffer, 0, part.Count);
                if (part.EndOfMessage) return output.ToArray();
            }
        }
    }
}
