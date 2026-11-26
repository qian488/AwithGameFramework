using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Domain.Expedition;

namespace Test.Application.Expedition
{
    public sealed class ExpeditionCheckpoint
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string Route { get; set; }
        public string Gear { get; set; }
        public string Fingerprint { get; set; }
        public string Supply { get; set; }
        public int FirstTicks { get; set; }
        public int Arrows { get; set; }
        public int Posture { get; set; }
        public int Capacity { get; set; }
        public int PostureLimit { get; set; }
        public int RecoveryBonus { get; set; }
        public int RewardAmount { get; set; }
    }

    public sealed class ExpeditionResult
    {
        public ExpeditionCheckpoint Checkpoint { get; set; }
        public int SecondTicks { get; set; }
        public int Reward { get; set; }
        public bool Pending { get; set; }
    }

    public sealed class ExpeditionSession
    {
        private readonly VersionedSaveService<Profile> saves;
        private Profile profile = new Profile();

        public ExpeditionSession(string directory)
        {
            saves = new VersionedSaveService<Profile>(directory, new JsonSerializer(), 1);
        }

        public int Marks => profile.Marks;
        public string Gear => profile.Gear;
        public ExpeditionCheckpoint Checkpoint => profile.Checkpoint;
        public ExpeditionResult Latest => profile.Results.LastOrDefault();
        public ExpeditionResult NextPending => profile.Results.FirstOrDefault(result => result.Pending);
        public bool Owns(string gear) => profile.Owned.Contains(gear);

        public async Task OpenAsync()
        {
            var result = await saves.LoadAsync(0);
            if (result.Status == SaveLoadStatus.NotFound) return;
            var restored = result.Data;
            if (restored?.Owned == null || restored.Results == null || restored.Marks < 0 ||
                !restored.Owned.Contains("standard") || !restored.Owned.Contains(restored.Gear) ||
                restored.Owned.Any(gear => !ExpeditionRules.IsGear(gear)) ||
                (restored.Checkpoint != null && !Valid(restored.Checkpoint)) ||
                restored.Results.Any(item => item == null || !Valid(item.Checkpoint) || item.SecondTicks <= 0))
                throw new InvalidDataException("The expedition profile contains invalid saved progress.");
            profile = restored;
        }

        public async Task SelectGearAsync(string gear, int price)
        {
            if (!ExpeditionRules.IsGear(gear)) throw new ArgumentException("Unknown expedition gear.", nameof(gear));
            var next = Next();
            if (!next.Owned.Contains(gear))
            {
                if (!ExpeditionRules.CanUnlock(next.Marks, price, false))
                    throw new InvalidOperationException("Not enough forging marks.");
                next.Marks -= price;
                next.Owned.Add(gear);
            }
            next.Gear = gear;
            await SaveAsync(next);
        }

        public async Task SaveCheckpointAsync(ExpeditionCheckpoint checkpoint)
        {
            if (!Valid(checkpoint)) throw new ArgumentException("Invalid expedition checkpoint.", nameof(checkpoint));
            var next = Next();
            next.Checkpoint = checkpoint;
            await SaveAsync(next);
        }

        public async Task AbandonAsync()
        {
            var next = Next();
            next.Checkpoint = null;
            await SaveAsync(next);
        }

        public Task SelectSupplyAsync(string supply)
        {
            var current = profile.Checkpoint ?? throw new InvalidOperationException("An expedition checkpoint is required.");
            return SaveCheckpointAsync(new ExpeditionCheckpoint
            {
                Id = current.Id, Version = current.Version, Route = current.Route, Gear = current.Gear,
                Fingerprint = current.Fingerprint, Supply = supply, FirstTicks = current.FirstTicks,
                Arrows = current.Arrows, Posture = current.Posture, Capacity = current.Capacity,
                PostureLimit = current.PostureLimit, RecoveryBonus = current.RecoveryBonus, RewardAmount = current.RewardAmount
            });
        }

        public async Task CompleteAsync(int secondTicks, int rewardAmount)
        {
            var checkpoint = profile.Checkpoint ?? throw new InvalidOperationException("An expedition checkpoint is required.");
            if (secondTicks <= 0 || rewardAmount < 0) throw new ArgumentOutOfRangeException(nameof(secondTicks));
            if (profile.Results.Any(item => item.Checkpoint.Id == checkpoint.Id))
                throw new InvalidOperationException("The expedition result was already recorded.");
            var next = Next();
            int reward = ExpeditionRules.AwardFirstClear(next.Results.Any(item =>
                item.Checkpoint.Route == checkpoint.Route)) * rewardAmount;
            next.Marks = checked(next.Marks + reward);
            next.Results.Add(new ExpeditionResult
            {
                Checkpoint = checkpoint, SecondTicks = secondTicks, Reward = reward, Pending = true
            });
            next.Checkpoint = null;
            await SaveAsync(next);
        }

        public async Task ConfirmAsync(string id)
        {
            var next = Next();
            int index = next.Results.FindIndex(result => result.Checkpoint.Id == id && result.Pending);
            if (index < 0) return;
            var pending = next.Results[index];
            next.Results[index] = new ExpeditionResult
            {
                Checkpoint = pending.Checkpoint, SecondTicks = pending.SecondTicks, Reward = pending.Reward
            };
            await SaveAsync(next);
        }

        private Profile Next() => new Profile
        {
            Marks = profile.Marks, Gear = profile.Gear, Owned = new List<string>(profile.Owned),
            Checkpoint = profile.Checkpoint, Results = new List<ExpeditionResult>(profile.Results)
        };

        private async Task SaveAsync(Profile next)
        {
            await saves.SaveAsync(0, next, "Rain Pass expedition");
            profile = next;
        }

        private static bool Valid(ExpeditionCheckpoint checkpoint) =>
            Guid.TryParseExact(checkpoint.Id, "N", out _) && !string.IsNullOrWhiteSpace(checkpoint.Version) &&
            ExpeditionRules.IsRoute(checkpoint.Route) && ExpeditionRules.IsGear(checkpoint.Gear) &&
            ExpeditionRules.IsSupply(checkpoint.Supply) && checkpoint.FirstTicks > 0 && checkpoint.Arrows >= 0 &&
            checkpoint.Capacity > 0 && checkpoint.Arrows <= checkpoint.Capacity &&
            checkpoint.PostureLimit > 0 && checkpoint.Posture >= 0 && checkpoint.Posture < checkpoint.PostureLimit &&
            checkpoint.RecoveryBonus >= 0 && checkpoint.RecoveryBonus < 12 && checkpoint.RewardAmount > 0 &&
            !string.IsNullOrWhiteSpace(checkpoint.Fingerprint);

        private sealed class Profile
        {
            public int Marks { get; set; }
            public string Gear { get; set; } = "standard";
            public List<string> Owned { get; set; } = new List<string> { "standard" };
            public ExpeditionCheckpoint Checkpoint { get; set; }
            public List<ExpeditionResult> Results { get; set; } = new List<ExpeditionResult>();
        }
    }
}
