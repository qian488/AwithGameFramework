using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwithGameFrame.Networking.Realtime;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    public class RealtimeTests
    {
        private static readonly Uri Endpoint = new Uri("ws://localhost/game");
        private static readonly BinaryRealtimeCodec Codec = new BinaryRealtimeCodec();

        [TestCase(RealtimeMessageKind.Business, 0, 0)]
        [TestCase(RealtimeMessageKind.Push, 7, 0)]
        [TestCase(RealtimeMessageKind.Ping, 0, 19)]
        [TestCase(RealtimeMessageKind.Pong, 0, 19)]
        public void BinaryCodecHasExplicitValidatedWireFields(RealtimeMessageKind kind, long sequence, long heartbeat)
        {
            var message = new RealtimeMessage(kind, heartbeat == 0 ? new byte[] { 1, 2, 3 } : null, sequence, heartbeat);
            byte[] bytes = Codec.Encode(message);
            var decoded = Codec.Decode(bytes);
            Assert.That(decoded.Kind, Is.EqualTo(kind));
            Assert.That(decoded.Sequence, Is.EqualTo(sequence));
            Assert.That(decoded.HeartbeatId, Is.EqualTo(heartbeat));
            Assert.That(decoded.Payload, Is.EqualTo(message.Payload));
            bytes[0] = 0;
            Assert.Throws<RealtimeProtocolException>(() => Codec.Decode(bytes));
        }

        [Test]
        public void BinaryCodecRejectsTrailingDataAndInvalidHeartbeat()
        {
            byte[] bytes = Codec.Encode(new RealtimeMessage(RealtimeMessageKind.Business));
            Assert.Throws<RealtimeProtocolException>(() => Codec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
            bytes[5] = (byte)RealtimeMessageKind.Ping;
            Assert.Throws<RealtimeProtocolException>(() => Codec.Decode(bytes));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeMessage(RealtimeMessageKind.Push));
        }

        [Test]
        public void ReconnectDelaysAreFiniteExponentialAndCapped()
        {
            var options = new RealtimeOptions(initialReconnectDelay: TimeSpan.FromMilliseconds(10),
                maxReconnectDelay: TimeSpan.FromMilliseconds(25));
            Assert.That(Enumerable.Range(1, 5).Select(n => options.GetReconnectDelay(n).TotalMilliseconds),
                Is.EqualTo(new double[] { 10, 20, 25, 25, 25 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeOptions(maxReconnectAttempts: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeOptions(heartbeatTimeout: TimeSpan.Zero));
        }

        [Test]
        public async Task InitialRecoveryThenLivePushesAreOrderedAndDuplicatesAreIgnored()
        {
            var transport = new FakeTransport();
            var recovery = new Recovery((cursor, call) => Batch(2, 1, 2));
            await using var client = Client(new Factory(transport), recovery);
            var applied = new ConcurrentQueue<long>();
            Task session = client.RunSessionAsync(0, (push, token) => { applied.Enqueue(push.Sequence); return Task.CompletedTask; });
            await Until(() => client.IsConnected);
            transport.Push(2);
            transport.Push(3);
            await Until(() => client.LastAppliedSequence == 3);
            Assert.That(applied.ToArray(), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(recovery.Cursors.ToArray(), Is.EqualTo(new long[] { 0 }));
            await client.StopAsync();
            Assert.That(session.IsCanceled, Is.True);
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CursorCommitsOnlyAfterHandlerCompletesSuccessfully()
        {
            var transport = new FakeTransport();
            var entered = Signal();
            var release = Signal();
            await using var client = Client(new Factory(transport));
            _ = client.RunSessionAsync(0, async (push, token) => { entered.TrySetResult(true); await release.Task; });
            await Until(() => client.IsConnected);
            transport.Push(1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(client.LastAppliedSequence, Is.Zero);
            release.SetResult(true);
            await Until(() => client.LastAppliedSequence == 1);
        }

        [Test]
        public async Task SequenceGapRecoversFromLastAppliedBeforeApplyingTheIncomingPush()
        {
            var transport = new FakeTransport();
            var recovery = new Recovery((cursor, call) => call == 1 ? Batch(0) : Batch(2, 2));
            await using var client = Client(new Factory(transport), recovery);
            var applied = new ConcurrentQueue<long>();
            _ = client.RunSessionAsync(0, (push, token) => { applied.Enqueue(push.Sequence); return Task.CompletedTask; });
            await Until(() => client.IsConnected);
            transport.Push(1);
            transport.Push(3);
            transport.Push(3);
            await Until(() => client.LastAppliedSequence == 3);
            Assert.That(applied.ToArray(), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(recovery.Cursors.ToArray(), Is.EqualTo(new long[] { 0, 1 }));
        }

        [Test]
        public async Task RecoveryCannotSilentlySkipAMissingSequence()
        {
            var transport = new FakeTransport();
            var factory = new Factory(transport);
            var recovery = new Recovery((cursor, call) => Batch(cursor));
            await using var client = Client(factory, recovery);
            Task session = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => client.IsConnected);
            transport.Push(1);
            transport.Push(3);
            Assert.That(await Failure(session), Is.TypeOf<PushRecoveryException>());
            Assert.That(client.LastAppliedSequence, Is.EqualTo(1));
            Assert.That(factory.Count, Is.EqualTo(1));
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task EntireRecoveryBatchIsValidatedBeforeAnyRecoveredPushIsApplied()
        {
            var transport = new FakeTransport();
            var applied = new ConcurrentQueue<long>();
            await using var client = Client(new Factory(transport), new Recovery((cursor, call) => Batch(2, 1, 3)));
            Task session = client.RunSessionAsync(0, (push, token) => { applied.Enqueue(push.Sequence); return Task.CompletedTask; });
            Assert.That(await Failure(session), Is.TypeOf<PushRecoveryException>());
            Assert.That(applied, Is.Empty);
            Assert.That(client.LastAppliedSequence, Is.Zero);
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AFailedBusinessSendIsNeverReplayedAndReconnectRecoversPushes()
        {
            var first = new FakeTransport { OnSend = message => message.Kind == RealtimeMessageKind.Business ?
                Task.FromException(new IOException("Delivery is unknown")) : Task.CompletedTask };
            var second = new FakeTransport();
            var factory = new Factory(first, second);
            var recovery = new Recovery((cursor, call) => call == 1 ? Batch(0) : Batch(1, 1));
            await using var client = Client(factory, recovery);
            _ = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => client.IsConnected);
            Assert.That(await Failure(client.SendAsync(new byte[] { 42 })), Is.TypeOf<IOException>());
            await Until(() => factory.Count == 2 && client.IsConnected && client.LastAppliedSequence == 1);
            Assert.That(first.Sent.Count(m => m.Kind == RealtimeMessageKind.Business), Is.EqualTo(1));
            Assert.That(second.Sent.Count(m => m.Kind == RealtimeMessageKind.Business), Is.Zero);
            Assert.That(recovery.Cursors.ToArray(), Is.EqualTo(new long[] { 0, 0 }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task HeartbeatTimeoutExhaustsFiniteReconnectBudgetAndDisposesEverySocket()
        {
            var transports = new[] { new FakeTransport(), new FakeTransport(), new FakeTransport() };
            var factory = new Factory(transports);
            var options = FastHeartbeat(maxReconnectAttempts: 2);
            await using var client = Client(factory, options: options);
            Task session = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            var error = await Failure(session);
            Assert.That(error, Is.TypeOf<RealtimeDisconnectedException>());
            Assert.That(error.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(factory.Count, Is.EqualTo(3));
            Assert.That(transports.All(t => t.DisposeCount == 1 && t.CloseCount == 1), Is.True);
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task HeartbeatRequiresTheMatchingPong()
        {
            var transport = new FakeTransport();
            transport.OnSend = message =>
            {
                if (message.Kind == RealtimeMessageKind.Ping)
                    transport.Incoming(new RealtimeMessage(RealtimeMessageKind.Pong, heartbeatId: message.HeartbeatId + 1));
                return Task.CompletedTask;
            };
            await using var client = Client(new Factory(transport), options: FastHeartbeat(0));
            Assert.That(await Failure(client.RunSessionAsync(0, (push, token) => Task.CompletedTask)),
                Is.TypeOf<RealtimeDisconnectedException>());
            Assert.That(transport.Sent.Count(m => m.Kind == RealtimeMessageKind.Ping), Is.EqualTo(1));
        }

        [Test]
        public async Task SlowPushHandlerDoesNotBlockReceivingHeartbeatAcknowledgments()
        {
            var transport = new FakeTransport();
            var entered = Signal();
            var release = Signal();
            var heartbeats = Signal();
            int pingCount = 0;
            transport.OnSend = message =>
            {
                if (message.Kind == RealtimeMessageKind.Ping)
                {
                    transport.Incoming(new RealtimeMessage(RealtimeMessageKind.Pong, heartbeatId: message.HeartbeatId));
                    if (Interlocked.Increment(ref pingCount) >= 4) heartbeats.TrySetResult(true);
                }
                return Task.CompletedTask;
            };
            var factory = new Factory(transport);
            await using var client = Client(factory, options: FastHeartbeat(0));
            Task session = client.RunSessionAsync(0, async (push, token) => { entered.TrySetResult(true); await release.Task; });
            await Until(() => client.IsConnected);
            transport.Push(1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await heartbeats.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(session.IsCompleted, Is.False);
            Assert.That(factory.Count, Is.EqualTo(1));
            Assert.That(client.LastAppliedSequence, Is.Zero);
            release.SetResult(true);
            await Until(() => client.LastAppliedSequence == 1);
        }

        [Test]
        public async Task ServerPingGetsAnExplicitCorrelatedPong()
        {
            var transport = new FakeTransport();
            await using var client = Client(new Factory(transport));
            _ = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => client.IsConnected);
            transport.Incoming(new RealtimeMessage(RealtimeMessageKind.Ping, heartbeatId: 45));
            await Until(() => transport.Sent.Any(m => m.Kind == RealtimeMessageKind.Pong));
            Assert.That(transport.Sent.Single().HeartbeatId, Is.EqualTo(45));
        }

        [Test]
        public async Task ConcurrentBusinessSendsAreSerialized()
        {
            var transport = new FakeTransport { OnSend = message => Task.Delay(5) };
            await using var client = Client(new Factory(transport));
            _ = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => client.IsConnected);
            await Task.WhenAll(Enumerable.Range(0, 15).Select(i => client.SendAsync(new[] { (byte)i })));
            Assert.That(transport.MaxConcurrentSends, Is.EqualTo(1));
            Assert.That(transport.Sent.Count, Is.EqualTo(15));
            Assert.That(transport.ReceiveCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task CancellationInterruptsAnUncooperativeConnectAndReleasesItsTransport()
        {
            var pendingConnect = Signal();
            var transport = new FakeTransport { OnConnect = token => pendingConnect.Task };
            var recovery = new Recovery();
            await using var client = Client(new Factory(transport), recovery);
            using var cancellation = new CancellationTokenSource();
            Task session = client.RunSessionAsync(0, (push, token) => Task.CompletedTask, cancellationToken: cancellation.Token);
            cancellation.Cancel();
            Assert.That(await Failure(session), Is.InstanceOf<OperationCanceledException>());
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
            pendingConnect.SetResult(true);
            await Task.Yield();
            Assert.That(recovery.Cursors, Is.Empty);
        }

        [Test]
        public async Task ReplacedSessionCannotDeliverAnUncooperativeLateReceive()
        {
            var lateReceive = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new FakeTransport { OnReceive = token => lateReceive.Task };
            var second = new FakeTransport();
            var factory = new Factory(first, second);
            var oldApplied = new ConcurrentQueue<long>();
            var newApplied = new ConcurrentQueue<long>();
            await using var client = Client(factory);
            Task old = client.RunSessionAsync(0, (push, token) => { oldApplied.Enqueue(push.Sequence); return Task.CompletedTask; });
            await Until(() => first.ReceiveCount == 1);
            _ = client.RunSessionAsync(10, (push, token) => { newApplied.Enqueue(push.Sequence); return Task.CompletedTask; });
            Assert.That(await Failure(old), Is.InstanceOf<OperationCanceledException>());
            await Until(() => second.ReceiveCount == 1);
            lateReceive.SetResult(Codec.Encode(new RealtimeMessage(RealtimeMessageKind.Push, sequence: 1)));
            second.Push(11);
            await Until(() => client.LastAppliedSequence == 11);
            Assert.That(oldApplied, Is.Empty);
            Assert.That(newApplied.ToArray(), Is.EqualTo(new long[] { 11 }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReplacedSessionCannotCommitItsAlreadyStartedHandler()
        {
            var first = new FakeTransport();
            var second = new FakeTransport();
            var entered = Signal();
            var release = Signal();
            await using var client = Client(new Factory(first, second));
            Task old = client.RunSessionAsync(0, async (push, token) => { entered.SetResult(true); await release.Task; });
            await Until(() => client.IsConnected);
            first.Push(1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            _ = client.RunSessionAsync(10, (push, token) => Task.CompletedTask);
            Assert.That(await Failure(old), Is.InstanceOf<OperationCanceledException>());
            release.SetResult(true);
            await Until(() => second.ReceiveCount == 1);
            Assert.That(client.LastAppliedSequence, Is.EqualTo(10));
        }

        [Test]
        public async Task CancelledRecoveryCannotApplyItsLateResult()
        {
            var pending = new TaskCompletionSource<PushRecoveryBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovery = new Recovery { OnRecover = (cursor, call, token) => pending.Task };
            var transport = new FakeTransport();
            int applied = 0;
            await using var client = Client(new Factory(transport), recovery);
            Task session = client.RunSessionAsync(0, (push, token) => { Interlocked.Increment(ref applied); return Task.CompletedTask; });
            await Until(() => recovery.Cursors.Count == 1);
            await client.StopAsync();
            pending.SetResult(Batch(1, 1));
            await Task.Yield();
            Assert.That(session.IsCanceled, Is.True);
            Assert.That(applied, Is.Zero);
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task HandlerFailureDoesNotReconnectOrCommitAndCloseFailureDoesNotHideIt()
        {
            var transport = new FakeTransport { FailClose = true };
            var factory = new Factory(transport);
            await using var client = Client(factory);
            Task session = client.RunSessionAsync(0, (push, token) => Task.FromException(new IOException("Business failure")));
            await Until(() => client.IsConnected);
            transport.Push(1);
            var failure = await Failure(session);
            Assert.That(failure, Is.TypeOf<PushApplicationException>());
            Assert.That(failure.InnerException.Message, Is.EqualTo("Business failure"));
            Assert.That(client.LastAppliedSequence, Is.Zero);
            Assert.That(factory.Count, Is.EqualTo(1));
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellationInterruptsReconnectBackoff()
        {
            var first = new FakeTransport { OnConnect = token => Task.FromException(new IOException("Offline")) };
            var factory = new Factory(first);
            await using var client = Client(factory, options: new RealtimeOptions(initialReconnectDelay: TimeSpan.FromSeconds(10)));
            Task session = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => first.DisposeCount == 1);
            await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(session.IsCanceled, Is.True);
            Assert.That(factory.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task StopWaitsForCleanupOfSessionsThatHaveAlreadyBeenReplaced()
        {
            var closing = Signal();
            var releaseClose = Signal();
            var first = new FakeTransport
            {
                OnClose = async token => { closing.TrySetResult(true); await releaseClose.Task.WaitAsync(token); }
            };
            var second = new FakeTransport();
            await using var client = Client(new Factory(first, second));
            Task old = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await Until(() => first.ReceiveCount == 1);
            Task latest = client.RunSessionAsync(0, (push, token) => Task.CompletedTask);
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Task stop = client.StopAsync();
            Assert.That(stop.IsCompleted, Is.False);
            Assert.That(first.DisposeCount, Is.Zero);
            releaseClose.SetResult(true);
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(old.IsCanceled && latest.IsCanceled, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Repeat(10)]
        public async Task ThrowingCancellationCallbackDoesNotPreventSocketCleanupOrSessionCompletion(bool externalCancellation)
        {
            var transport = new FakeTransport();
            var entered = Signal();
            var cancellationEntered = Signal();
            using var callbackRelease = new ManualResetEventSlim();
            using var external = new CancellationTokenSource();
            await using var client = Client(new Factory(transport));
            Task session = client.RunSessionAsync(0, async (push, token) =>
            {
                token.Register(() =>
                {
                    cancellationEntered.TrySetResult(true);
                    if (!callbackRelease.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("The fixture did not release cancellation.");
                    throw new InvalidOperationException("Cancellation callback failed");
                });
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }, cancellationToken: external.Token);
            await Until(() => client.IsConnected);
            transport.Push(1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Task stopping = Task.Run(async () =>
            {
                if (externalCancellation) external.Cancel();
                await client.StopAsync();
            });
            try
            {
                await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                // Give the canceled I/O/handler continuations time to reach cleanup while the callback
                // is deliberately still running. Completion must join it, rather than lose its error.
                await Task.WhenAny(session, Task.Delay(30));
                Assert.That(session.IsCompleted, Is.False);
            }
            finally { callbackRelease.Set(); }
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            var failure = await Failure(session);
            Assert.That(failure, Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)failure).Flatten().InnerExceptions.Single().Message,
                Is.EqualTo("Cancellation callback failed"));
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
            Assert.That(client.LastAppliedSequence, Is.Zero);
        }

        [Test]
        public async Task MessageSizeAndPushQueueLimitsFailExplicitlyAndReleaseResources()
        {
            var transport = new FakeTransport();
            var entered = Signal();
            var release = Signal();
            await using var client = Client(new Factory(transport), options: new RealtimeOptions(maxMessageBytes: 32, maxPendingPushes: 1));
            Task session = client.RunSessionAsync(0, async (push, token) => { entered.SetResult(true); await release.Task; });
            await Until(() => client.IsConnected);
            Assert.That(await Failure(client.SendAsync(new byte[20])), Is.TypeOf<RealtimeProtocolException>());
            Assert.That(transport.Sent, Is.Empty);
            transport.Push(1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            transport.Push(2);
            transport.Push(3);
            Assert.That(await Failure(session), Is.TypeOf<RealtimeProtocolException>());
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
            release.SetResult(true);
        }

        private static RealtimeClient Client(Factory factory, Recovery recovery = null, RealtimeOptions options = null) =>
            new RealtimeClient(Endpoint, factory, Codec, recovery ?? new Recovery(), options ?? new RealtimeOptions(
                initialReconnectDelay: TimeSpan.Zero, heartbeatInterval: TimeSpan.FromSeconds(30)));
        private static RealtimeOptions FastHeartbeat(int maxReconnectAttempts) => new RealtimeOptions(
            maxReconnectAttempts: maxReconnectAttempts, initialReconnectDelay: TimeSpan.FromMilliseconds(1),
            heartbeatInterval: TimeSpan.FromMilliseconds(15), heartbeatTimeout: TimeSpan.FromMilliseconds(150));
        private static PushRecoveryBatch Batch(long through, params long[] sequences) =>
            new PushRecoveryBatch(through, sequences.Select(n => new RealtimePush(n, new[] { (byte)n })).ToArray());
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (!condition()) await Task.Delay(5, timeout.Token);
        }
        private static async Task<Exception> Failure(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (Exception error) { return error; }
            Assert.Fail("The operation unexpectedly succeeded.");
            return null;
        }

        private sealed class Recovery : IPushRecovery
        {
            internal readonly ConcurrentQueue<long> Cursors = new ConcurrentQueue<long>();
            internal Func<long, int, CancellationToken, Task<PushRecoveryBatch>> OnRecover;
            private int calls;
            internal Recovery(Func<long, int, PushRecoveryBatch> handler = null)
            { OnRecover = (cursor, call, token) => Task.FromResult(handler == null ? Batch(cursor) : handler(cursor, call)); }
            public Task<PushRecoveryBatch> RecoverAsync(long afterSequence, CancellationToken cancellationToken)
            {
                Cursors.Enqueue(afterSequence);
                return OnRecover(afterSequence, Interlocked.Increment(ref calls), cancellationToken);
            }
        }

        private sealed class Factory : IWebSocketTransportFactory
        {
            private readonly FakeTransport[] transports;
            private int count;
            internal int Count => Volatile.Read(ref count);
            internal Factory(params FakeTransport[] transports) { this.transports = transports; }
            public IWebSocketTransport Create(int maxMessageBytes)
            {
                int index = Interlocked.Increment(ref count) - 1;
                if (index >= transports.Length) throw new InvalidOperationException("Unexpected reconnect.");
                return transports[index];
            }
        }

        private sealed class FakeTransport : IWebSocketTransport
        {
            private readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
            internal readonly ConcurrentQueue<RealtimeMessage> Sent = new ConcurrentQueue<RealtimeMessage>();
            internal Func<CancellationToken, Task> OnConnect = token => Task.CompletedTask;
            internal Func<RealtimeMessage, Task> OnSend = message => Task.CompletedTask;
            internal Func<CancellationToken, Task> OnClose = token => Task.CompletedTask;
            internal Func<CancellationToken, Task<byte[]>> OnReceive;
            internal bool FailClose;
            internal int DisposeCount;
            internal int CloseCount;
            internal int ReceiveCount;
            internal int MaxConcurrentSends;
            private int activeSends;
            public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) => OnConnect(cancellationToken);
            public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref activeSends);
                MaxConcurrentSends = Math.Max(MaxConcurrentSends, active);
                try
                {
                    var message = Codec.Decode(bytes);
                    Sent.Enqueue(message);
                    await OnSend(message);
                }
                finally { Interlocked.Decrement(ref activeSends); }
            }
            public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref ReceiveCount);
                return OnReceive == null ? incoming.Reader.ReadAsync(cancellationToken).AsTask() : OnReceive(cancellationToken);
            }
            internal void Push(long sequence) => Incoming(new RealtimeMessage(RealtimeMessageKind.Push, sequence: sequence));
            internal void Incoming(RealtimeMessage message) => incoming.Writer.TryWrite(Codec.Encode(message));
            public Task CloseAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CloseCount);
                return FailClose ? Task.FromException(new IOException("Close failed")) : OnClose(cancellationToken);
            }
            public ValueTask DisposeAsync() { Interlocked.Increment(ref DisposeCount); return default; }
        }
    }
}
