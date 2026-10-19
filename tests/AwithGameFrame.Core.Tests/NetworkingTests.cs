using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class NetworkingTests
    {
        private static readonly Uri BaseUri = new Uri("https://game.example/api/");
        private static readonly ProtocolEndpoint<LevelRequest, LevelResponse> LevelEndpoint =
            new ProtocolEndpoint<LevelRequest, LevelResponse>(HttpMethod.Post, "level/play?mode=ranked");

        [Test]
        public async Task HttpRequest_ShouldUseDeclaredRouteMethodHeadersAndTypedBody()
        {
            string body = null;
            var handler = new Handler(async (request, token) =>
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://game.example/api/level/play?mode=ranked")));
                Assert.That(request.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
                Assert.That(request.Headers.Authorization.Parameter, Is.EqualTo("session-one"));
                Assert.That(request.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
                body = await request.Content.ReadAsStringAsync(token);
                return Json(HttpStatusCode.OK, "{\"status\":0,\"data\":{\"score\":42}}");
            });
            using var transport = new HttpClientTransport(handler);
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer session-one" };
            long epoch = client.BeginSession(headers);
            headers["Authorization"] = "Bearer mutated-after-snapshot";
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest { LevelId = 7 });

            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain("\"levelId\":7"));
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Failure, Is.EqualTo(NetworkFailure.None));
                Assert.That(result.HttpStatusCode, Is.EqualTo(200));
                Assert.That(result.BusinessStatus, Is.EqualTo(0));
                Assert.That(result.Data.Score, Is.EqualTo(42));
                Assert.That(result.SessionEpoch, Is.EqualTo(epoch));
                Assert.That(client.IsCurrentEpoch(epoch), Is.True);
                Assert.That(handler.Calls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task BusinessFailure_ShouldRemainDistinctFromSuccessfulHttpTransport()
        {
            using var transport = new HttpClientTransport(new Handler((request, token) =>
                Task.FromResult(Json(HttpStatusCode.OK, "{\"status\":1203,\"message\":\"level locked\",\"data\":{\"score\":99}}"))));
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest());
            Assert.Multiple(() =>
            {
                Assert.That(result.Failure, Is.EqualTo(NetworkFailure.None));
                Assert.That(result.BusinessStatus, Is.EqualTo(1203));
                Assert.That(result.HttpStatusCode, Is.EqualTo(200));
                Assert.That(result.Message, Is.EqualTo("level locked"));
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Data, Is.Null);
            });
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public async Task HttpFailure_ShouldPreserveStatusWithoutDecodingOrRetryingPost(HttpStatusCode status)
        {
            var handler = new Handler((request, token) => Task.FromResult(Json(status, "not a protocol body")));
            using var transport = new HttpClientTransport(handler);
            var codec = new HookCodec();
            using var client = new ProtocolClient(BaseUri, transport, codec);
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest());
            Assert.Multiple(() =>
            {
                Assert.That(result.Failure, Is.EqualTo(NetworkFailure.HttpError));
                Assert.That(result.HttpStatusCode, Is.EqualTo((int)status));
                Assert.That(result.BusinessStatus, Is.Null);
                Assert.That(result.Data, Is.Null);
                Assert.That(codec.DecodeCalls, Is.Zero);
                Assert.That(handler.Calls, Is.EqualTo(1));
            });
        }

        [TestCase("")]
        [TestCase("{\"data\":{\"score\":1}}")]
        [TestCase("{\"status\":0,\"data\":")]
        public async Task EmptyMissingStatusOrMalformedSuccessBody_ShouldNotBecomeSuccessfulDefaultData(string body)
        {
            using var transport = new HttpClientTransport(new Handler((request, token) => Task.FromResult(Json(HttpStatusCode.OK, body))));
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest());
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.InvalidResponse));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.HttpStatusCode, Is.EqualTo(200));
        }

        [Test]
        public async Task Timeout_ShouldCancelHandlerAndRemainDistinctFromCallerCancellation()
        {
            var observed = Signal();
            var handler = new Handler(async (request, token) =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { observed.TrySetResult(true); throw; }
                return Json(HttpStatusCode.OK, "{}");
            });
            using var transport = new HttpClientTransport(handler);
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest(), timeout: TimeSpan.FromMilliseconds(80));
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.Timeout));
            Assert.That(result.HttpStatusCode, Is.Null);
            Assert.That(handler.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task CallerCancellation_ShouldInterruptActiveRequestAndNeverReportTimeout()
        {
            var entered = Signal();
            using var transport = new HttpClientTransport(new Handler(async (request, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return Json(HttpStatusCode.OK, "{}");
            }));
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            using var cancellation = new CancellationTokenSource();
            var call = client.SendAsync(LevelEndpoint, new LevelRequest(), timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellation.Token);
            await entered.Task;
            cancellation.Cancel();
            var result = await call.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.CallerCancelled));
            Assert.That(result.Data, Is.Null);
        }

        [Test]
        public async Task PreCancelledRequest_ShouldNotEncodeOrSend()
        {
            var handler = new Handler((request, token) => Task.FromResult(Json(HttpStatusCode.OK, "{}")));
            using var transport = new HttpClientTransport(handler);
            var codec = new HookCodec();
            using var client = new ProtocolClient(BaseUri, transport, codec);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest(), cancellationToken: cancellation.Token);
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.CallerCancelled));
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(codec.EncodeCalls, Is.Zero);
        }

        [Test]
        public async Task SessionChange_ShouldReturnBeforeUncooperativeOldHandlerAndNeverDecodeItsResponse()
        {
            var entered = Signal();
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler((request, token) =>
            {
                if (request.Headers.Authorization.Parameter == "old")
                {
                    entered.TrySetResult(true);
                    return release.Task; // Deliberately ignores cancellation.
                }
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"status\":0,\"data\":{\"score\":2}}"));
            });
            using var transport = new HttpClientTransport(handler);
            var codec = new HookCodec();
            using var client = new ProtocolClient(BaseUri, transport, codec);
            long oldEpoch = client.BeginSession(new Dictionary<string, string> { ["Authorization"] = "Bearer old" });
            var call = client.SendAsync(LevelEndpoint, new LevelRequest(), timeout: Timeout.InfiniteTimeSpan);
            try
            {
                await entered.Task;
                client.BeginSession(new Dictionary<string, string> { ["Authorization"] = "Bearer new" });
                var oldResult = await call.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(oldResult.Failure, Is.EqualTo(NetworkFailure.SessionChanged));
                Assert.That(oldResult.Data, Is.Null);
                Assert.That(codec.DecodeCalls, Is.Zero);
                Assert.That(client.IsCurrentEpoch(oldEpoch), Is.False);
                var current = await client.SendAsync(LevelEndpoint, new LevelRequest());
                Assert.That(current.IsSuccess, Is.True);
                Assert.That(current.Data.Score, Is.EqualTo(2));
                Assert.That(codec.DecodeCalls, Is.EqualTo(1));
            }
            finally { release.TrySetResult(Json(HttpStatusCode.OK, "{\"status\":0,\"data\":{\"score\":999}}")); }
        }

        [Test]
        public async Task SessionChangeDuringDecode_ShouldDiscardAlreadyDecodedData()
        {
            using var transport = new HttpClientTransport(new Handler((request, token) =>
                Task.FromResult(Json(HttpStatusCode.OK, "{\"status\":0,\"data\":{\"score\":42}}"))));
            var codec = new HookCodec();
            using var client = new ProtocolClient(BaseUri, transport, codec);
            codec.OnDecode = () => client.BeginSession();
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest());
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.SessionChanged));
            Assert.That(result.Data, Is.Null);
            Assert.That(result.BusinessStatus, Is.Null);
        }

        [Test]
        public async Task Timeout_ShouldCoverBodyStreamingAfterHeaders()
        {
            var stream = new BlockingReadStream();
            using var transport = new HttpClientTransport(new Handler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            })));
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest(), timeout: TimeSpan.FromMilliseconds(80));
            await stream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.Timeout));
        }

        [Test]
        public async Task ConnectionFailure_ShouldNotBeMisreportedAsHttpOrBusinessFailure()
        {
            using var transport = new HttpClientTransport(new Handler((request, token) => throw new HttpRequestException("connection refused")));
            using var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var result = await client.SendAsync(LevelEndpoint, new LevelRequest());
            Assert.That(result.Failure, Is.EqualTo(NetworkFailure.NetworkError));
            Assert.That(result.HttpStatusCode, Is.Null);
            Assert.That(result.BusinessStatus, Is.Null);
        }

        [Test]
        public async Task DisposeClient_ShouldCancelPendingCallsWithoutDisposingSharedTransport()
        {
            var entered = Signal();
            int count = 0;
            using var transport = new HttpClientTransport(new Handler(async (request, token) =>
            {
                if (Interlocked.Increment(ref count) == 1)
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                }
                return Json(HttpStatusCode.OK, "{\"status\":0}");
            }));
            var client = new ProtocolClient(BaseUri, transport, new DataContractJsonProtocolCodec());
            var active = client.SendAsync(LevelEndpoint, new LevelRequest());
            await entered.Task;
            client.Dispose();
            Assert.That((await active.WaitAsync(TimeSpan.FromSeconds(2))).Failure, Is.EqualTo(NetworkFailure.ClientDisposed));
            Assert.That((await client.SendAsync(LevelEndpoint, new LevelRequest())).Failure, Is.EqualTo(NetworkFailure.ClientDisposed));
            Assert.That((await transport.SendAsync(new TransportRequest(BaseUri, HttpMethod.Get), CancellationToken.None)).StatusCode, Is.EqualTo(200));
        }

        [Test]
        public async Task GetWithoutBody_ShouldNotInvokeRequestEncoder()
        {
            using var transport = new HttpClientTransport(new Handler((request, token) =>
            {
                Assert.That(request.Content, Is.Null);
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"status\":0,\"data\":{\"score\":1}}"));
            }));
            var codec = new HookCodec();
            using var client = new ProtocolClient(BaseUri, transport, codec);
            var endpoint = new ProtocolEndpoint<EmptyRequest, LevelResponse>(HttpMethod.Get, "level/current", hasRequestBody: false);
            Assert.That((await client.SendAsync(endpoint, new EmptyRequest())).IsSuccess, Is.True);
            Assert.That(codec.EncodeCalls, Is.Zero);
        }

        [TestCase("https://other.example/route")]
        [TestCase("//other.example/route")]
        [TestCase(" //other.example/route")]
        [TestCase("\\\\other.example\\route")]
        [TestCase("route#fragment")]
        public void AbsoluteOrAmbiguousProtocolRoutes_ShouldBeRejected(string route)
        {
            Assert.Throws<ArgumentException>(() => new ProtocolEndpoint<LevelRequest, LevelResponse>(HttpMethod.Post, route));
        }

        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static HttpResponseMessage Json(HttpStatusCode code, string body) => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [DataContract]
        public sealed class LevelRequest { [DataMember(Name = "levelId")] public int LevelId; }
        [DataContract]
        public sealed class LevelResponse { [DataMember(Name = "score")] public int Score; }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;
            internal int Calls;
            internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) { this.send = send; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return send(request, cancellationToken);
            }
        }

        private sealed class HookCodec : IProtocolCodec
        {
            private readonly DataContractJsonProtocolCodec inner = new DataContractJsonProtocolCodec();
            internal int EncodeCalls;
            internal int DecodeCalls;
            internal Action OnDecode;
            public string ContentType => inner.ContentType;
            public byte[] Encode<T>(T request) { Interlocked.Increment(ref EncodeCalls); return inner.Encode(request); }
            public ProtocolEnvelope<T> Decode<T>(byte[] response)
            {
                Interlocked.Increment(ref DecodeCalls);
                OnDecode?.Invoke();
                return inner.Decode<T>(response);
            }
        }

        private sealed class BlockingReadStream : Stream
        {
            internal readonly TaskCompletionSource<bool> Cancelled = Signal();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(true); throw; }
                return 0;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
