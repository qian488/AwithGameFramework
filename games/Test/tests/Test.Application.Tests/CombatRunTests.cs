using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
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
            run.Cue += cue =>
            {
                if (cue.Attacker != 0) return;
                Assert.That(cue.IsArrow, Is.False);
                hits++;
            };
            run.Queue(CombatButtons.Attack);
            for (int tick = 0; tick < 30; tick++) Step(run);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(51));
        }

        [Test]
        public void ReleasingLeftMouseEarlySwingsInsteadOfFinishing()
        {
            var run = CreateFinishOpportunity(out _);
            run.Queue(CombatButtons.AttackPressed);
            Step(run);
            for (int tick = 1; tick < CombatRun.FinishHoldTicks; tick++) Step(run);
            run.Queue(CombatButtons.AttackReleased);
            Step(run);

            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(run.View(1).Vitality.Alive, Is.True);
            Assert.That(run.IsHoldingFinish, Is.False);
        }

        [Test]
        public void HoldingLeftMouseFinishesOnceAndReleasingDoesNotSwing()
        {
            var run = CreateFinishOpportunity(out _);
            int finishes = 0;
            run.Cue += cue => { if (cue.Result == ContactResult.Finish) finishes++; };
            run.Queue(CombatButtons.AttackPressed);
            Step(run);
            for (int tick = 1; tick < CombatRun.FinishHoldTicks; tick++) Step(run);
            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(run.View(1).Vitality.Alive, Is.True);
            Assert.That(run.FinishHoldProgress, Is.GreaterThan(0).And.LessThan(1));

            Step(run);
            Assert.That(run.View(1).Vitality.Alive, Is.False);
            Assert.That(run.IsHoldingFinish, Is.False);
            run.Queue(CombatButtons.AttackReleased);
            for (int tick = 0; tick < 30; tick++)
            {
                Step(run);
                Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Ready));
            }
            Assert.That(finishes, Is.EqualTo(1));
        }

        [TestCase("pause")]
        [TestCase("retry")]
        [TestCase("range")]
        public void InterruptedFinishHoldDoesNotConvertToAnAttack(string interruption)
        {
            var run = CreateFinishOpportunity(out var space);
            run.Queue(CombatButtons.AttackPressed);
            Step(run);
            Assert.That(run.IsHoldingFinish, Is.True);

            if (interruption == "pause")
            {
                run.TogglePause();
                run.TogglePause();
            }
            else if (interruption == "retry") run.Retry();
            else
            {
                space.PlayerDepth = -1;
                Step(run);
                space.PlayerDepth = 0;
            }
            for (int tick = 0; tick <= CombatRun.FinishHoldTicks; tick++) Step(run);
            run.Queue(CombatButtons.AttackReleased);
            Step(run);

            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(run.View(1).Vitality.Alive, Is.True);
            Assert.That(run.IsHoldingFinish, Is.False);
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

        [Test]
        public void BowDamagesOnlyOnArrivalAndSpendsOneArrow()
        {
            var space = new Space { EnemiesCanHit = false };
            var run = new CombatRun(space);
            var contacts = new List<CombatCue>();
            run.Cue += contacts.Add;
            int startingHealth = run.View(1).Vitality.Health;
            run.Queue(CombatButtons.EquipBow | CombatButtons.AttackPressed);
            Step(run);
            for (int tick = 1; tick < BowRules.Shot.Windup; tick++) Step(run);
            Assert.That(run.ArrowsRemaining, Is.EqualTo(BowRules.QuiverCapacity));
            Step(run);
            Assert.That(run.AirborneArrows, Has.Count.EqualTo(1));
            Assert.That(run.ArrowsRemaining, Is.EqualTo(BowRules.QuiverCapacity - 1));
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(startingHealth));
            Assert.That(contacts, Is.Empty);

            space.ArrowHit = 1;
            Step(run);
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(startingHealth - BowRules.Shot.Damage));
            Assert.That(contacts, Has.Count.EqualTo(1));
            Assert.That(contacts[0].IsArrow, Is.True);
            Assert.That(run.AirborneArrows, Is.Empty);
            for (int tick = 0; tick < BowRules.Shot.Recovery; tick++) Step(run);
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(startingHealth - BowRules.Shot.Damage));
        }

        [Test]
        public void BowCannotGuardOrShootThroughCoverAndRetryRestoresTheLoadout()
        {
            var space = new Space { EnemiesCanHit = false, ArrowHit = 0 };
            var run = new CombatRun(space);
            run.Queue(CombatButtons.EquipBow | CombatButtons.Guard);
            Step(run, guard: true);
            Assert.That(run.View(0).Guarding, Is.False);
            int startingHealth = run.View(1).Vitality.Health;
            for (int shot = 0; shot <= BowRules.QuiverCapacity; shot++)
            {
                run.Queue(CombatButtons.Attack);
                for (int tick = 0; tick <= BowRules.Shot.Duration; tick++) Step(run);
            }
            Assert.That(run.ArrowsRemaining, Is.Zero);
            Assert.That(space.ArrowTraces, Is.EqualTo(BowRules.QuiverCapacity));
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(startingHealth));
            Assert.That(run.AirborneArrows, Is.Empty);
            run.Queue(CombatButtons.EquipSword | CombatButtons.Guard);
            Step(run, guard: true);
            Assert.That(run.View(0).Guarding, Is.True);
            run.Retry();
            Assert.That(run.Weapon, Is.EqualTo(PlayerWeapon.Sword));
            Assert.That(run.ArrowsRemaining, Is.EqualTo(BowRules.QuiverCapacity));
        }

        [Test]
        public async Task TrialCatalogAcceptsAnotherOpponentFromConfigurationAndNamesInvalidFields()
        {
            var shipped = await TrialCatalog.LoadAsync();
            Assert.That(shipped.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(shipped[0].Strike.Windup, Is.GreaterThan(shipped[1].Strike.Windup));
            Assert.That(shipped[0].Strike.Damage, Is.GreaterThan(shipped[1].Strike.Damage));
            Assert.That(shipped[0].AttackIntervalTicks, Is.GreaterThan(shipped[1].AttackIntervalTicks));

            using var source = typeof(TrialCatalog).Assembly.GetManifestResourceStream("Test.RainPass.Opponents.json");
            var document = JsonNode.Parse(source);
            var rows = document["rows"].AsArray();
            var opponent = rows[0].DeepClone();
            int id = shipped.Max(item => item.Id) + 1;
            opponent["id"] = id;
            opponent["name"] = "Dusk Scout";
            rows.Add(opponent);
            var expanded = await TrialCatalog.LoadAsync(Encoding.UTF8.GetBytes(document.ToJsonString()));
            Assert.That(expanded.Count, Is.EqualTo(shipped.Count + 1));
            Assert.That(expanded.Single(item => item.Id == id).Name, Is.EqualTo("Dusk Scout"));

            opponent["attackIntervalTicks"] = 0;
            var error = Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await TrialCatalog.LoadAsync(Encoding.UTF8.GetBytes(document.ToJsonString())));
            Assert.That(error.Message, Does.Contain($"opponent ID {id}").And.Contain("attackIntervalTicks"));
        }

        [Test]
        public async Task TrialRecordsSurviveRetryDefeatSwitchAndReopen()
        {
            var opponents = await TrialCatalog.LoadAsync();
            string directory = Path.Combine(Path.GetTempPath(), "Test-RainPass-" + Guid.NewGuid().ToString("N"));
            try
            {
                var space = new Space { EnemiesCanHit = false };
                var run = new CombatRun(space);
                var session = new TrialSession(run, directory);
                await session.OpenAsync();
                session.Start(opponents[0]);
                Assert.That(session.ElapsedTicks, Is.Zero);
                Assert.That(run.View(1).HealthLimit, Is.EqualTo(opponents[0].Health));
                Assert.That(TrialStep(session), Is.Null);
                run.TogglePause();
                Assert.That(TrialStep(session), Is.Null);
                Assert.That(session.ElapsedTicks, Is.EqualTo(1));
                run.TogglePause();
                await FinishTrial(run, session);
                int firstBest = session.BestTicks(opponents[0].Id).Value;
                Assert.That(session.NewRecord, Is.True);
                Assert.That(TrialStep(session), Is.Null);
                Assert.That(session.ElapsedTicks, Is.EqualTo(firstBest));

                session.Start(opponents[0]);
                Assert.That(run.View(0).Vitality.Health, Is.EqualTo(100));
                Assert.That(run.View(1).Vitality.Health, Is.EqualTo(opponents[0].Health));
                Assert.That(run.AttackOwner, Is.Zero);
                Assert.That(run.Locked, Is.Zero);
                for (int tick = 0; tick < 80; tick++) Assert.That(TrialStep(session), Is.Null);
                await FinishTrial(run, session);
                Assert.That(session.NewRecord, Is.False);
                Assert.That(session.BestTicks(opponents[0].Id), Is.EqualTo(firstBest));

                space.EnemiesCanHit = true;
                session.Start(opponents[0]);
                for (int tick = 0; tick < 1800 && session.Status == RunStatus.Fighting; tick++) Assert.That(TrialStep(session), Is.Null);
                Assert.That(session.Status, Is.EqualTo(RunStatus.Defeated));
                Assert.That(session.BestTicks(opponents[0].Id), Is.EqualTo(firstBest));

                space.EnemiesCanHit = false;
                session.Start(opponents[1]);
                Assert.That(session.ElapsedTicks, Is.Zero);
                Assert.That(run.View(1).HealthLimit, Is.EqualTo(opponents[1].Health));
                Assert.That(session.BestTicks(opponents[1].Id), Is.Null);
                await FinishTrial(run, session);
                int secondBest = session.BestTicks(opponents[1].Id).Value;
                var reopened = new TrialSession(new CombatRun(new Space()), directory);
                await reopened.OpenAsync();
                Assert.That(reopened.BestTicks(opponents[0].Id), Is.EqualTo(firstBest));
                Assert.That(reopened.BestTicks(opponents[1].Id), Is.EqualTo(secondBest));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }

        private static async Task FinishTrial(CombatRun run, TrialSession session)
        {
            for (int tick = 0; tick < 700 && session.Status == RunStatus.Fighting; tick++)
            {
                run.Queue(CombatButtons.Attack);
                var save = TrialStep(session);
                if (save != null) await save;
            }
            Assert.That(session.Status, Is.EqualTo(RunStatus.Victory));
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Practice));
        }

        private static Task TrialStep(TrialSession session) => session.Step(Vector3.Zero, Vector3.UnitZ, false);

        [TestCase(CombatMapId.CedarGrove, 3, EnemyWeapon.ShortSword, EnemyWeapon.TwinBlades)]
        [TestCase(CombatMapId.SaltCauseway, 2, EnemyWeapon.Spear, EnemyWeapon.Poleaxe)]
        [TestCase(CombatMapId.EmberFoundry, 2, EnemyWeapon.ShieldMace, EnemyWeapon.Maul)]
        public void ARouteHasItsOwnPatrolBossAndCheckpoint(CombatMapId id, int guards, EnemyWeapon patrol, EnemyWeapon boss)
        {
            var map = CombatMap.Routes.Single(route => route.Id == id);
            var space = new Space { EnemiesCanHit = false };
            var run = new CombatRun(space, map);
            Assert.That(run.Count, Is.EqualTo(guards + 1));
            Assert.That(run.Map.Patrol.Weapon, Is.EqualTo(patrol));
            Assert.That(run.Map.Boss.Weapon, Is.EqualTo(boss));

            space.PlayerDepth = map.GateDepth + 2;
            Step(run);
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Courtyard));
            space.PlayerDepth = 0;
            ClearArea(run, space, map.GateDepth + 1.5f);
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Gate));
            Assert.That(run.Count, Is.EqualTo(2));
            Assert.That(run.View(1).HealthLimit, Is.GreaterThan(110));
            Step(run);
            run.Retry();
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Gate));
            Assert.That(run.Tick, Is.Zero);
            Assert.That(run.Map, Is.SameAs(map));
            run.Restart();
            Assert.That(run.Encounter, Is.EqualTo(Encounter.Courtyard));
        }

        [Test]
        public void SaltLancerStartsAThrustOutsideGroveScoutsAttackRange()
        {
            var groveSpace = new Space { EnemiesCanHit = false };
            var saltSpace = new Space { EnemiesCanHit = false };
            var grove = new CombatRun(groveSpace, CombatMap.Routes.Single(route => route.Id == CombatMapId.CedarGrove));
            var salt = new CombatRun(saltSpace, CombatMap.Routes.Single(route => route.Id == CombatMapId.SaltCauseway));
            groveSpace.PlayerDepth = saltSpace.PlayerDepth = -1.1f;
            for (int tick = 0; tick < 46; tick++)
            {
                Step(grove);
                Step(salt);
            }
            Assert.That(grove.AttackOwner, Is.Zero);
            Assert.That(salt.AttackOwner, Is.EqualTo(1));
            Assert.That(salt.View(1).AttackArc, Is.LessThan(60));
        }

        [Test]
        public void FoundryPatrolGuardsWhileIdleAndDropsItsGuardToAttack()
        {
            var run = new CombatRun(new Space { EnemiesCanHit = false }, CombatMap.Routes.Single(route => route.Id == CombatMapId.EmberFoundry));
            int health = run.View(1).Vitality.Health;
            run.Queue(CombatButtons.AttackPressed);
            for (int tick = 0; tick < 16; tick++) Step(run);
            Assert.That(run.View(1).Guarding, Is.True);
            Assert.That(run.View(1).Vitality.Health, Is.EqualTo(health));
            Assert.That(run.View(1).Vitality.Posture, Is.GreaterThan(0));
            for (int tick = 0; tick < 30; tick++) Step(run);
            Assert.That(run.View(1).Phase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(run.View(1).Guarding, Is.False);
        }

        private static CombatRun CreateFinishOpportunity(out Space space)
        {
            space = new Space { EnemiesCanHit = false };
            var run = new CombatRun(space);
            run.StartTrial(new TrialOpponent(1, "Practice", "", 65, 15, 60, new Strike(32, 4, 27, 18, 18, 2.15f, 90)));
            run.Queue(CombatButtons.AttackPressed);
            Step(run);
            Assert.That(run.View(0).Phase, Is.EqualTo(AttackPhase.Windup));
            for (int tick = 0; tick < 40 && !run.CanFinish; tick++) Step(run);
            Assert.That(run.CanFinish, Is.True);
            return run;
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
            public int? ArrowHit { get; set; }
            public int ArrowTraces { get; private set; }
            public float PlayerDepth { get; set; }

            public void Reset(Encounter encounter, int actorCount)
            {
                count = actorCount;
                PlayerDepth = 0;
            }

            public Vector3 Position(int actor) => actor == 0 ? new Vector3(0, 0, PlayerDepth) : new Vector3(0, 0, 2);
            public void Move(int actor, Vector3 displacement) { }

            public bool TraceArrow(Vector3 from, Vector3 to, out int target)
            {
                ArrowTraces++;
                target = ArrowHit.GetValueOrDefault();
                return ArrowHit.HasValue;
            }

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
