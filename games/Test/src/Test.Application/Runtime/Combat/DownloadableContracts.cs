using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Networking.Content;

namespace Test.Application.Combat
{
    public sealed class DownloadableContractOffer
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
    }

    public sealed class InstalledContract
    {
        public InstalledContract(ContentSnapshot snapshot, MapBossOption boss, string configurationHash)
        {
            Snapshot = snapshot;
            Boss = boss;
            ConfigurationHash = configurationHash;
        }

        public ContentSnapshot Snapshot { get; }
        public MapBossOption Boss { get; }
        public string ConfigurationHash { get; }
    }

    public sealed class DownloadableContracts : IDisposable
    {
        private static readonly Regex Slug = new Regex(@"\A[a-z0-9][a-z0-9-]{0,47}\z", RegexOptions.CultureInvariant);
        private readonly Uri feed;
        private readonly string directory;
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly JsonSerializer json = new JsonSerializer();

        public DownloadableContracts(Uri feed, string directory)
        {
            if (!feed.IsAbsoluteUri || !feed.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ||
                (feed.Scheme != Uri.UriSchemeHttp && feed.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("The contract feed needs an HTTP(S) directory URL ending in /.", nameof(feed));
            this.feed = feed;
            this.directory = directory;
        }

        public async Task<IReadOnlyList<DownloadableContractOffer>> ReadOffersAsync(CancellationToken token)
        {
            var bytes = await GetAsync(new Uri(feed, "index.json"), token);
            var offers = await ReadOffersAsync(bytes);
            var path = Path.Combine(directory, "index.json");
            Directory.CreateDirectory(directory);
            var pending = path + ".pending";
            try
            {
                File.WriteAllBytes(pending, bytes);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { File.Delete(pending); }
            return offers;
        }

        public async Task<IReadOnlyList<DownloadableContractOffer>> ReadCachedOffersAsync()
        {
            var path = Path.Combine(directory, "index.json");
            return File.Exists(path) ? await ReadOffersAsync(File.ReadAllBytes(path)) : Array.Empty<DownloadableContractOffer>();
        }

        private async Task<IReadOnlyList<DownloadableContractOffer>> ReadOffersAsync(byte[] bytes)
        {
            var offers = await json.DeserializeAsync<DownloadableContractOffer[]>(bytes);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (offers == null || offers.Any(offer => offer == null || !Valid(offer.Id) || offer.Id == ContractSession.CedarId ||
                !Valid(offer.Version) || string.IsNullOrWhiteSpace(offer.Title) || offer.Title.Length > 48 || !ids.Add(offer.Id)))
                throw new InvalidDataException("The contract feed contains an invalid or duplicate offer.");
            return offers;
        }

        public async Task<ContentSnapshot> OpenAsync(string id)
        {
            await using var updater = For(id);
            return updater.OpenCurrent();
        }

        public async Task<InstalledContract> OpenInstalledAsync(string id, string fingerprint)
        {
            await using var updater = For(id);
            var snapshot = updater.OpenInstalled(fingerprint);
            return snapshot == null ? null : await ResolveAsync(id, snapshot);
        }

        public async Task<InstalledContract> InstallAsync(DownloadableContractOffer offer, CancellationToken token)
        {
            if (!Valid(offer.Id) || !Valid(offer.Version)) throw new InvalidDataException("Invalid contract offer.");
            var source = new Uri(feed, offer.Id + "/" + offer.Version + "/");
            var manifest = ContentManifest.FromJson(await GetAsync(new Uri(source, "content-manifest.json"), token));
            if (manifest.Version != offer.Version) throw new InvalidDataException("The manifest version differs from the offer.");
            await using var updater = For(offer.Id);
            var previous = updater.OpenCurrent();
            var installed = await updater.UpdateAsync(manifest, source, token);
            try { return await ResolveAsync(offer.Id, installed); }
            catch (Exception error) when (previous != null && previous.Fingerprint != installed.Fingerprint)
            {
                try { await updater.RollbackAsync(); }
                catch (Exception rollback) { throw new AggregateException(error, rollback); }
                throw;
            }
        }

        public async Task<InstalledContract> RollbackAsync(string id, CancellationToken token)
        {
            await using var updater = For(id);
            return await ResolveAsync(id, await updater.RollbackAsync(token));
        }

        public async Task<InstalledContract> ResolveAsync(string id, ContentSnapshot snapshot)
        {
            if (snapshot == null) throw new InvalidOperationException("Install the contract before entering.");
            var descriptor = await json.DeserializeAsync<Descriptor>(File.ReadAllBytes(snapshot.GetPath("contract.json")));
            if (descriptor == null || descriptor.Id != id) throw new InvalidDataException("The installed contract ID differs from the offer.");
            var bosses = await MapBossCatalog.LoadAsync(File.ReadAllBytes(snapshot.GetPath("BossStrike.json")),
                File.ReadAllBytes(snapshot.GetPath("BossChallenge.json")));
            var boss = bosses.SingleOrDefault(item => item.Id == descriptor.BossId)
                ?? throw new InvalidDataException("The installed contract references a missing boss.");
            return new InstalledContract(snapshot, boss, bosses.ConfigurationHash);
        }

        private ContentUpdateService For(string id)
        {
            if (!Valid(id)) throw new ArgumentException("Invalid contract ID.", nameof(id));
            return new ContentUpdateService(Path.Combine(directory, id), downloadTimeout: TimeSpan.FromSeconds(15));
        }

        private static bool Valid(string value) => value != null && Slug.IsMatch(value);

        private async Task<byte[]> GetAsync(Uri address, CancellationToken token)
        {
            using var response = await http.GetAsync(address, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        public void Dispose() => http.Dispose();

        private sealed class Descriptor
        {
            public string Id { get; set; }
            public int BossId { get; set; }
        }
    }
}
