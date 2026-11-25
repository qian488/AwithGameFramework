using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Test.Application.Combat;

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
    }
}
