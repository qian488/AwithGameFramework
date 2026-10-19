using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking.Realtime;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    public class WebSocketTransportTests
    {
        [Test]
        public async Task RealRealtimeClientRecoversAppliesPushesSendsBusinessAndExchangesHeartbeats()
        {
            using var server = new LocalWebSocketServer();
            var codec = new BinaryRealtimeCodec();
            var recovery = new EmptyRecovery();
            var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var heartbeatReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var businessReady = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            int applications = 0;
            int heartbeats = 0;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var client = new RealtimeClient(server.Uri, new ClientWebSocketTransportFactory(), codec, recovery,
                new RealtimeOptions(maxReconnectAttempts: 0, heartbeatInterval: TimeSpan.FromMilliseconds(15),
                    heartbeatTimeout: TimeSpan.FromSeconds(1)));
            Task session = client.RunSessionAsync(0, (push, token) =>
            {
                Interlocked.Increment(ref applications);
                applied.TrySetResult(true);
                return Task.CompletedTask;
            }, new Dictionary<string, string> { ["Authorization"] = "Bearer fixture" });
            HttpListenerContext context = await server.AcceptAsync();
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture"));
            using WebSocket peer = (await context.AcceptWebSocketAsync(null)).WebSocket;
            byte[] push = codec.Encode(new RealtimeMessage(RealtimeMessageKind.Push, new byte[] { 7 }, sequence: 1));
            await peer.SendAsync(new ArraySegment<byte>(push), WebSocketMessageType.Binary, true, timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(push), WebSocketMessageType.Binary, true, timeout.Token);
            async Task ServeAsync()
            {
                try
                {
                    var buffer = new byte[1024];
                    while (true)
                    {
                        WebSocketReceiveResult result = await peer.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        var message = codec.Decode(buffer.Take(result.Count).ToArray());
                        if (message.Kind == RealtimeMessageKind.Ping)
                        {
                            byte[] pong = codec.Encode(new RealtimeMessage(RealtimeMessageKind.Pong, heartbeatId: message.HeartbeatId));
                            await peer.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Binary, true, timeout.Token);
                            if (Interlocked.Increment(ref heartbeats) >= 3) heartbeatReady.TrySetResult(true);
                        }
                        else if (message.Kind == RealtimeMessageKind.Business) businessReady.TrySetResult(message.Payload);
                    }
                }
                catch (WebSocketException) { }
                catch (OperationCanceledException) { }
            }
            Task serving = ServeAsync();
            await applied.Task.WaitAsync(timeout.Token);
            await client.SendAsync(new byte[] { 42 }, timeout.Token);
            Assert.That(await businessReady.Task.WaitAsync(timeout.Token), Is.EqualTo(new byte[] { 42 }));
            await heartbeatReady.Task.WaitAsync(timeout.Token);
            Assert.That(client.LastAppliedSequence, Is.EqualTo(1));
            Assert.That(applications, Is.EqualTo(1));
            Assert.That(recovery.Calls, Is.EqualTo(1));
            await client.StopAsync();
            await serving.WaitAsync(timeout.Token);
            Assert.That(session.IsCanceled, Is.True);
        }

        [Test]
        public async Task RealSocketHandshakeFragmentedReceiveConcurrentSendsAndClose()
        {
            using var server = new LocalWebSocketServer();
            Task<HttpListenerContext> accepted = server.AcceptAsync();
            await using var client = new ClientWebSocketTransport(64);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task connecting = client.ConnectAsync(server.Uri, new Dictionary<string, string> { ["X-Session"] = "fixture" }, timeout.Token);
            HttpListenerContext context = await accepted;
            Assert.That(context.Request.Headers["X-Session"], Is.EqualTo("fixture"));
            using WebSocket peer = (await context.AcceptWebSocketAsync(null)).WebSocket;
            await connecting;

            Task<byte[]> received = client.ReceiveAsync(timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(new byte[] { 1, 2 }), WebSocketMessageType.Binary, false, timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(new byte[] { 3, 4, 5 }), WebSocketMessageType.Binary, true, timeout.Token);
            Assert.That(await received, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));

            Task sends = Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.SendAsync(new[] { (byte)i }, timeout.Token)));
            var sent = new List<byte>();
            for (int i = 0; i < 8; i++)
            {
                var buffer = new byte[64];
                WebSocketReceiveResult result = await peer.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Binary));
                Assert.That(result.EndOfMessage, Is.True);
                Assert.That(result.Count, Is.EqualTo(1));
                sent.Add(buffer[0]);
            }
            await sends;
            Assert.That(sent, Is.EquivalentTo(Enumerable.Range(0, 8).Select(i => (byte)i)));
            await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            Assert.That(await client.ReceiveAsync(timeout.Token), Is.Null);
            await client.CloseAsync(timeout.Token);
            Assert.That((await peer.ReceiveAsync(new ArraySegment<byte>(new byte[64]), timeout.Token)).MessageType,
                Is.EqualTo(WebSocketMessageType.Close));
        }

        [Test]
        public async Task RealSocketRejectsOversizedMessageAcrossFragments()
        {
            using var server = new LocalWebSocketServer();
            Task<HttpListenerContext> accepted = server.AcceptAsync();
            await using var client = new ClientWebSocketTransport(8);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task connecting = client.ConnectAsync(server.Uri, null, timeout.Token);
            using WebSocket peer = (await (await accepted).AcceptWebSocketAsync(null)).WebSocket;
            await connecting;
            Task<byte[]> received = client.ReceiveAsync(timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(new byte[4]), WebSocketMessageType.Binary, false, timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(new byte[5]), WebSocketMessageType.Binary, true, timeout.Token);
            Assert.That(await Failure(received), Is.TypeOf<RealtimeProtocolException>());
        }

        [Test]
        public async Task RealSocketRejectsTextFrames()
        {
            using var server = new LocalWebSocketServer();
            Task<HttpListenerContext> accepted = server.AcceptAsync();
            await using var client = new ClientWebSocketTransport(64);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task connecting = client.ConnectAsync(server.Uri, null, timeout.Token);
            using WebSocket peer = (await (await accepted).AcceptWebSocketAsync(null)).WebSocket;
            await connecting;
            Task<byte[]> received = client.ReceiveAsync(timeout.Token);
            await peer.SendAsync(new ArraySegment<byte>(new byte[] { 65 }), WebSocketMessageType.Text, true, timeout.Token);
            Assert.That(await Failure(received), Is.TypeOf<RealtimeProtocolException>());
        }

        [Test]
        public async Task RealSocketReceiveCancellationAndDisposeInterruptPendingReads()
        {
            using var server = new LocalWebSocketServer();
            Task<HttpListenerContext> accepted = server.AcceptAsync();
            var client = new ClientWebSocketTransport(64);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task connecting = client.ConnectAsync(server.Uri, null, timeout.Token);
            using WebSocket peer = (await (await accepted).AcceptWebSocketAsync(null)).WebSocket;
            await connecting;
            using var receiveCancellation = new CancellationTokenSource();
            Task<byte[]> received = client.ReceiveAsync(receiveCancellation.Token);
            receiveCancellation.Cancel();
            var error = await Failure(received);
            Assert.That(error, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<WebSocketException>());
            await client.DisposeAsync();
            Assert.That(await Failure(client.ReceiveAsync(timeout.Token)), Is.TypeOf<ObjectDisposedException>());
            await client.DisposeAsync();
        }

        [Test]
        public async Task RealSocketDisposeInterruptsPendingReadWithoutCallerCancellation()
        {
            using var server = new LocalWebSocketServer();
            Task<HttpListenerContext> accepted = server.AcceptAsync();
            var client = new ClientWebSocketTransport(64);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task connecting = client.ConnectAsync(server.Uri, null, timeout.Token);
            using WebSocket peer = (await (await accepted).AcceptWebSocketAsync(null)).WebSocket;
            await connecting;
            Task<byte[]> received = client.ReceiveAsync(CancellationToken.None);
            await client.DisposeAsync();
            var error = await Failure(received);
            Assert.That(error, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<WebSocketException>());
        }

        private static async Task<Exception> Failure(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error) { return error; }
            Assert.Fail("The operation unexpectedly succeeded.");
            return null;
        }

        private sealed class LocalWebSocketServer : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            internal Uri Uri { get; }
            internal LocalWebSocketServer()
            {
                var portReservation = new TcpListener(IPAddress.Loopback, 0);
                portReservation.Start();
                int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
                portReservation.Stop();
                string route = "127.0.0.1:" + port + "/" + Guid.NewGuid().ToString("N") + "/";
                listener.Prefixes.Add("http://" + route);
                listener.Start();
                Uri = new Uri("ws://" + route);
            }
            internal Task<HttpListenerContext> AcceptAsync() => listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            public void Dispose() => listener.Close();
        }

        private sealed class EmptyRecovery : IPushRecovery
        {
            internal int Calls;
            public Task<PushRecoveryBatch> RecoverAsync(long afterSequence, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(new PushRecoveryBatch(afterSequence, Array.Empty<RealtimePush>()));
            }
        }
    }
}
