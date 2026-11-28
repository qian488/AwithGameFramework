using NUnit.Framework;
using Test.Domain.BBads;

namespace Test.Domain.Tests.BBads
{
    [TestFixture]
    public sealed class BBadsCombatRulesTests
    {
        [TestCase(10f, 3f, 7f, false)]
        [TestCase(10f, 10f, 0f, true)]
        [TestCase(10f, 12f, 0f, true)]
        [TestCase(10f, 0f, 10f, false)]
        [TestCase(10f, -3f, 10f, false)]
        [TestCase(0f, 10f, 0f, false)]
        public void DamageClampsHealthAndReportsOnlyTheHitThatDefeatsTheTarget(
            float health, float amount, float expectedHealth, bool expectedDefeat)
        {
            var result = BBadsCombatRules.ApplyDamage(health, amount);

            Assert.That(result.RemainingHealth, Is.EqualTo(expectedHealth));
            Assert.That(result.Defeated, Is.EqualTo(expectedDefeat));
        }
    }
}