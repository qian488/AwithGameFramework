using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class HttpClientTransportTests
    {
        private static readonly TransportRequest Request = new TransportRequest(new Uri("https://example.test/data"), HttpMethod.Get);

        [Test]
        public void ExistingConstructors_ShouldKeepDefaultLimitAndHandlerOwnership()
        {
            using var defaultTransport = new HttpClientTransport();
            using var handler = new Handler(() => new HttpResponseMessage(HttpStatusCode.OK));
            var injected = new HttpClientTransport(handler, disposeHandler: false);

            Assert.That(defaultTransport.MaxResponseBodyBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(injected.MaxResponseBodyBytes, Is.EqualTo(defaultTransport.MaxResponseBodyBytes));
            injected.Dispose();
            Assert.That(handler.Disposed, Is.False);
        }

        [Test]
        public void NegativeResponseLimit_ShouldBeRejected()
        {
            using var handler = new Handler(() => new HttpResponseMessage(HttpStatusCode.OK));

            Assert.Throws<ArgumentOutOfRangeException>(() => new HttpClientTransport(handler, maxResponseBodyBytes: -1));
            Assert.That(handler.Disposed, Is.False);
        }

        [TestCase(9L)]
        [TestCase(long.MaxValue)]
        public void OversizedContentLength_ShouldRejectBeforeReadingAndDisposeResponse(long contentLength)
        {
            var stream = new FragmentedReadStream(new byte[1]);
            using var transport = new HttpClientTransport(new Handler(() => Response(stream, contentLength)), maxResponseBodyBytes: 8);

            var failure = Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(Request, CancellationToken.None));

            Assert.That(failure.Message, Does.Contain("limit of 8 bytes"));
            Assert.That(stream.ReadCalls, Is.Zero);
            Assert.That(stream.Disposed, Is.True);
        }

        [TestCase(9, -1, HttpStatusCode.OK)]
        [TestCase(32, -1, HttpStatusCode.OK)]
        [TestCase(32, 2, HttpStatusCode.OK)]
        [TestCase(32, -1, HttpStatusCode.InternalServerError)]
        [TestCase(32, 2, HttpStatusCode.InternalServerError)]
        public void ActualBodyLimit_ShouldHandleUnknownOrFalseLengthsAndErrorStatus(int bodyLength, int contentLength, HttpStatusCode status)
        {
            var stream = new FragmentedReadStream(new byte[bodyLength]);
            using var transport = new HttpClientTransport(new Handler(() => Response(stream, contentLength < 0 ? null : contentLength, status)),
                maxResponseBodyBytes: 8);

            var failure = Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(Request, CancellationToken.None));

            Assert.That(failure.Message, Does.Contain("limit of 8 bytes"));
            Assert.That(stream.BytesRead, Is.EqualTo(9), "Stop reading as soon as one byte proves that the limit was exceeded.");
            Assert.That(stream.Disposed, Is.True);
        }

        [TestCase(0, -1)]
        [TestCase(15, -1)]
        [TestCase(16, -1)]
        [TestCase(16, 16)]
        public async Task BodyAtOrBelowLimit_ShouldPreserveBytesAndStatus(int bodyLength, int contentLength)
        {
            var body = new byte[bodyLength];
            for (int index = 0; index < body.Length; index++) body[index] = (byte)(index + 1);
            var stream = new FragmentedReadStream(body);
            using var transport = new HttpClientTransport(new Handler(() => Response(stream, contentLength < 0 ? null : contentLength,
                HttpStatusCode.BadRequest)), maxResponseBodyBytes: 16);

            var response = await transport.SendAsync(Request, CancellationToken.None);

            Assert.That(response.Body, Is.EqualTo(body));
            Assert.That(response.StatusCode, Is.EqualTo(400));
            Assert.That(stream.Disposed, Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task ZeroLimit_ShouldAllowOnlyEmptyBody(int bodyLength)
        {
            var stream = new FragmentedReadStream(new byte[bodyLength]);
            using var transport = new HttpClientTransport(new Handler(() => Response(stream, null)), maxResponseBodyBytes: 0);

            if (bodyLength == 0)
                Assert.That((await transport.SendAsync(Request, CancellationToken.None)).Body, Is.Empty);
            else
                Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(Request, CancellationToken.None));
            Assert.That(stream.Disposed, Is.True);
        }

        [Test]
        public async Task ProtocolClient_ShouldReportOversizedErrorBodyAsNetworkError()
        {
            var stream = new FragmentedReadStream(new byte[32]);
            using var transport = new HttpClientTransport(new Handler(() => Response(stream, null, HttpStatusCode.InternalServerError)),
                maxResponseBodyBytes: 8);
            using var client = new ProtocolClient(new Uri("https://example.test/"), transport, new DataContractJsonProtocolCodec());
            var endpoint = new ProtocolEndpoint<EmptyRequest, EmptyRequest>(HttpMethod.Get, "data", hasRequestBody: false);

            var result = await client.SendAsync(endpoint, new EmptyRequest());

            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.NetworkError));
            Assert.That(result.Message, Does.Contain("limit of 8 bytes"));
            Assert.That(stream.Disposed, Is.True);
        }

        [Test]
        public async Task CancellationDuringBodyRead_ShouldKeepCancellationSemanticsAndDisposeResponse()
        {
            var stream = new BlockingReadStream();
            using var transport = new HttpClientTransport(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            }), maxResponseBodyBytes: 8);
            using var cancellation = new CancellationTokenSource();
            var operation = transport.SendAsync(Request, cancellation.Token);
            await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            cancellation.Cancel();
            var failure = Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.That(failure.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(stream.Disposed, Is.True);
        }

        private static HttpResponseMessage Response(Stream stream, long? contentLength, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentLength = contentLength;
            return response;
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> response;
            internal bool Disposed;

            internal Handler(Func<HttpResponseMessage> response) { this.response = response; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(response());

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class FragmentedReadStream : Stream
        {
            private readonly byte[] body;
            internal int BytesRead;
            internal int ReadCalls;
            internal bool Disposed;

            internal FragmentedReadStream(byte[] body) { this.body = body; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCalls++;
                int read = Math.Min(3, Math.Min(count, body.Length - BytesRead));
                Array.Copy(body, BytesRead, buffer, offset, read);
                BytesRead += read;
                return read;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Read(buffer, offset, count));
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class BlockingReadStream : Stream
        {
            internal readonly TaskCompletionSource<bool> ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Disposed;

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ReadStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
