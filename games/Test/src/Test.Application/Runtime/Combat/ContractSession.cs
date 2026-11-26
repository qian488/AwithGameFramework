using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Application.Protocol;

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
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
        private Progress progress = new Progress();
        private string contractId = CedarId;
        private string contentVersion = "built-in";
        private int victoryTicks;
        private string resultSubmissionId;

        public ContractSession(string saveDirectory)
        {
            saves = new VersionedSaveService<Progress>(saveDirectory, Serializer, 3,
                new[] { new SaveMigration(1, 2, MigrateCompletionAsync), new SaveMigration(2, 3, MigrateProgressAsync) });
        }

        public ContractPhase Phase { get; private set; }
        public bool Completed => HasCompleted(contractId);
        public bool WasMigrated { get; private set; }
        public bool SavedResult { get; private set; }
        public bool IsSaving { get; private set; }
        public long ScoreCursor => progress.ScoreCursor;
        public int PendingScoreCount => progress.PendingScores.Count;
        public bool HasResultSubmission => resultSubmissionId != null;
        public bool IsResultPending => progress.PendingScores.Exists(score => score.SubmissionId == resultSubmissionId);
        public SubmitContractScoreRequest NextPendingScore =>
            progress.PendingScores.Count == 0 ? null : CopyScore(progress.PendingScores[0]);
        public bool HasCompleted(string id) => progress.Contracts.ContainsKey(id);

        public int? BestTicks(string id, string version) =>
            progress.Contracts.TryGetValue(id, out var record) && record.BestTicks.TryGetValue(version, out int ticks) ? ticks : (int?)null;

        public async Task OpenAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Opening) return;
            var result = await saves.LoadAsync(SaveSlot, token);
            if (result.Status != SaveLoadStatus.NotFound)
            {
                if (result.Data?.Contracts == null || result.Data.PendingScores == null || result.Data.ScoreCursor < 0)
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
            resultSubmissionId = null;
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
            if (contentVersion != "built-in" && elapsedTicks > 0)
                resultSubmissionId = Guid.NewGuid().ToString("N");
            Phase = ContractPhase.Result;
            return true;
        }

        public async Task SaveResultAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Result || IsSaving || SavedResult) return;
            IsSaving = true;
            try
            {
                await writeGate.WaitAsync(token);
                try
                {
                    int? best = BestTicks(contractId, contentVersion);
                    bool improved = victoryTicks > 0 && (!best.HasValue || victoryTicks < best.Value);
                    if (!Completed || improved || resultSubmissionId != null)
                    {
                        progress.Contracts.TryGetValue(contractId, out var previous);
                        var next = Snapshot();
                        var record = new Record
                        {
                            FirstCompletedUtc = previous?.FirstCompletedUtc ?? DateTime.UtcNow,
                            BestTicks = previous == null ? new Dictionary<string, int>() : new Dictionary<string, int>(previous.BestTicks)
                        };
                        if (improved) record.BestTicks[contentVersion] = victoryTicks;
                        next.Contracts[contractId] = record;
                        if (resultSubmissionId != null)
                            next.PendingScores.Add(new SubmitContractScoreRequest
                            {
                                SubmissionId = resultSubmissionId, ContractId = contractId,
                                ContentVersion = contentVersion, ElapsedTicks = victoryTicks
                            });
                        await PersistAsync(next, token);
                    }
                    SavedResult = true;
                }
                finally { writeGate.Release(); }
            }
            finally { IsSaving = false; }
        }

        internal async Task ConfirmScoreAsync(SubmitContractScoreResponse receipt, CancellationToken token)
        {
            await writeGate.WaitAsync(token);
            try
            {
                if (receipt.Sequence <= progress.ScoreCursor) return;
                if (receipt.Sequence != progress.ScoreCursor + 1)
                    throw new InvalidDataException("The score confirmation sequence is discontinuous.");
                var pending = progress.PendingScores.Find(score => score.SubmissionId == receipt.SubmissionId);
                if (pending != null && (pending.ContractId != receipt.ContractId || pending.ContentVersion != receipt.ContentVersion ||
                    pending.ElapsedTicks != receipt.ElapsedTicks))
                    throw new InvalidDataException("The score confirmation differs from the local submission.");
                var next = Snapshot();
                next.PendingScores.RemoveAll(score => score.SubmissionId == receipt.SubmissionId);
                next.ScoreCursor = receipt.Sequence;
                await PersistAsync(next, token);
            }
            finally { writeGate.Release(); }
        }

        private Progress Snapshot() => new Progress
        {
            Contracts = new Dictionary<string, Record>(progress.Contracts),
            PendingScores = new List<SubmitContractScoreRequest>(progress.PendingScores),
            ScoreCursor = progress.ScoreCursor
        };

        private async Task PersistAsync(Progress next, CancellationToken token)
        {
            await saves.SaveAsync(SaveSlot, next, "Rain Pass contracts", token);
            progress = next;
        }

        private static SubmitContractScoreRequest CopyScore(SubmitContractScoreRequest score) => new SubmitContractScoreRequest
        {
            SubmissionId = score.SubmissionId, ContractId = score.ContractId,
            ContentVersion = score.ContentVersion, ElapsedTicks = score.ElapsedTicks
        };

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

        private static async Task<byte[]> MigrateProgressAsync(byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return await Serializer.SerializeAsync(await Serializer.DeserializeAsync<Progress>(bytes));
        }

        private sealed class LegacyCompletion { public bool Completed { get; set; } }
        private sealed class Progress
        {
            public Dictionary<string, Record> Contracts { get; set; } = new Dictionary<string, Record>();
            public List<SubmitContractScoreRequest> PendingScores { get; set; } = new List<SubmitContractScoreRequest>();
            public long ScoreCursor { get; set; }
        }

        private sealed class Record
        {
            public DateTime FirstCompletedUtc { get; set; }
            public Dictionary<string, int> BestTicks { get; set; } = new Dictionary<string, int>();
        }
    }
}
