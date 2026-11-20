using System.Numerics;
using NUnit.Framework;
using Test.Domain.Combat;

namespace Test.Domain.Tests
{
    public sealed class CombatRulesTests
    {
        private static readonly Strike TestStrike = new Strike(30, 4, 20, 18, 17, 2, 90);

        [Test]
        public void BowFlightArcsTowardTheAimPoint()
        {
            var origin = new Vector3(0, 1.34f, 0);
            var target = origin + Vector3.UnitZ * 12;
            var arrow = BowRules.Launch(origin, target, Vector3.UnitZ);
            float travelTime = (target.Z - origin.Z) / arrow.Velocity.Z;
            var apex = BowRules.Advance(arrow, travelTime / 2);
            var arrival = BowRules.Advance(apex, travelTime / 2);
            Assert.That(apex.Position.Y, Is.GreaterThan(target.Y));
            Assert.That(Vector3.Distance(arrival.Position, target), Is.LessThan(.0001f));
        }

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
