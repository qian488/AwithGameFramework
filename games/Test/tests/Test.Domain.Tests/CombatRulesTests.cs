using NUnit.Framework;
using Test.Domain.Combat;

namespace Test.Domain.Tests
{
    public sealed class CombatRulesTests
    {
        private static readonly Strike TestStrike = new Strike(30, 4, 20, 18, 17, 2, 90);

        [TestCase(0, ContactResult.Deflect)]
        [TestCase(7, ContactResult.Deflect)]
        [TestCase(8, ContactResult.Guard)]
        [TestCase(-1, ContactResult.Guard)]
        public void DeflectionUsesAnEightTickHalfOpenWindow(int age, ContactResult expected)
        {
            var outcome = CombatRules.Resolve(new Vitality(100), new Vitality(100), TestStrike, 30, 30 - age, true, true, false, 100, 100);
            Assert.That(outcome.Result, Is.EqualTo(expected));
            Assert.That(outcome.Defender.Health, Is.EqualTo(100));
        }

        [Test]
        public void PostureBreakHasAFiniteFinishWindow()
        {
            var outcome = CombatRules.Resolve(new Vitality(100, 68), new Vitality(100), TestStrike, 30, 30, true, true, false, 100, 100);
            int end = outcome.Attacker.BrokenUntil;
            Assert.That(outcome.Attacker.Broken(30), Is.True);
            Assert.That(CombatRules.Finish(outcome.Attacker, end - 1, 2, 2.1f).Alive, Is.False);
            Assert.That(CombatRules.Finish(outcome.Attacker, end - 1, 2.2f, 2.1f).Alive, Is.True);
            Assert.That(CombatRules.Finish(outcome.Attacker, end, 2, 2.1f).Alive, Is.True);
            Assert.That(CombatRules.Recover(outcome.Attacker, end, true, 100).Posture, Is.EqualTo(33));
        }

        [TestCase(29, AttackPhase.Windup)]
        [TestCase(30, AttackPhase.Active)]
        [TestCase(33, AttackPhase.Active)]
        [TestCase(34, AttackPhase.Recovery)]
        [TestCase(54, AttackPhase.Ready)]
        public void StrikeTransitionsAtExactTicks(int step, AttackPhase phase) => Assert.That(TestStrike.At(step), Is.EqualTo(phase));
    }
}
