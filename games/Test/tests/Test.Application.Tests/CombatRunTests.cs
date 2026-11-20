using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Domain.Combat;

namespace Test.Application.Tests
{
    public sealed class CombatRunTests
    {
        [Test]
        public void OneSwingHitsOneTargetOnceEvenWithDuplicateContacts()
        {
            var run = new CombatRun(new Space());
            int hits = 0;
            run.Cue += cue => { if (cue.Attacker == 0) hits++; };
            run.Queue(CombatButtons.Attack);
            for (int tick = 0; tick < 30; tick++) Step(run);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(51));
        }

        [Test]
        public void ADeflectReleasesTheAttackerAndRetryClearsTheCheckpointState()
        {
            var run = new CombatRun(new Space());
            for (int tick = 0; tick < 71; tick++) Step(run);
            Assert.That(run.AttackOwner, Is.EqualTo(1));
            run.Queue(CombatButtons.Guard);
            for (int tick = 0; tick < 4; tick++) Step(run, true);
            Assert.That(run.AttackOwner, Is.Zero);
            Assert.That(run.View(1).Vitality.Posture, Is.EqualTo(36));
            run.Queue(CombatButtons.Lock | CombatButtons.Attack);
            run.Retry();
            Step(run);
            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(run.Locked, Is.Zero);
            Assert.That(run.View(1).Vitality.Posture, Is.Zero);
        }

        [Test]
        public void PauseClearsActionsAndFreezesTheTimeline()
        {
            var run = new CombatRun(new Space());
            Step(run);
            run.Queue(CombatButtons.Attack);
            run.TogglePause();
            for (int tick = 0; tick < 10; tick++) Step(run);
            Assert.That(run.Tick, Is.EqualTo(1));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Paused));
            run.TogglePause();
            Step(run);
            Assert.That(run.Tick, Is.EqualTo(2));
            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Ready));
        }

        [Test]
        public void ReplayFromTheSameSpaceProducesIdenticalSnapshots()
        {
            var first = new CombatRun(new Space());
            var second = new CombatRun(new Space());
            for (int index = 0; index < 200; index++)
            {
                foreach (var run in new[] { first, second })
                {
                    if (index % 41 == 0) run.Queue(CombatButtons.Attack);
                    if (index % 57 == 0) run.Queue(CombatButtons.Guard);
                    Step(run, index % 57 < 7);
                }
                Assert.That(first.Status, Is.EqualTo(second.Status));
                Assert.That(first.AttackOwner, Is.EqualTo(second.AttackOwner));
                Assert.That(first.View(0).Vitality, Is.EqualTo(second.View(0).Vitality));
                Assert.That(first.View(1).Vitality, Is.EqualTo(second.View(1).Vitality));
                Assert.That(first.View(1).Phase, Is.EqualTo(second.View(1).Phase));
            }
        }

        [Test]
        public void EachSegmentOfTheEliteComboCanHitOnce()
        {
            var space = new Space { EnemiesCanHit = false };
            var run = new CombatRun(space);
            ClearArea(run, space, 14);
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Courtyard));
            ClearArea(run, space, 36);
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Gate));

            var hits = new List<int>();
            run.Cue += cue =>
            {
                if (cue.Target == 0 && run.View(1).Segment >= 0) hits.Add(run.View(1).Segment);
            };
            for (int tick = 0; tick < 290 && run.View(1).Segment == 0; tick++) Step(run);
            Assert.That(run.View(1).Segment, Is.EqualTo(1));
            Assert.That(run.AttackOwner, Is.EqualTo(1));

            run.Retry();
            for (int tick = 0; tick < 222; tick++) Step(run);
            space.EnemiesCanHit = true;
            for (int tick = 0; tick < 100 && hits.Count < 2; tick++) Step(run);
            Assert.That(hits, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(run.View(0).Vitality.Health, Is.EqualTo(57));
        }

        [Test]
        public void CourtyardOnlyLetsOneEnemyAttackAtATime()
        {
            var space = new Space { EnemiesCanHit = false };
            var run = new CombatRun(space);
            ClearArea(run, space, 14);
            var owners = new HashSet<int>();
            for (int tick = 0; tick < 450; tick++)
            {
                Step(run);
                int activeAttacks = 0;
                for (int actor = 1; actor < run.Count; actor++)
                    if (run.View(actor).Phase != AttackPhase.Ready) activeAttacks++;
                Assert.That(activeAttacks, Is.EqualTo(run.AttackOwner == 0 ? 0 : 1));
                if (run.AttackOwner > 0) owners.Add(run.AttackOwner);
            }
            Assert.That(owners, Is.EquivalentTo(new[] { 1, 2, 3 }));
            run.Retry();
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Courtyard));
            Assert.That(run.AttackOwner, Is.Zero);
        }

        private static void ClearArea(CombatRun run, Space space, float exit)
        {
            for (int tick = 0; tick < 300 && run.Status == RunStatus.Fighting; tick++)
            {
                run.Queue(CombatButtons.Attack);
                Step(run);
            }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Proceed));
            space.PlayerDepth = exit;
            Step(run);
        }

        private static void Step(CombatRun run, bool guard = false) => run.Step(Vector3.Zero, Vector3.UnitZ, guard);

        private sealed class Space : ICombatSpace
        {
            private int count;
            public bool EnemiesCanHit { get; set; } = true;
            public float PlayerDepth { get; set; }

            public void Reset(Encounter encounter, int actorCount)
            {
                count = actorCount;
                PlayerDepth = 0;
            }

            public Vector3 Position(int actor) => actor == 0 ? new Vector3(0, 0, PlayerDepth) : new Vector3(0, 0, 2);
            public void Move(int actor, Vector3 displacement) { }

            public void Sweep(int actor, Vector3 facing, Strike strike, List<int> contacts)
            {
                if (actor != 0)
                {
                    if (EnemiesCanHit) { contacts.Add(0); contacts.Add(0); }
                    return;
                }
                for (int target = 1; target < count; target++)
                {
                    contacts.Add(target);
                    contacts.Add(target);
                }
            }
        }
    }
}
