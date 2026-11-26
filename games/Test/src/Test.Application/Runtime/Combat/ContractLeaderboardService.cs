using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Networking;
using Test.Application.Protocol;

namespace Test.Application.Combat
{
    public sealed class ContractLeaderboardService : IAsyncDisposable
    {
        private readonly string playerId;
        private readonly VersionedSaveService<LeaderboardCache> cache;
        private readonly ProtocolClient client;
        private readonly HttpClientTransport transport;
        private LeaderboardCache current = new LeaderboardCache();

        public ContractLeaderboardService(Uri baseAddress, string playerId, string cacheDirectory)
        {
            if (baseAddress == null || !baseAddress.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("The leaderboard service needs a base URL ending in /.", nameof(baseAddress));
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A player ID is required.", nameof(playerId));
            this.playerId = playerId;
            cache = new VersionedSaveService<LeaderboardCache>(cacheDirectory, new JsonSerializer(), 1);
            transport = new HttpClientTransport(64 * 1024);
            client = new ProtocolClient(baseAddress, transport, new DataContractJsonProtocolCodec(),
                TimeSpan.FromSeconds(5));
        }

        public async Task InitializeAsync(CancellationToken token)
        {
            var result = await cache.LoadAsync(0, token);
            if (result.Status == SaveLoadStatus.NotFound) return;
            if (result.Data?.Entries == null) throw new InvalidDataException("Leaderboard cache is empty.");
            if (result.Data.PlayerId == playerId) current = result.Data;
        }

        public async Task<LeaderboardView> QueryAsync(string contractId, string version, CancellationToken token)
        {
            var request = new GetContractLeaderboardRequest
            {
                PlayerId = playerId, ContractId = contractId, ContentVersion = version, Limit = 10
            };
            var result = await client.SendAsync(GameProtocol.GetContractLeaderboard, request, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (result.IsSuccess && result.Data != null)
            {
                var cached = new CachedLeaderboard
                {
                    ContractId = contractId, ContentVersion = version, SavedAtUtc = DateTime.UtcNow, Response = result.Data
                };
                var next = new LeaderboardCache
                {
                    PlayerId = playerId,
                    Entries = current.Entries.Where(entry => entry.ContractId != contractId || entry.ContentVersion != version).ToList()
                };
                next.Entries.Add(cached);
                string cacheError = null;
                try { await cache.SaveAsync(0, next, "Contract leaderboard cache", token); }
                catch (IOException error) { cacheError = error.Message; }
                catch (UnauthorizedAccessException error) { cacheError = error.Message; }
                current = next;
                return new LeaderboardView(result.Data, false, cached.SavedAtUtc, cacheError);
            }

            var fallback = current.Entries.LastOrDefault(entry =>
                entry.ContractId == contractId && entry.ContentVersion == version);
            if (fallback == null)
                throw new IOException(result.Message ?? "The leaderboard is unavailable.");
            return new LeaderboardView(fallback.Response, true, fallback.SavedAtUtc);
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            transport.Dispose();
            return default;
        }

        public sealed class LeaderboardView
        {
            public LeaderboardView(GetContractLeaderboardResponse response, bool stale, DateTime savedAtUtc, string cacheError = null)
            {
                Response = response;
                IsStale = stale;
                SavedAtUtc = savedAtUtc;
                CacheError = cacheError;
            }

            public GetContractLeaderboardResponse Response { get; }
            public bool IsStale { get; }
            public DateTime SavedAtUtc { get; }
            public string CacheError { get; }
        }

        private sealed class LeaderboardCache
        {
            public string PlayerId { get; set; }
            public List<CachedLeaderboard> Entries { get; set; } = new List<CachedLeaderboard>();
        }

        private sealed class CachedLeaderboard
        {
            public string ContractId { get; set; }
            public string ContentVersion { get; set; }
            public DateTime SavedAtUtc { get; set; }
            public GetContractLeaderboardResponse Response { get; set; }
        }
    }
}
