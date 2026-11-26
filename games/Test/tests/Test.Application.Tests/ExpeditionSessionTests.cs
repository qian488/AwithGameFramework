using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Test.Application.Expedition;

namespace Test.Application.Tests
{
    public sealed class ExpeditionSessionTests
    {
        private string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "ExpeditionTest-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public async Task RestartKeepsTheOriginalCheckpointAndRewardIsGrantedOncePerRoute()
        {
            var session = new ExpeditionSession(root);
            await session.SaveCheckpointAsync(Checkpoint("v1"));
            await session.SelectSupplyAsync("posture");
            var resumed = new ExpeditionSession(root);
            await resumed.OpenAsync();
            Assert.That(resumed.Checkpoint.Version, Is.EqualTo("v1"));
            Assert.That(resumed.Checkpoint.Supply, Is.EqualTo("posture"));
            await resumed.CompleteAsync(75, 1);
            await resumed.SaveCheckpointAsync(Checkpoint("v2"));
            await resumed.CompleteAsync(60, 1);
            Assert.That(resumed.Marks, Is.EqualTo(1));
            Assert.That(resumed.Latest.Reward, Is.Zero);
            await resumed.ConfirmAsync(resumed.NextPending.Checkpoint.Id);
            await resumed.SelectGearAsync("guard", 1);

            var reopened = new ExpeditionSession(root);
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
            File.WriteAllText(root, "blocked directory");
            try
            {
                var session = new ExpeditionSession(root);
                var checkpoint = Checkpoint("v1");
                Assert.ThrowsAsync<IOException>(async () => await session.SaveCheckpointAsync(checkpoint));
                Assert.That(session.Checkpoint, Is.Null);
                File.Delete(root);
                await session.SaveCheckpointAsync(checkpoint);
                Assert.That(session.Checkpoint.Id, Is.EqualTo(checkpoint.Id));
            }
            finally { if (File.Exists(root)) File.Delete(root); }
        }

        private static ExpeditionCheckpoint Checkpoint(string version) => new ExpeditionCheckpoint
        {
            Id = Guid.NewGuid().ToString("N"), Version = version, Route = "salt-causeway", Gear = "standard",
            Fingerprint = new string('a', 64), Supply = "arrows", FirstTicks = 60, Arrows = 2,
            Capacity = 6, PostureLimit = 100, RewardAmount = 1
        };
    }
}
