using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Networking.Realtime;
using Test.Application.Protocol;

namespace Test.Application.Combat
{
    public sealed class ContractScoreSync : IAsyncDisposable
    {
        private static readonly JsonSerializer Serializer = new JsonSerializer();
        private readonly ContractSession contract;
        private readonly MainThreadDispatcher dispatcher;
        private readonly HttpClient http;
        private readonly RealtimeClient realtime;
        private readonly Uri recoveryUrl;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task session;

        public ContractScoreSync(Uri address, ContractSession contract, MainThreadDispatcher dispatcher)
        {
            if (!address.IsAbsoluteUri || (address.Scheme != "http" && address.Scheme != "https") ||
                !address.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("The score service needs an HTTP(S) directory URL ending in /.", nameof(address));
            this.contract = contract;
            this.dispatcher = dispatcher;
            recoveryUrl = new Uri(address, "contract-scores/recovery");
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var socket = new UriBuilder(new Uri(address, "contract-scores/session"))
            {
                Scheme = address.Scheme == "https" ? "wss" : "ws",
                Port = address.Port
            }.Uri;
            realtime = new RealtimeClient(socket, new ClientWebSocketTransportFactory(), new BinaryRealtimeCodec(),
                new Recovery(this), new RealtimeOptions(connectTimeout: TimeSpan.FromSeconds(3),
                    heartbeatInterval: TimeSpan.FromSeconds(4), heartbeatTimeout: TimeSpan.FromSeconds(2)));
        }

        /// <summary>Call periodically on the game thread. Each attempt sends the durable submission ID.</summary>
        public async Task ExchangeAsync()
        {
            if (session == null || session.IsCompleted)
            {
                if (session != null)
                {
                    var completed = session;
                    session = null;
                    await completed;
                }
                session = realtime.RunSessionAsync(contract.ScoreCursor, ApplyReceiptAsync, cancellationToken: lifetime.Token);
            }
            if (!realtime.IsConnected || contract.NextPendingScore is not SubmitContractScoreRequest score) return;
            await realtime.SendAsync(await Serializer.SerializeAsync(score), lifetime.Token);
        }

        private async Task ApplyReceiptAsync(RealtimePush push, CancellationToken token)
        {
            var receipt = await Serializer.DeserializeAsync<SubmitContractScoreResponse>(push.Payload).ConfigureAwait(false);
            if (receipt == null || receipt.Sequence != push.Sequence || !Guid.TryParseExact(receipt.SubmissionId, "N", out _))
                throw new InvalidDataException("Invalid score receipt.");
            Task commit = null;
            await dispatcher.InvokeAsync(() => commit = contract.ConfirmScoreAsync(receipt, token), token).ConfigureAwait(false);
            await commit.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            try
            {
                await realtime.DisposeAsync();
                if (session != null)
                {
                    try { await session; }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (RealtimeDisconnectedException) { }
                }
            }
            finally
            {
                http.Dispose();
                lifetime.Dispose();
            }
        }

        private sealed class Recovery : IPushRecovery
        {
            private readonly ContractScoreSync owner;
            public Recovery(ContractScoreSync owner) => this.owner = owner;

            public async Task<PushRecoveryBatch> RecoverAsync(long afterSequence, CancellationToken token)
            {
                var address = new UriBuilder(owner.recoveryUrl) { Query = "after=" + afterSequence }.Uri;
                using var response = await owner.http.GetAsync(address, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var batch = await Serializer.DeserializeAsync<ContractScoreRecovery>(
                    await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)).ConfigureAwait(false);
                if (batch?.Receipts == null) throw new InvalidDataException("Missing score recovery batch.");
                var pushes = await Task.WhenAll(batch.Receipts.Select(async receipt =>
                    new RealtimePush(receipt.Sequence, await Serializer.SerializeAsync(receipt)))).ConfigureAwait(false);
                return new PushRecoveryBatch(batch.ThroughSequence, pushes);
            }
        }
    }

    public sealed class ContractScoreRecovery
    {
        public long ThroughSequence { get; set; }
        public SubmitContractScoreResponse[] Receipts { get; set; }
    }
}
