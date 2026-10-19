using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking.Realtime
{
    public sealed class ClientWebSocketTransportFactory : IWebSocketTransportFactory
    {
        public IWebSocketTransport Create(int maxMessageBytes) => new ClientWebSocketTransport(maxMessageBytes);
    }

    /// <summary>Owns one BCL WebSocket. One send and one receive may run concurrently.</summary>
    public sealed class ClientWebSocketTransport : IWebSocketTransport
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim receiveGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly int maxMessageBytes;
        private int disposed;
        private int connectStarted;

        public ClientWebSocketTransport(int maxMessageBytes = 1024 * 1024)
        {
            if (maxMessageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            this.maxMessageBytes = maxMessageBytes;
            // Application Ping/Pong is implemented by RealtimeClient, independently of WebSocket keepalive.
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
        }

        public async Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("An absolute WS or WSS URI is required.", nameof(uri));
            if (Interlocked.Exchange(ref connectStarted, 1) != 0) throw new InvalidOperationException("A transport connects only once.");
            var snapshot = TransportRequest.CopyHeaders(headers);
            foreach (var header in snapshot) socket.Options.SetRequestHeader(header.Key, header.Value);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            using (linked.Token.Register(Abort))
                await socket.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
        }

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Length > maxMessageBytes) throw new RealtimeProtocolException("The outgoing message exceeds the size limit.");
            ThrowIfDisposed();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    using (linked.Token.Register(Abort))
                        await socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, linked.Token).ConfigureAwait(false);
                }
                finally { sendGate.Release(); }
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await receiveGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    using (linked.Token.Register(Abort))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[Math.Min(8192, maxMessageBytes)];
                        while (true)
                        {
                            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return null;
                            if (result.MessageType != WebSocketMessageType.Binary)
                                throw new RealtimeProtocolException("Only binary WebSocket messages are supported.");
                            if (result.Count > maxMessageBytes - output.Length)
                                throw new RealtimeProtocolException("The incoming message exceeds the size limit.");
                            output.Write(buffer, 0, result.Count);
                            if (result.EndOfMessage) return output.ToArray();
                        }
                    }
                }
                catch { Abort(); throw; }
                finally { receiveGate.Release(); }
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        using (linked.Token.Register(Abort))
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session closed", linked.Token).ConfigureAwait(false);
                }
                finally { sendGate.Release(); }
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                lifetime.Cancel();
                Abort();
                socket.Dispose();
                lifetime.Dispose();
                // Keep the small synchronization primitives alive for operations finishing cancellation.
            }
            return default;
        }

        private void Abort() { try { socket.Abort(); } catch (ObjectDisposedException) { } }
        private void ThrowIfDisposed() { if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ClientWebSocketTransport)); }
    }
}
