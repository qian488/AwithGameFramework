using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public sealed class TrialSession
    {
        private const int SaveSlot = 1;
        private readonly CombatRun run;
        private readonly VersionedSaveService<Records> saves;
        private Records records = new Records();

        public TrialSession(CombatRun run, string saveDirectory)
        {
            this.run = run;
            saves = new VersionedSaveService<Records>(saveDirectory, new JsonSerializer(), 1);
        }

        public TrialOpponent Opponent => run.TrialOpponent;
        public RunStatus Status => run.Status;
        public int ElapsedTicks => run.Tick;
        public bool NewRecord { get; private set; }
        public bool IsSaving { get; private set; }
        public bool SaveFailed { get; private set; }
        public bool RecordsUnavailable { get; private set; }

        public async Task OpenAsync()
        {
            try
            {
                var result = await saves.LoadAsync(SaveSlot);
                if (result.Status != SaveLoadStatus.Loaded) return;
                if (result.Data?.BestTicks == null)
                    throw new InvalidDataException("Rain Pass records are missing the bestTicks table.");
                foreach (var entry in result.Data.BestTicks)
                    if (entry.Key <= 0 || entry.Value <= 0)
                        throw new InvalidDataException($"Rain Pass record has invalid opponent ID or time: {entry.Key}, {entry.Value} ticks.");
                records = result.Data;
            }
            catch
            {
                RecordsUnavailable = true;
                throw;
            }
        }

        public int? BestTicks(int opponentId) => records.BestTicks.TryGetValue(opponentId, out int ticks) ? ticks : (int?)null;

        public void Start(TrialOpponent opponent)
        {
            if (IsSaving) throw new System.InvalidOperationException("Wait for the current trial record to finish saving.");
            NewRecord = SaveFailed = false;
            run.StartTrial(opponent);
        }

        public Task Step(Vector3 move, Vector3 look, bool guard, Vector3? arrowAim = null)
        {
            if (Opponent == null || Status != RunStatus.Fighting) return null;
            run.Step(move, look, guard, arrowAim);
            return Status == RunStatus.Victory && !RecordsUnavailable && ElapsedTicks < (BestTicks(Opponent.Id) ?? int.MaxValue)
                ? SaveRecordAsync() : null;
        }

        public async Task SaveRecordAsync()
        {
            if (Opponent == null || Status != RunStatus.Victory || RecordsUnavailable || IsSaving || ElapsedTicks >= (BestTicks(Opponent.Id) ?? int.MaxValue)) return;
            int opponentId = Opponent.Id;
            var next = new Records { BestTicks = new Dictionary<int, int>(records.BestTicks) { [opponentId] = ElapsedTicks } };
            SaveFailed = false;
            IsSaving = true;
            try
            {
                await saves.SaveAsync(SaveSlot, next, "Rain Pass trial best times");
                records = next;
                NewRecord = true;
            }
            catch
            {
                SaveFailed = true;
                throw;
            }
            finally { IsSaving = false; }
        }

        private sealed class Records
        {
            public Dictionary<int, int> BestTicks { get; set; } = new Dictionary<int, int>();
        }
    }
}
