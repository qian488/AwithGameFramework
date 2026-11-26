using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;

namespace Test.Application.Combat
{
    public sealed class ContractReplayStore
    {
        private static readonly Regex Slug = new Regex(@"\A[a-z0-9][a-z0-9-]{0,47}\z", RegexOptions.CultureInvariant);
        private readonly string directory;
        private readonly ISerializer serializer = new CompressedJsonSerializer(new SerializationConfig { EnableCompression = true });

        public ContractReplayStore(string directory) => this.directory = directory;

        public IReadOnlyList<string> Versions(string contractId)
        {
            var root = Path.Combine(directory, CheckSegment(contractId));
            if (!Directory.Exists(root)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(root).Select(Path.GetFileName)
                .Where(version => Slug.IsMatch(version) && HasReplay(contractId, version))
                .OrderBy(version => version, StringComparer.Ordinal).ToArray();
        }

        public bool HasReplay(string contractId, string version)
        {
            string file = For(contractId, version).GetFilePath(0);
            return File.Exists(file) || File.Exists(file + ".bak");
        }

        public Task<SaveMetadata> SaveAsync(ContractReplay replay) =>
            For(replay.ContractId, replay.ContentVersion).SaveAsync(0, replay, "Latest successful commission replay");

        public async Task<ContractReplay> LoadAsync(string contractId, string version)
        {
            var saved = await For(contractId, version).LoadAsync(0);
            if (saved.Status == SaveLoadStatus.NotFound) return null;
            var replay = saved.Data;
            if (replay?.ContractId != contractId || replay.ContentVersion != version || replay.Frames == null ||
                replay.Frames.Count < 2 || replay.Frames.Count > ContractReplay.MaximumTicks ||
                replay.Frames[replay.Frames.Count - 1]?.Status != RunStatus.Victory || replay.Markers == null ||
                replay.Frames.Any(frame => frame?.Actors == null || frame.Actors.Length < 2 || frame.Actors.Length > 4 ||
                    frame.Actors.Any(actor => actor == null) || frame.Arrows == null || frame.Arrows.Any(arrow => arrow == null)) ||
                replay.Markers.Any(marker => marker == null || marker.Tick < 1 || marker.Tick >= replay.Frames.Count))
                throw new InvalidDataException("Invalid commission replay for " + contractId + " " + version + ".");
            return replay;
        }

        private VersionedSaveService<ContractReplay> For(string contractId, string version) =>
            new VersionedSaveService<ContractReplay>(Path.Combine(directory, CheckSegment(contractId), CheckSegment(version)), serializer, 1);

        private static string CheckSegment(string value) =>
            value != null && Slug.IsMatch(value) ? value : throw new ArgumentException("Invalid replay path segment.", nameof(value));
    }
}
