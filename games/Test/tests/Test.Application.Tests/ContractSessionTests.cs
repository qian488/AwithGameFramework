using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Test.Application.Combat;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;

namespace Test.Application.Tests
{
    public sealed class ContractSessionTests
    {
        private string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "RainPassContract-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public async Task ReplaySaveReplacesTheSameVersionAndRetainsOtherVersions()
        {
            var store = new ContractReplayStore(root);
            var replay = new ContractReplay { ContractId = "salt-causeway", ContentVersion = "v1" };
            var actor = new ReplayActor
            {
                Position = new System.Numerics.Vector3(1.25f, 0.02f, 4),
                Facing = System.Numerics.Vector3.UnitZ
            };
            replay.Frames.Add(new ReplayFrame { Actors = new[] { actor, actor }, Arrows = Array.Empty<ReplayArrow>() });
            replay.Frames.Add(new ReplayFrame
            {
                Actors = new[] { actor, actor }, Arrows = Array.Empty<ReplayArrow>(), Status = RunStatus.Victory
            });
            await store.SaveAsync(replay);
            replay.ContentVersion = "v2";
            await store.SaveAsync(replay);
            replay.ContentVersion = "v1";
            actor.Position = new System.Numerics.Vector3(2.5f, 0.02f, 5);
            await store.SaveAsync(replay);

            var reopened = new ContractReplayStore(root);
            Assert.That(reopened.Versions("salt-causeway"), Is.EqualTo(new[] { "v1", "v2" }));
            var latestV1 = await reopened.LoadAsync("salt-causeway", "v1");
            var existingV2 = await reopened.LoadAsync("salt-causeway", "v2");
            Assert.That(latestV1.Frames[1].Actors[0].Position, Is.EqualTo(actor.Position));
            Assert.That(existingV2.Frames[1].Actors[0].Position, Is.EqualTo(new System.Numerics.Vector3(1.25f, 0.02f, 4)));
            Assert.That(await reopened.LoadAsync("salt-causeway", "v3"), Is.Null);
        }

        [Test]
        public async Task CancelAndRetreatAllowAnotherAttemptWithoutSavingCompletion()
        {
            var session = new ContractSession(root);
            await session.OpenAsync();
            Assert.That(session.TryAccept(), Is.True);
            Assert.That(session.TryAccept(), Is.False);
            Assert.That(session.TryReturn(), Is.True);
            session.FinishReturn();
            Assert.That(session.TryAccept() && session.TryBeginBattle(), Is.True);
            Assert.That(session.TryRecordVictory(RunStatus.Defeated), Is.False);
            Assert.That(session.TryReturn(), Is.True);
            session.FinishReturn();
            Assert.That(session.Phase, Is.EqualTo(ContractPhase.Camp));
            Assert.That(session.Completed, Is.False);
            Assert.That(Directory.Exists(root), Is.False);
        }

        [Test]
        public async Task SaveFailureKeepsResultAvailableForRetryAndSuccessfulSaveSurvivesReopen()
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "save");
            var session = new ContractSession(path);
            await session.OpenAsync();
            Assert.That(session.TryAccept() && session.TryBeginBattle(), Is.True);
            Assert.That(session.TryRecordVictory(RunStatus.Victory), Is.True);
            File.WriteAllText(path, "block directory creation");
            Assert.ThrowsAsync<IOException>(async () => await session.SaveResultAsync());
            Assert.That(session.Phase, Is.EqualTo(ContractPhase.Result));
            Assert.That(session.SavedResult || session.Completed || session.TryReturn(), Is.False);
            File.Delete(path);
            await session.SaveResultAsync();
            Assert.That(session.Completed && session.SavedResult && session.TryReturn(), Is.True);
            var reopened = new ContractSession(path);
            await reopened.OpenAsync();
            Assert.That(reopened.Completed, Is.True);
            Assert.That(reopened.TryAccept(), Is.True);
        }

        [Test]
        public async Task PreservesLegacyCompletionAndKeepsIndependentBestTimes()
        {
            var legacy = new VersionedSaveService<LegacyCompletion>(root, new JsonSerializer(), 1);
            await legacy.SaveAsync(1, new LegacyCompletion { Completed = true }, "Rain Pass contract");
            var session = new ContractSession(root);
            await session.OpenAsync();
            Assert.That(session.WasMigrated && session.Completed, Is.True);
            Assert.That(session.BestTicks(ContractSession.CedarId, "built-in"), Is.Null);

            await Finish(session, "salt-causeway", "v1", 320);
            await Finish(session, "salt-causeway", "v1", 400);
            await Finish(session, "salt-causeway", "v1", 300);
            await Finish(session, "salt-causeway", "v2", 412);
            await Finish(session, "ember-foundry", "v1", 581);

            var reopened = new ContractSession(root);
            await reopened.OpenAsync();
            Assert.That(reopened.WasMigrated, Is.False);
            Assert.That(reopened.HasCompleted(ContractSession.CedarId), Is.True);
            Assert.That(reopened.BestTicks("salt-causeway", "v1"), Is.EqualTo(300));
            Assert.That(reopened.BestTicks("salt-causeway", "v2"), Is.EqualTo(412));
            Assert.That(reopened.BestTicks("ember-foundry", "v1"), Is.EqualTo(581));
        }

        [Test]
        public async Task MigratesRecordedBestTimesAndQueuesEachNewResultOnlyAfterSavingIt()
        {
            var legacy = new VersionedSaveService<LegacyProgress>(root, new JsonSerializer(), 2);
            await legacy.SaveAsync(1, new LegacyProgress
            {
                Contracts =
                {
                    ["salt-causeway"] = new LegacyRecord
                    {
                        FirstCompletedUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                        BestTicks = { ["v1"] = 300 }
                    }
                }
            }, "Rain Pass contracts");
            var session = new ContractSession(root);
            await session.OpenAsync();
            Assert.That(session.WasMigrated, Is.True);
            Assert.That(session.PendingScoreCount, Is.Zero);
            Assert.That(session.TryAccept("salt-causeway", "v1") && session.TryBeginBattle() &&
                session.TryRecordVictory(RunStatus.Victory, 350), Is.True);
            Assert.That(session.PendingScoreCount, Is.Zero);
            await session.SaveResultAsync();
            await session.SaveResultAsync();

            var reopened = new ContractSession(root);
            await reopened.OpenAsync();
            Assert.That(reopened.PendingScoreCount, Is.EqualTo(1));
            Assert.That(reopened.NextPendingScore.SubmissionId, Is.EqualTo(session.NextPendingScore.SubmissionId));
            Assert.That(reopened.NextPendingScore.ElapsedTicks, Is.EqualTo(350));
            Assert.That(reopened.BestTicks("salt-causeway", "v1"), Is.EqualTo(300));
        }

        private static async Task Finish(ContractSession session, string id, string version, int ticks)
        {
            Assert.That(session.TryAccept(id, version) && session.TryBeginBattle() &&
                session.TryRecordVictory(RunStatus.Victory, ticks), Is.True);
            await session.SaveResultAsync();
            Assert.That(session.TryReturn(), Is.True);
            session.FinishReturn();
        }

        private sealed class LegacyCompletion { public bool Completed { get; set; } }
        private sealed class LegacyProgress
        {
            public Dictionary<string, LegacyRecord> Contracts { get; set; } = new Dictionary<string, LegacyRecord>();
        }

        private sealed class LegacyRecord
        {
            public DateTime FirstCompletedUtc { get; set; }
            public Dictionary<string, int> BestTicks { get; set; } = new Dictionary<string, int>();
        }
    }
}
