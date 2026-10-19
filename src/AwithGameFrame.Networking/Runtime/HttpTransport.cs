using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking
{
    /// <summary>An HTTP request snapshot. Transport implementations must not retry it implicitly.</summary>
    public sealed class TransportRequest
    {
        public Uri Uri { get; }
        public HttpMethod Method { get; }
        public byte[] Body { get; }
        public string ContentType { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }

        public TransportRequest(Uri uri, HttpMethod method, byte[] body = null, string contentType = null,
            IReadOnlyDictionary<string, string> headers = null)
        {
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("An absolute HTTP or HTTPS URI is required.", nameof(uri));
            Uri = uri;
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Body = body == null ? null : (byte[])body.Clone();
            ContentType = contentType;
            Headers = CopyHeaders(headers);
        }

        internal static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string> headers)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
                foreach (var pair in headers)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                        throw new ArgumentException("HTTP header names and values cannot be null or empty.", nameof(headers));
                    snapshot.Add(pair.Key, pair.Value);
                }
            return new ReadOnlyDictionary<string, string>(snapshot);
        }
    }

    public sealed class TransportResponse
    {
        public int StatusCode { get; }
        public byte[] Body { get; }
        public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode <= 299;

        public TransportResponse(int statusCode, byte[] body)
        {
            if (statusCode < 100 || statusCode > 599) throw new ArgumentOutOfRangeException(nameof(statusCode));
            StatusCode = statusCode;
            Body = body ?? Array.Empty<byte>();
        }
    }

    public interface IHttpTransport
    {
        /// <summary>Send once. Cancellation interrupts waiting; it does not prove that a server operation was undone.</summary>
        Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Owns its HttpClient. Inject a handler for platform integration or deterministic HTTP tests.</summary>
    public sealed class HttpClientTransport : IHttpTransport, IDisposable
    {
        private readonly HttpClient client;

        public HttpClientTransport() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

        public HttpClientTransport(HttpMessageHandler handler, bool disposeHandler = true)
        {
            client = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler)), disposeHandler);
            // ProtocolClient owns the timeout so its cause cannot be confused with caller cancellation.
            client.Timeout = Timeout.InfiniteTimeSpan;
        }

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            using (var message = new HttpRequestMessage(request.Method, request.Uri))
            {
                if (request.Body != null)
                {
                    message.Content = new ByteArrayContent(request.Body);
                    if (request.ContentType != null) message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
                }
                foreach (var header in request.Headers) message.Headers.Add(header.Key, header.Value);
                using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (response.Content == null) return new TransportResponse((int)response.StatusCode, Array.Empty<byte>());
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        return new TransportResponse((int)response.StatusCode, output.ToArray());
                    }
                }
            }
        }

        public void Dispose() => client.Dispose();
    }
}
