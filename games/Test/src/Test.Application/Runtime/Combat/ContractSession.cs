using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;

namespace Test.Application.Combat
{
    public enum ContractPhase { Opening, Camp, Loading, Battle, Result, Returning }

    public sealed class ContractSession
    {
        public const int BossId = 101;
        public const string CedarId = "cedar-grove";
        private const int SaveSlot = 1;
        private static readonly JsonSerializer Serializer = new JsonSerializer();
        private readonly VersionedSaveService<Progress> saves;
        private Progress progress = new Progress();
        private string contractId = CedarId;
        private string contentVersion = "built-in";
        private int victoryTicks;

        public ContractSession(string saveDirectory)
        {
            saves = new VersionedSaveService<Progress>(saveDirectory, Serializer, 2,
                new[] { new SaveMigration(1, 2, MigrateCompletionAsync) });
        }

        public ContractPhase Phase { get; private set; }
        public bool Completed => HasCompleted(contractId);
        public bool WasMigrated { get; private set; }
        public bool SavedResult { get; private set; }
        public bool IsSaving { get; private set; }
        public bool HasCompleted(string id) => progress.Contracts.ContainsKey(id);

        public int? BestTicks(string id, string version) =>
            progress.Contracts.TryGetValue(id, out var record) && record.BestTicks.TryGetValue(version, out int ticks) ? ticks : (int?)null;

        public async Task OpenAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Opening) return;
            var result = await saves.LoadAsync(SaveSlot, token);
            if (result.Status != SaveLoadStatus.NotFound)
            {
                if (result.Data?.Contracts == null)
                    throw new InvalidDataException("The Rain Pass contract save is missing its completion records.");
                foreach (var record in result.Data.Contracts.Values)
                    if (record?.BestTicks == null || record.FirstCompletedUtc == default)
                        throw new InvalidDataException("The Rain Pass contract save has an invalid completion record.");
                progress = result.Data;
            }
            WasMigrated = result.WasMigrated;
            Phase = ContractPhase.Camp;
        }

        public bool TryAccept(string id = CedarId, string version = "built-in")
        {
            if (Phase != ContractPhase.Camp) return false;
            contractId = id;
            contentVersion = version;
            SavedResult = false;
            Phase = ContractPhase.Loading;
            return true;
        }

        public bool TryBeginBattle()
        {
            if (Phase != ContractPhase.Loading) return false;
            Phase = ContractPhase.Battle;
            return true;
        }

        public bool TryRecordVictory(RunStatus status, int elapsedTicks = 0)
        {
            if (Phase != ContractPhase.Battle || status != RunStatus.Victory) return false;
            victoryTicks = elapsedTicks;
            Phase = ContractPhase.Result;
            return true;
        }

        public async Task SaveResultAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Result || IsSaving || SavedResult) return;
            IsSaving = true;
            try
            {
                if (!Completed || victoryTicks > 0 && (!BestTicks(contractId, contentVersion).HasValue ||
                    victoryTicks < BestTicks(contractId, contentVersion).Value))
                {
                    progress.Contracts.TryGetValue(contractId, out var previous);
                    var next = new Progress { Contracts = new Dictionary<string, Record>(progress.Contracts) };
                    var record = new Record
                    {
                        FirstCompletedUtc = previous?.FirstCompletedUtc ?? DateTime.UtcNow,
                        BestTicks = previous == null ? new Dictionary<string, int>() : new Dictionary<string, int>(previous.BestTicks)
                    };
                    if (victoryTicks > 0) record.BestTicks[contentVersion] = victoryTicks;
                    next.Contracts[contractId] = record;
                    await saves.SaveAsync(SaveSlot, next, "Rain Pass contracts", token);
                    progress = next;
                }
                SavedResult = true;
            }
            finally { IsSaving = false; }
        }

        public bool TryReturn()
        {
            if (Phase != ContractPhase.Loading && Phase != ContractPhase.Battle &&
                (Phase != ContractPhase.Result || !SavedResult)) return false;
            Phase = ContractPhase.Returning;
            return true;
        }

        public void FinishReturn()
        {
            if (Phase == ContractPhase.Returning) Phase = ContractPhase.Camp;
        }

        private static async Task<byte[]> MigrateCompletionAsync(byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var old = await Serializer.DeserializeAsync<LegacyCompletion>(bytes);
            if (old?.Completed != true) throw new InvalidDataException("The legacy Rain Pass save has no completion record.");
            var result = new Progress();
            result.Contracts.Add(CedarId, new Record { FirstCompletedUtc = DateTime.UtcNow });
            return await Serializer.SerializeAsync(result);
        }

        private sealed class LegacyCompletion { public bool Completed { get; set; } }
        private sealed class Progress
        {
            public Dictionary<string, Record> Contracts { get; set; } = new Dictionary<string, Record>();
        }

        private sealed class Record
        {
            public DateTime FirstCompletedUtc { get; set; }
            public Dictionary<string, int> BestTicks { get; set; } = new Dictionary<string, int>();
        }
    }
}
