using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Application.Expedition;

namespace Test.Application.Tests
{
    public sealed class ExpeditionSessionTests
    {
        private string root;
        private static readonly string Fingerprint = new string('a', 64);

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "ExpeditionTest-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [TestCase("posture", 2, 0)]
        [TestCase("arrows", 5, 35)]
        public async Task RestartKeepsPinnedContentAndAppliesSelectedSupply(string supply, int arrows, int posture)
        {
            var catalog = await ReadCatalogAsync();
            var session = OpenSession();
            var battle = session.StartRoute(catalog, "salt-causeway", "v1", Fingerprint);
            Assert.That(battle.Arrows, Is.EqualTo(6));
            Assert.That(battle.ContractId, Is.EqualTo("salt-causeway"));
            Assert.That(await session.FinishBattleAsync(60, 2, 35, () => null), Is.EqualTo(ExpeditionBattleOutcome.CheckpointSaved));
            await session.SelectSupplyAsync(supply);
            var resumed = OpenSession();
            await resumed.OpenAsync();
            Assert.That(resumed.Checkpoint.Version, Is.EqualTo("v1"));
            Assert.That(resumed.Checkpoint.Supply, Is.EqualTo(supply));
            Assert.Throws<IOException>(() => resumed.Resume(catalog, new string('b', 64)));
            Assert.That(resumed.ActiveBattle, Is.Null);
            battle = resumed.Resume(catalog, Fingerprint);
            Assert.That(battle.ContractId, Is.EqualTo(ContractSession.CedarId));
            Assert.That(battle.ContentVersion, Is.EqualTo("v1"));
            Assert.That(battle.Arrows, Is.EqualTo(arrows));
            Assert.That(battle.Posture, Is.EqualTo(posture));
            resumed.CancelBattle();
            Assert.That(resumed.Checkpoint.Supply, Is.EqualTo(supply));
            resumed.Resume(catalog, Fingerprint);
            Assert.That(await resumed.FinishBattleAsync(75, 1, 0, () => null), Is.EqualTo(ExpeditionBattleOutcome.Completed));
            var updatedFingerprint = new string('b', 64);
            resumed.StartRoute(catalog, "salt-causeway", "v2", updatedFingerprint);
            await resumed.FinishBattleAsync(60, 2, 20, () => null);
            resumed.Resume(catalog, updatedFingerprint);
            await resumed.FinishBattleAsync(60, 1, 0, () => null);
            Assert.That(resumed.Marks, Is.EqualTo(1));
            Assert.That(resumed.Latest.Reward, Is.Zero);
            await resumed.ConfirmAsync(resumed.NextPending.Checkpoint.Id);
            await resumed.SelectGearAsync("guard", 1);

            var reopened = OpenSession();
            await reopened.OpenAsync();
            Assert.That(reopened.Checkpoint, Is.Null);
            Assert.That(reopened.NextPending.Checkpoint.Version, Is.EqualTo("v2"));
            Assert.That(reopened.Owns("guard"), Is.True);
            Assert.That(reopened.Gear, Is.EqualTo("guard"));
            Assert.That(reopened.Marks, Is.Zero);
        }

        [Test]
        public async Task FailedCheckpointWriteLeavesSessionRetryable()
        {
            var catalog = await ReadCatalogAsync();
            Directory.CreateDirectory(root);
            string profile = Path.Combine(root, "profile");
            File.WriteAllText(profile, "blocked directory");
            var session = OpenSession();
            var battle = session.StartRoute(catalog, "ember-foundry", "v1", Fingerprint);
            int captureCount = 0;
            ContractReplay CaptureReplay()
            {
                captureCount++;
                return new ContractReplay
                {
                    ContractId = battle.ContractId,
                    ContentVersion = battle.ContentVersion,
                    Fingerprint = Fingerprint
                };
            }

            try
            {
                Assert.ThrowsAsync<IOException>(async () => await session.FinishBattleAsync(60, 2, 35, CaptureReplay));
                Assert.That(session.Checkpoint, Is.Null);
                Assert.That(session.ActiveBattle, Is.SameAs(battle));
                File.Delete(profile);
                Assert.That(await session.FinishBattleAsync(60, 2, 35, CaptureReplay), Is.EqualTo(ExpeditionBattleOutcome.CheckpointSaved));
                Assert.That(captureCount, Is.EqualTo(1));
                Assert.That(session.ActiveBattle, Is.Null);
                Assert.That(session.Checkpoint.Route, Is.EqualTo("ember-foundry"));
                session.Resume(catalog, Fingerprint);
                await session.FinishBattleAsync(75, 1, 0, () => null);
                Assert.That(session.ReplaysFor(session.Latest).HasReplay("ember-foundry", "v1"), Is.True);
            }
            finally { if (File.Exists(profile)) File.Delete(profile); }
        }

        private ExpeditionSession OpenSession() => new ExpeditionSession(Path.Combine(root, "profile"), Path.Combine(root, "replays"));

        private static Task<ExpeditionCatalog> ReadCatalogAsync([CallerFilePath] string testFile = "") =>
            ExpeditionCatalog.LoadAsync(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(testFile), "../../configuration/expedition.json")));
    }
}
