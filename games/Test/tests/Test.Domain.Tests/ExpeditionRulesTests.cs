using NUnit.Framework;
using Test.Domain.Expedition;

namespace Test.Domain.Tests
{
    public sealed class ExpeditionRulesTests
    {
        [TestCase("salt-causeway")]
        [TestCase("ember-foundry")]
        public void BothRoutesEndAtCedarGrove(string route)
        {
            Assert.That(ExpeditionRules.IsRoute(route), Is.True);
            Assert.That(ExpeditionRules.ContractForLeg(route, 0), Is.EqualTo(route));
            Assert.That(ExpeditionRules.ContractForLeg(route, 1), Is.EqualTo("cedar-grove"));
            Assert.That(ExpeditionRules.ContractForLeg(route, -1), Is.Null);
            Assert.That(ExpeditionRules.ContractForLeg(route, 2), Is.Null);
        }

        [TestCase("cedar-grove")]
        [TestCase("Salt-Causeway")]
        [TestCase("")]
        [TestCase(null)]
        public void UnknownOpeningRoutesHaveNoLegs(string route)
        {
            Assert.That(ExpeditionRules.IsRoute(route), Is.False);
            Assert.That(ExpeditionRules.ContractForLeg(route, 0), Is.Null);
            Assert.That(ExpeditionRules.ContractForLeg(route, 1), Is.Null);
        }

        [TestCase("standard", true, 6, 0)]
        [TestCase("guard", true, 3, 1)]
        [TestCase("archer", true, 9, 0)]
        [TestCase("Guard", false, 6, 0)]
        [TestCase(null, false, 6, 0)]
        public void GearDefinesCapacityAndRecoveryBonus(string gear, bool known, int arrows, int recoveryBonus)
        {
            Assert.That(ExpeditionRules.IsGear(gear), Is.EqualTo(known));
            Assert.That(ExpeditionRules.ArrowCapacity(gear), Is.EqualTo(arrows));
            Assert.That(ExpeditionRules.PostureRecoveryBonus(gear), Is.EqualTo(recoveryBonus));
        }

        [TestCase("arrows", true)]
        [TestCase("posture", true)]
        [TestCase("Arrows", false)]
        [TestCase(null, false)]
        public void SupplyIdsAreExact(string supply, bool known) =>
            Assert.That(ExpeditionRules.IsSupply(supply), Is.EqualTo(known));

        [TestCase(1, 6, "arrows", 4)]
        [TestCase(2, 3, "arrows", 3)]
        [TestCase(2, 9, "arrows", 5)]
        [TestCase(9, 9, "arrows", 9)]
        [TestCase(2, 9, "posture", 2)]
        [TestCase(0, 6, "posture", 0)]
        [TestCase(2, 9, null, 2)]
        [TestCase(-2, 6, "arrows", 3)]
        [TestCase(12, 9, "posture", 9)]
        [TestCase(1, -1, "arrows", 0)]
        [TestCase(int.MaxValue - 1, int.MaxValue, "arrows", int.MaxValue)]
        public void CheckpointSupplyUsesCarriedArrows(int current, int capacity, string supply, int expected) =>
            Assert.That(ExpeditionRules.RestockArrows(current, capacity, supply), Is.EqualTo(expected));

        [Test]
        public void FirstClearFundsAnUnlockAndRepeatedClearsAwardNothing()
        {
            int balance = ExpeditionRules.AwardFirstClear(false);
            Assert.That(balance, Is.EqualTo(1));
            Assert.That(ExpeditionRules.TrySpend(balance, 1, false, out int remaining), Is.True);
            Assert.That(remaining, Is.Zero);
            Assert.That(ExpeditionRules.AwardFirstClear(true), Is.Zero);
        }

        [TestCase(5, 2, false, true, 3)]
        [TestCase(2, 2, false, true, 0)]
        [TestCase(1, 2, false, false, 1)]
        [TestCase(5, 2, true, false, 5)]
        [TestCase(5, 0, false, false, 5)]
        [TestCase(5, -1, false, false, 5)]
        public void OnlyAffordableNewUnlocksChangeTheBalance(
            int balance, int price, bool alreadyOwned, bool expected, int expectedBalance)
        {
            Assert.That(ExpeditionRules.CanUnlock(balance, price, alreadyOwned), Is.EqualTo(expected));
            Assert.That(ExpeditionRules.TrySpend(balance, price, alreadyOwned, out int remaining), Is.EqualTo(expected));
            Assert.That(remaining, Is.EqualTo(expectedBalance));
        }
    }
}
