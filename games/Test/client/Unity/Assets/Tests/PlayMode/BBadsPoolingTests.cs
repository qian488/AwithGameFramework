using System.Collections;
using BBads.Combat;
using BBads.Data;
using BBads.Infrastructure;
using BBads.Timeline;
using NUnit.Framework;
using Test.Client.BBads;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    public sealed class BBadsPoolingTests
    {
        [UnityTest]
        public IEnumerator ThreeBattleRunsReleaseTheirUnitsAndProjectiles()
        {
            int previousRate = Time.captureFramerate;
            Time.captureFramerate = 60;
            try
            {
                for (int round = 1; round <= 3; round++)
                {
                    yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
                    var runner = Object.FindObjectOfType<ScenarioRunner>();
                    var spawner = Object.FindObjectOfType<CombatSpawner>();
                    var pool = Object.FindObjectOfType<CombatPresentationPool>();
                    Object.FindObjectOfType<BBadsBattleClock>().SetPlaybackSpeed(2f);
                    yield return BBadsTimingTests.Until(() => runner.Elapsed >= 16f);
                    var units = spawner.UnitPoolCounts;
                    var projectiles = pool.ProjectileCounts;
                    Assert.That(units.Rents, Is.GreaterThanOrEqualTo(8));
                    Assert.That(units.Created, Is.LessThanOrEqualTo(units.Rents));
                    Assert.That(units.Active + units.Idle, Is.EqualTo(units.Created));
                    Assert.That(projectiles.Rents, Is.GreaterThan(0));
                    Assert.That(projectiles.Active + projectiles.Idle, Is.EqualTo(projectiles.Created));
                    Debug.Log($"BBADS_POOL_ROUND round={round} units={units} projectiles={projectiles}");

                    yield return SceneManager.LoadSceneAsync(BBadsSceneActions.ExitScene);
                    Assert.That(spawner == null, Is.True);
                    Assert.That(pool == null, Is.True);
                    Assert.That(Object.FindObjectsOfType<CombatActor>(true), Is.Empty);
                    Assert.That(Object.FindObjectsOfType<CombatProjectile>(true), Is.Empty);
                }
            }
            finally { Time.captureFramerate = previousRate; }
        }

        [UnityTest]
        public IEnumerator ProjectileCannotHitAReplacementThatReusesTheTargetObject()
        {
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
            var pool = Object.FindObjectOfType<CombatPresentationPool>();
            var world = Object.FindObjectOfType<CombatWorld>();
            var data = Object.FindObjectOfType<CsvRuntimeData>();
            Assert.That(data.Units.TryGet("EnemySkeletonArcher", out var definition), Is.True);
            Assert.That(data.Units.TryGet("CatGangster1", out var source), Is.True);
            var prefab = Resources.Load<GameObject>("BBads/Unit");
            Assert.That(prefab, Is.Not.Null);
            var root = new GameObject("BBads pool test root");
            var actors = new PrefabPool(prefab, root.transform);
            try
            {
                var original = actors.Get();
                var originalActor = original.GetComponent<CombatActor>();
                originalActor.Initialize(definition, null, null);
                original.transform.position = new Vector3(0.6f, 0f, 0f);
                Assert.That(pool.LaunchProjectile(source, source.AttackProfile, world, originalActor, Vector2.zero), Is.True);
                var shot = Object.FindObjectOfType<CombatProjectile>();

                actors.Release(original);
                actors.Release(original);
                var replacement = actors.Get();
                Assert.That(replacement, Is.SameAs(original));
                var replacementActor = replacement.GetComponent<CombatActor>();
                replacementActor.Initialize(definition, null, null);
                yield return BBadsTimingTests.Until(() => !shot.gameObject.activeSelf);
                Assert.That(replacementActor.CurrentHealth, Is.EqualTo(definition.maxHealth));
                Assert.That(shot.GetComponent<SpriteRenderer>().sprite, Is.Null);

                Assert.That(pool.LaunchProjectile(source, source.AttackProfile, world, replacementActor, Vector2.zero), Is.True);
                Assert.That(Object.FindObjectOfType<CombatProjectile>(), Is.SameAs(shot));
                yield return BBadsTimingTests.Until(() => !shot.gameObject.activeSelf);
                Assert.That(replacementActor.CurrentHealth, Is.EqualTo(definition.maxHealth - source.attack).Within(0.001f));
                actors.Release(replacement);

                var counts = actors.Counts;
                Assert.That(counts, Is.EqualTo((1, 2, 2, 0, 1, 1)));
                Assert.That(pool.ProjectileCounts.Rents, Is.EqualTo(2));
                Assert.That(pool.ProjectileCounts.Returns, Is.EqualTo(2));
                Debug.Log($"BBADS_POOL unit={counts} projectile={pool.ProjectileCounts}");
            }
            finally { Object.Destroy(root); }
        }
    }
}
