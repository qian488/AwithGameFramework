using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking.Realtime
{
    /// <summary>Session-owned realtime connection with explicit push recovery and no business-send replay.</summary>
    public sealed class RealtimeClient : IAsyncDisposable
    {
        private readonly object sync = new object();
        private readonly object codecSync = new object();
        private readonly Uri uri;
        private readonly IWebSocketTransportFactory factory;
        private readonly IRealtimeCodec codec;
        private readonly IPushRecovery recovery;
        private readonly RealtimeOptions options;
        private readonly HashSet<Session> sessions = new HashSet<Session>();
        private Session current;
        private long epoch;
        private long lastAppliedSequence;
        private bool disposed;

        public long SessionEpoch { get { lock (sync) return epoch; } }
        public long LastAppliedSequence { get { lock (sync) return lastAppliedSequence; } }
        public bool IsConnected { get { lock (sync) return current?.Connection != null && !current.Cancellation.IsCancellationRequested; } }

        public RealtimeClient(Uri uri, IWebSocketTransportFactory factory, IRealtimeCodec codec,
            IPushRecovery recovery, RealtimeOptions options = null)
        {
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != "ws" && uri.Scheme != "wss") || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("An absolute WS or WSS URI without a fragment is required.", nameof(uri));
            this.uri = uri;
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            this.options = options ?? new RealtimeOptions();
        }

        /// <summary>Start a session, invalidating the previous one immediately. The task represents its full lifetime.</summary>
        public Task RunSessionAsync(long lastAppliedSequence, Func<RealtimePush, CancellationToken, Task> applyPushAsync,
            IReadOnlyDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            if (lastAppliedSequence < 0) throw new ArgumentOutOfRangeException(nameof(lastAppliedSequence));
            if (applyPushAsync == null) throw new ArgumentNullException(nameof(applyPushAsync));
            var snapshot = TransportRequest.CopyHeaders(headers);
            Session previous;
            Session session;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(RealtimeClient));
                previous = current;
                session = new Session(++epoch, lastAppliedSequence, applyPushAsync, snapshot, cancellationToken);
                current = session;
                this.lastAppliedSequence = lastAppliedSequence;
                sessions.Add(session);
            }
            Cancel(previous);
            _ = RunAsync(session);
            return session.Completion.Task;
        }

        /// <summary>Send once on the current connection. Failure or cancellation cannot prove the server did not apply it.</summary>
        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            Session session;
            Connection connection;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(RealtimeClient));
                session = current;
                connection = session?.Connection;
                if (connection == null || session.Cancellation.IsCancellationRequested)
                    throw new RealtimeDisconnectedException("There is no active realtime connection.");
            }
            // Encode before entering the transport: local validation failure must not trigger reconnect.
            byte[] message = Encode(new RealtimeMessage(RealtimeMessageKind.Business, payload));
            await SendMessageAsync(session, connection, message, cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            Session[] pending;
            lock (sync)
            {
                if (current != null) ++epoch;
                current = null;
                pending = new Session[sessions.Count];
                sessions.CopyTo(pending);
            }
            foreach (var session in pending) Cancel(session);
            foreach (var session in pending)
                try { await session.Completion.Task.ConfigureAwait(false); }
                catch (Exception) { /* RunSessionAsync retains the session outcome; Stop only joins cleanup. */ }
        }

        public async ValueTask DisposeAsync()
        {
            lock (sync) disposed = true;
            await StopAsync().ConfigureAwait(false);
        }

        private async Task RunAsync(Session session)
        {
            Exception failure = null;
            try
            {
                int retries = 0;
                while (true)
                {
                    CheckCurrent(session);
                    try { await RunAttemptAsync(session).ConfigureAwait(false); }
                    catch (Exception error) when (IsTransient(error) && !session.Token.IsCancellationRequested)
                    {
                        CheckCurrent(session);
                        if (retries >= options.MaxReconnectAttempts)
                            throw new RealtimeDisconnectedException("The realtime reconnect budget was exhausted.", error);
                        retries++;
                        await Task.Delay(options.GetReconnectDelay(retries), session.Token).ConfigureAwait(false);
                        continue;
                    }
                    break;
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                // Invalidate delivery before signaling Completion, including uncooperative late user tasks.
                bool canceled = session.Token.IsCancellationRequested;
                Cancel(session);
                await session.WaitForCancellationAsync().ConfigureAwait(false);
                if (session.CancellationError != null)
                    failure = failure == null || failure is OperationCanceledException ? session.CancellationError :
                        new AggregateException(failure, session.CancellationError);
                lock (sync)
                {
                    session.Connection = null;
                    sessions.Remove(session);
                }
                session.ExternalCancellation.Dispose();
                session.Cancellation.Dispose();
                if (failure is OperationCanceledException && canceled)
                    session.Completion.TrySetCanceled(session.Token);
                else if (failure != null) session.Completion.TrySetException(failure);
                else session.Completion.TrySetResult(true);
            }
        }

        private async Task RunAttemptAsync(Session session)
        {
            var transport = factory.Create(options.MaxMessageBytes) ?? throw new InvalidOperationException("The transport factory returned null.");
            var connection = new Connection(transport, session);
            Exception attemptError = null;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
                {
                    timeout.CancelAfter(options.ConnectTimeout);
                    try { await WaitAsync(transport.ConnectAsync(uri, session.Headers, timeout.Token), timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!session.Token.IsCancellationRequested && timeout.IsCancellationRequested)
                    { throw new TimeoutException("The realtime connection timed out."); }
                }
                CheckCurrent(session);
                // Recovery happens on initial connect as well: the supplied cursor can belong to an earlier process.
                await RecoverAsync(session, connection, 0).ConfigureAwait(false);
                lock (sync)
                {
                    CheckCurrent(session);
                    session.Connection = connection;
                }
                Task receiving = ReceiveAsync(session, connection);
                Task applying = ProcessPushesAsync(session, connection);
                Task heartbeat = HeartbeatAsync(session, connection);
                Task first = await Task.WhenAny(receiving, applying, heartbeat, connection.Failure.Task).ConfigureAwait(false);
                // Send failure cancels receive; preserve the actual failure rather than its cancellation symptom.
                if (connection.Failure.Task.IsFaulted) first = connection.Failure.Task;
                Observe(receiving);
                Observe(applying);
                Observe(heartbeat);
                await first.ConfigureAwait(false);
            }
            catch (Exception error) { attemptError = error; throw; }
            finally
            {
                lock (sync) if (session.Connection == connection) session.Connection = null;
                connection.Cancel();
                await session.WaitForCancellationAsync().ConfigureAwait(false);
                // CloseOutput avoids a second concurrent receive. Always dispose, even if close itself fails.
                try
                {
                    using (var close = new CancellationTokenSource(options.CloseTimeout))
                        await WaitAsync(transport.CloseAsync(close.Token), close.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
                finally
                {
                    try { await transport.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception) when (attemptError != null) { }
                    finally { connection.Cancellation.Dispose(); }
                }
            }
        }

        private async Task ReceiveAsync(Session session, Connection connection)
        {
            while (true)
            {
                byte[] bytes = await WaitAsync(connection.Transport.ReceiveAsync(connection.Token), connection.Token).ConfigureAwait(false);
                CheckCurrent(session);
                if (bytes == null) throw new RealtimeDisconnectedException("The peer closed the realtime connection.");
                if (bytes.Length > options.MaxMessageBytes) throw new RealtimeProtocolException("The incoming message exceeds the size limit.");
                RealtimeMessage message;
                try { lock (codecSync) message = codec.Decode(bytes) ?? throw new RealtimeProtocolException("The codec returned no message."); }
                catch (RealtimeProtocolException) { throw; }
                catch (Exception error) { throw new RealtimeProtocolException("The realtime message could not be decoded.", error); }
                switch (message.Kind)
                {
                    case RealtimeMessageKind.Ping:
                        await SendMessageAsync(session, connection, Encode(new RealtimeMessage(RealtimeMessageKind.Pong,
                            heartbeatId: message.HeartbeatId)), connection.Token).ConfigureAwait(false);
                        break;
                    case RealtimeMessageKind.Pong:
                        connection.AcceptPong(message.HeartbeatId);
                        break;
                    case RealtimeMessageKind.Push:
                        connection.EnqueuePush(new RealtimePush(message.Sequence, message.Payload), options.MaxPendingPushes);
                        break;
                    default:
                        throw new RealtimeProtocolException("Server business messages must use sequenced Push messages.");
                }
            }
        }

        private async Task ProcessPushesAsync(Session session, Connection connection)
        {
            while (true)
            {
                await connection.PushReady.WaitAsync(connection.Token).ConfigureAwait(false);
                RealtimePush push = connection.DequeuePush();
                await ApplyIncomingAsync(session, connection, push).ConfigureAwait(false);
            }
        }

        private async Task HeartbeatAsync(Session session, Connection connection)
        {
            long id = 0;
            while (true)
            {
                await Task.Delay(options.HeartbeatInterval, connection.Token).ConfigureAwait(false);
                CheckCurrent(session);
                Task pong = connection.ExpectPong(++id);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
                {
                    timeout.CancelAfter(options.HeartbeatTimeout);
                    try
                    {
                        await SendMessageAsync(session, connection, Encode(new RealtimeMessage(RealtimeMessageKind.Ping,
                            heartbeatId: id)), timeout.Token).ConfigureAwait(false);
                        await WaitAsync(pong, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!connection.Token.IsCancellationRequested && timeout.IsCancellationRequested)
                    { throw new TimeoutException("The application heartbeat was not acknowledged before its deadline."); }
                }
            }
        }

        private async Task SendMessageAsync(Session session, Connection connection, byte[] bytes, CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Token))
            {
                await connection.SendGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    CheckCurrent(session);
                    linked.Token.ThrowIfCancellationRequested();
                    try { await WaitAsync(connection.Transport.SendAsync(bytes, linked.Token), linked.Token).ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        // Delivery can be ambiguous. Mark this connection failed, without replaying the message.
                        connection.Fail(error is RealtimeProtocolException ? error :
                            new RealtimeDisconnectedException("A realtime send failed; delivery is unknown.", error));
                        throw;
                    }
                    CheckCurrent(session);
                }
                finally { connection.SendGate.Release(); }
            }
        }

        private async Task ApplyIncomingAsync(Session session, Connection connection, RealtimePush push)
        {
            long cursor;
            lock (sync) { CheckCurrent(session); cursor = session.LastApplied; }
            if (push.Sequence <= cursor) return;
            if (cursor == long.MaxValue || push.Sequence != cursor + 1)
                await RecoverAsync(session, connection, push.Sequence - 1).ConfigureAwait(false);
            lock (sync) { CheckCurrent(session); cursor = session.LastApplied; }
            if (push.Sequence <= cursor) return;
            if (push.Sequence != cursor + 1) throw new PushRecoveryException("Recovery did not close the push sequence gap.");
            await ApplyAsync(session, connection, push).ConfigureAwait(false);
        }

        private async Task RecoverAsync(Session session, Connection connection, long minimumThrough)
        {
            long cursor;
            Task<PushRecoveryBatch> operation;
            try
            {
                lock (sync)
                {
                    CheckCurrent(session);
                    cursor = session.LastApplied;
                    operation = recovery.RecoverAsync(cursor, connection.Token);
                }
                var batch = await WaitAsync(operation, connection.Token).ConfigureAwait(false);
                CheckCurrent(session);
                if (batch == null || batch.ThroughSequence < cursor || batch.ThroughSequence < minimumThrough ||
                    batch.ThroughSequence - cursor != batch.Pushes.Count)
                    throw new PushRecoveryException("Recovery must return every push through its authoritative high watermark.");
                for (int i = 0; i < batch.Pushes.Count; i++)
                    if (batch.Pushes[i].Sequence != cursor + i + 1)
                        throw new PushRecoveryException("Recovered push sequences must be contiguous and ordered.");
                foreach (var push in batch.Pushes) await ApplyAsync(session, connection, push).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (connection.Token.IsCancellationRequested) { throw; }
            catch (PushRecoveryException) { throw; }
            catch (PushApplicationException) { throw; }
            catch (Exception error) { throw new PushRecoveryException("Push recovery failed; the session cannot safely continue.", error); }
        }

        private async Task ApplyAsync(Session session, Connection connection, RealtimePush push)
        {
            Task operation;
            try
            {
                lock (sync)
                {
                    CheckCurrent(session);
                    connection.Token.ThrowIfCancellationRequested();
                    operation = session.ApplyPush(push, connection.Token);
                }
                await WaitAsync(operation, connection.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (connection.Token.IsCancellationRequested) { throw; }
            catch (Exception error) { throw new PushApplicationException(push.Sequence, error); }
            lock (sync)
            {
                CheckCurrent(session);
                connection.Token.ThrowIfCancellationRequested();
                session.LastApplied = push.Sequence;
                lastAppliedSequence = push.Sequence;
            }
        }

        private byte[] Encode(RealtimeMessage message)
        {
            byte[] bytes;
            try { lock (codecSync) bytes = codec.Encode(message); }
            catch (Exception error) { throw new RealtimeProtocolException("The realtime message could not be encoded.", error); }
            if (bytes == null || bytes.Length > options.MaxMessageBytes)
                throw new RealtimeProtocolException("The encoded message is null or exceeds the size limit.");
            return bytes;
        }

        private void CheckCurrent(Session session)
        {
            lock (sync)
            {
                if (disposed || current != session || epoch != session.Epoch) throw new OperationCanceledException(session.Token);
                session.Token.ThrowIfCancellationRequested();
            }
        }

        private static bool IsTransient(Exception error) => error is IOException || error is WebSocketException ||
            error is TimeoutException || error is ObjectDisposedException;

        private static void Cancel(Session session)
        {
            if (session == null) return;
            session.CancelSource(session.Cancellation);
        }

        private static async Task WaitAsync(Task operation, CancellationToken cancellationToken)
        {
            if (operation == null) throw new InvalidOperationException("An asynchronous provider returned no task.");
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false) != operation)
                {
                    Observe(operation);
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            await operation.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task<T> WaitAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            await WaitAsync((Task)operation, cancellationToken).ConfigureAwait(false);
            return await operation.ConfigureAwait(false);
        }

        private static async void Observe(Task operation) { try { await operation.ConfigureAwait(false); } catch (Exception) { } }

        private sealed class Session
        {
            internal readonly long Epoch;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationTokenRegistration ExternalCancellation;
            internal readonly CancellationToken Token;
            internal readonly Func<RealtimePush, CancellationToken, Task> ApplyPush;
            internal readonly IReadOnlyDictionary<string, string> Headers;
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal long LastApplied;
            internal Connection Connection;
            internal Exception CancellationError;
            private readonly object cancellationSync = new object();
            private int cancelOperations;
            private TaskCompletionSource<bool> cancellationFinished;
            internal Session(long epoch, long cursor, Func<RealtimePush, CancellationToken, Task> applyPush,
                IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
            {
                Epoch = epoch;
                LastApplied = cursor;
                ApplyPush = applyPush;
                Headers = headers;
                Cancellation = new CancellationTokenSource();
                Token = Cancellation.Token;
                ExternalCancellation = cancellationToken.Register(() => Cancel(this));
            }
            internal void CancelSource(CancellationTokenSource source)
            {
                lock (cancellationSync)
                {
                    if (cancelOperations++ == 0)
                        cancellationFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                // Cancel wakes I/O waiters before it finishes invoking callbacks. Track its whole
                // synchronous invocation, including exception recording, without holding a lock
                // across arbitrary user code. Cleanup asynchronously joins every such invocation.
                try { source.Cancel(); }
                catch (ObjectDisposedException) { }
                catch (AggregateException error) { Interlocked.CompareExchange(ref CancellationError, error, null); }
                finally
                {
                    lock (cancellationSync)
                        if (--cancelOperations == 0) cancellationFinished.TrySetResult(true);
                }
            }
            internal Task WaitForCancellationAsync()
            {
                lock (cancellationSync) return cancellationFinished?.Task ?? Task.CompletedTask;
            }
        }

        private sealed class Connection
        {
            internal readonly IWebSocketTransport Transport;
            internal readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken Token;
            internal readonly TaskCompletionSource<bool> Failure = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly SemaphoreSlim PushReady = new SemaphoreSlim(0);
            private readonly Queue<RealtimePush> pushes = new Queue<RealtimePush>();
            private readonly object heartbeatSync = new object();
            private long heartbeatId;
            private TaskCompletionSource<bool> pong;
            private readonly Session session;
            internal Connection(IWebSocketTransport transport, Session session)
            {
                Transport = transport;
                this.session = session;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                Token = Cancellation.Token;
                Observe(Failure.Task);
            }
            internal Task ExpectPong(long id)
            {
                lock (heartbeatSync)
                {
                    heartbeatId = id;
                    pong = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    return pong.Task;
                }
            }
            internal void AcceptPong(long id) { lock (heartbeatSync) if (id == heartbeatId) pong?.TrySetResult(true); }
            internal void EnqueuePush(RealtimePush push, int capacity)
            {
                lock (pushes)
                {
                    if (pushes.Count >= capacity) throw new RealtimeProtocolException("The pending push queue exceeded its limit.");
                    pushes.Enqueue(push);
                }
                PushReady.Release();
            }
            internal RealtimePush DequeuePush() { lock (pushes) return pushes.Dequeue(); }
            internal void Fail(Exception error) { Failure.TrySetException(error); Cancel(); }
            internal void Cancel()
            {
                session.CancelSource(Cancellation);
            }
        }
    }
}
