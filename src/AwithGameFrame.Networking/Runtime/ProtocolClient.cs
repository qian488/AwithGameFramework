using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking
{
    /// <summary>Session-aware, single-attempt protocol calls. The caller owns the transport.</summary>
    public sealed class ProtocolClient : IProtocolClient, IDisposable
    {
        private readonly object sync = new object();
        private readonly Uri baseUri;
        private readonly IHttpTransport transport;
        private readonly IProtocolCodec codec;
        private readonly TimeSpan defaultTimeout;
        private readonly HashSet<PendingRequest> pending = new HashSet<PendingRequest>();
        private IReadOnlyDictionary<string, string> headers = TransportRequest.CopyHeaders(null);
        private long epoch;
        private bool disposed;

        public long SessionEpoch { get { lock (sync) return epoch; } }

        public ProtocolClient(Uri baseUri, IHttpTransport transport, IProtocolCodec codec, TimeSpan? defaultTimeout = null)
        {
            if (baseUri == null || !baseUri.IsAbsoluteUri || (baseUri.Scheme != "http" && baseUri.Scheme != "https") ||
                !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
                throw new ArgumentException("An absolute HTTP base URI without a query or fragment is required.", nameof(baseUri));
            this.baseUri = baseUri;
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.defaultTimeout = ValidateTimeout(defaultTimeout ?? TimeSpan.FromSeconds(15));
        }

        /// <summary>Replace session headers and invalidate all pending calls, including handlers that ignore cancellation.</summary>
        public long BeginSession(IReadOnlyDictionary<string, string> headers = null)
        {
            var snapshot = TransportRequest.CopyHeaders(headers);
            PendingRequest[] previous;
            long newEpoch;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ProtocolClient));
                newEpoch = ++epoch;
                this.headers = snapshot;
                previous = new PendingRequest[pending.Count];
                pending.CopyTo(previous);
            }
            Cancel(previous);
            return newEpoch;
        }

        public bool IsCurrentEpoch(long sessionEpoch) { lock (sync) return !disposed && epoch == sessionEpoch; }

        public async Task<NetworkResult<TResponse>> SendAsync<TRequest, TResponse>(ProtocolEndpoint<TRequest, TResponse> endpoint,
            TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var callTimeout = ValidateTimeout(timeout ?? defaultTimeout);
            PendingRequest call;
            IReadOnlyDictionary<string, string> callHeaders;
            lock (sync)
            {
                if (disposed) return new NetworkResult<TResponse>(NetworkFailure.ClientDisposed, epoch);
                call = new PendingRequest(epoch);
                pending.Add(call);
                callHeaders = headers;
            }
            using (var timer = new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token, call.Cancellation.Token))
            {
                if (callTimeout != Timeout.InfiniteTimeSpan) timer.CancelAfter(callTimeout);
                try
                {
                    var interrupted = Interruption<TResponse>(call, cancellationToken, timer.Token);
                    if (interrupted != null) return interrupted;
                    byte[] body = null;
                    if (endpoint.HasRequestBody)
                    {
                        try
                        {
                            body = codec.Encode(request);
                            if (body == null) throw new InvalidOperationException("The codec returned no request body.");
                        }
                        catch (Exception error)
                        {
                            return Interruption<TResponse>(call, cancellationToken, timer.Token) ??
                                new NetworkResult<TResponse>(NetworkFailure.EncodingError, call.Epoch, message: error.Message);
                        }
                    }
                    interrupted = Interruption<TResponse>(call, cancellationToken, timer.Token);
                    if (interrupted != null) return interrupted;
                    var message = new TransportRequest(new Uri(baseUri, endpoint.RelativePath), endpoint.Method,
                        body, endpoint.HasRequestBody ? codec.ContentType : null, callHeaders);
                    TransportResponse response;
                    try
                    {
                        response = await WaitForTransportAsync(transport.SendAsync(message, linked.Token), linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is OperationCanceledException || error is HttpRequestException || error is IOException || error is ObjectDisposedException)
                    {
                        return Interruption<TResponse>(call, cancellationToken, timer.Token) ??
                            new NetworkResult<TResponse>(NetworkFailure.NetworkError, call.Epoch, message: error.Message);
                    }
                    interrupted = Interruption<TResponse>(call, cancellationToken, timer.Token);
                    if (interrupted != null) return interrupted;
                    if (response == null) return new NetworkResult<TResponse>(NetworkFailure.InvalidResponse, call.Epoch, message: "The transport returned no response.");
                    if (!response.IsSuccessStatusCode)
                        return FinalizeResult(call, cancellationToken, timer.Token,
                            new NetworkResult<TResponse>(NetworkFailure.HttpError, call.Epoch, response.StatusCode, message: "HTTP request failed."));
                    ProtocolEnvelope<TResponse> envelope;
                    try
                    {
                        envelope = codec.Decode<TResponse>(response.Body);
                        if (envelope == null) throw new InvalidOperationException("The codec returned no response envelope.");
                    }
                    catch (Exception error)
                    {
                        return FinalizeResult(call, cancellationToken, timer.Token,
                            new NetworkResult<TResponse>(NetworkFailure.InvalidResponse, call.Epoch, response.StatusCode, message: error.Message));
                    }
                    return FinalizeResult(call, cancellationToken, timer.Token,
                        new NetworkResult<TResponse>(NetworkFailure.None, call.Epoch, response.StatusCode, envelope.Status,
                            envelope.Message, envelope.Status == 0 ? envelope.Data : default));
                }
                finally
                {
                    lock (sync) pending.Remove(call);
                    call.Cancellation.Dispose();
                }
            }
        }

        private NetworkResult<T> FinalizeResult<T>(PendingRequest call, CancellationToken callerToken, CancellationToken timeoutToken, NetworkResult<T> result)
        {
            lock (sync) return Interruption<T>(call, callerToken, timeoutToken) ?? result;
        }

        private NetworkResult<T> Interruption<T>(PendingRequest call, CancellationToken callerToken, CancellationToken timeoutToken)
        {
            lock (sync)
            {
                if (disposed) return new NetworkResult<T>(NetworkFailure.ClientDisposed, call.Epoch);
                if (epoch != call.Epoch) return new NetworkResult<T>(NetworkFailure.SessionChanged, call.Epoch);
                if (callerToken.IsCancellationRequested) return new NetworkResult<T>(NetworkFailure.CallerCancelled, call.Epoch);
                if (timeoutToken.IsCancellationRequested) return new NetworkResult<T>(NetworkFailure.Timeout, call.Epoch);
                return null;
            }
        }

        private static async Task<TransportResponse> WaitForTransportAsync(Task<TransportResponse> operation, CancellationToken cancellationToken)
        {
            if (operation == null) throw new InvalidOperationException("The transport returned no task.");
            var interrupted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => interrupted.TrySetResult(true)))
            {
                if (await Task.WhenAny(operation, interrupted.Task).ConfigureAwait(false) != operation)
                {
                    // Observe eventual errors without keeping a cancelled protocol call blocked on an uncooperative transport.
                    _ = ObserveLateAsync(operation);
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await operation.ConfigureAwait(false);
        }

        private static async Task ObserveLateAsync(Task<TransportResponse> operation)
        {
            try { await operation.ConfigureAwait(false); }
            catch (Exception) { }
        }

        private static TimeSpan ValidateTimeout(TimeSpan timeout)
        {
            if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeout), "Use a positive timeout up to Int32.MaxValue milliseconds or InfiniteTimeSpan.");
            return timeout;
        }

        public void Dispose()
        {
            PendingRequest[] active;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                active = new PendingRequest[pending.Count];
                pending.CopyTo(active);
            }
            Cancel(active);
        }

        private static void Cancel(PendingRequest[] active)
        {
            foreach (var call in active)
            {
                try { call.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        private sealed class PendingRequest
        {
            internal readonly long Epoch;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal PendingRequest(long epoch) { Epoch = epoch; }
        }
    }
}
