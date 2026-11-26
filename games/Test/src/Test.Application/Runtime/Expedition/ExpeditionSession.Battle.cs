using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Test.Application.Combat;
using Test.Domain.Expedition;

namespace Test.Application.Expedition
{
    public sealed partial class ExpeditionSession
    {
        private ExpeditionCheckpoint activeRun;
        private ContractReplay pendingReplay;
        private bool replayCaptured;
        private bool finishingBattle;

        public ExpeditionBattle ActiveBattle { get; private set; }

        public ExpeditionBattle StartRoute(ExpeditionCatalog catalog, string route, string version, string fingerprint)
        {
            RequireIdle();
            if (Checkpoint != null) throw new InvalidOperationException("Continue or abandon the saved expedition first.");
            if (!catalog.Routes.Any(candidate => candidate.Id == route))
                throw new ArgumentException("Unknown expedition route.", nameof(route));
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("An installed route version and fingerprint are required.");
            var gear = catalog.Equipment.FirstOrDefault(item => item.Id == Gear)
                ?? throw new InvalidOperationException($"Selected expedition equipment '{Gear}' is missing from the configuration.");
            activeRun = new ExpeditionCheckpoint
            {
                Id = Guid.NewGuid().ToString("N"),
                Route = route,
                Version = version,
                Fingerprint = fingerprint,
                Gear = gear.Id,
                Supply = ExpeditionRules.ArrowsSupply,
                Capacity = gear.Arrows,
                Arrows = gear.Arrows,
                PostureLimit = gear.Posture,
                RecoveryBonus = ExpeditionRules.PostureRecoveryBonus(gear.Id),
                RewardAmount = catalog.FirstClearMarks
            };
            return ActiveBattle = new ExpeditionBattle(activeRun, route, isFinalLeg: false);
        }

        public ExpeditionBattle Resume(ExpeditionCatalog catalog, string installedFingerprint)
        {
            RequireIdle();
            var checkpoint = Checkpoint ?? throw new InvalidOperationException("An expedition checkpoint is required.");
            if (!string.Equals(checkpoint.Fingerprint, installedFingerprint, StringComparison.Ordinal))
                throw new IOException("Expedition requires the installed route " + checkpoint.Version + ".");
            activeRun = checkpoint;
            return ActiveBattle = new ExpeditionBattle(checkpoint, catalog.FinalRoute.Id, isFinalLeg: true);
        }

        public async Task<ExpeditionBattleOutcome> FinishBattleAsync(int ticks, int arrows, int posture,
            Func<ContractReplay> captureReplay)
        {
            var battle = ActiveBattle ?? throw new InvalidOperationException("An active expedition battle is required.");
            if (finishingBattle) throw new InvalidOperationException("The expedition result is already being saved.");
            finishingBattle = true;
            try
            {
                if (!replayCaptured)
                {
                    pendingReplay = captureReplay();
                    replayCaptured = true;
                }
                if (pendingReplay != null)
                {
                    await ReplaysFor(activeRun.Id).SaveAsync(pendingReplay);
                    pendingReplay = null;
                }

                if (battle.IsFinalLeg) await CompleteAsync(ticks);
                else
                {
                    activeRun.FirstTicks = ticks;
                    activeRun.Arrows = arrows;
                    activeRun.Posture = Math.Min(activeRun.PostureLimit - 1, posture);
                    await SaveCheckpointAsync(activeRun);
                }
            }
            finally { finishingBattle = false; }

            CancelBattle();
            return battle.IsFinalLeg ? ExpeditionBattleOutcome.Completed : ExpeditionBattleOutcome.CheckpointSaved;
        }

        public void CancelBattle()
        {
            if (finishingBattle) throw new InvalidOperationException("The expedition result is being saved.");
            ActiveBattle = null;
            activeRun = null;
            pendingReplay = null;
            replayCaptured = false;
        }

        public ContractReplayStore ReplaysFor(ExpeditionResult result) => ReplaysFor(result.Checkpoint.Id);

        private ContractReplayStore ReplaysFor(string runId) => new ContractReplayStore(Path.Combine(replayRoot, runId));

        private void RequireIdle()
        {
            if (ActiveBattle != null) throw new InvalidOperationException("An expedition battle is already active.");
        }
    }

    public enum ExpeditionBattleOutcome { CheckpointSaved, Completed }

    public sealed class ExpeditionBattle
    {
        internal ExpeditionBattle(ExpeditionCheckpoint run, string contractId, bool isFinalLeg)
        {
            ContractId = contractId;
            ContentVersion = run.Version;
            IsFinalLeg = isFinalLeg;
            Capacity = run.Capacity;
            Arrows = isFinalLeg ? ExpeditionRules.RestockArrows(run.Arrows, run.Capacity, run.Supply) : run.Capacity;
            Posture = isFinalLeg && run.Supply != ExpeditionRules.PostureSupply ? run.Posture : 0;
            PostureLimit = run.PostureLimit;
            RecoveryBonus = run.RecoveryBonus;
        }

        public string ContractId { get; }
        public string ContentVersion { get; }
        public string ContractVersion => IsFinalLeg ? "built-in" : ContentVersion;
        public bool IsFinalLeg { get; }
        public int Capacity { get; }
        public int Arrows { get; }
        public int Posture { get; }
        public int PostureLimit { get; }
        public int RecoveryBonus { get; }

        public void ApplyTo(CombatRun run) => run.ConfigureExpeditionKit(Arrows, Capacity, Posture, PostureLimit, RecoveryBonus);
    }
}
