using System;
using System.Collections;
using System.Collections.Generic;
using BBads.Backpack;
using BBads.Combat;
using BBads.Data;
using BBads.Timeline;
using NUnit.Framework;
using Test.Client.BBads;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    public sealed class BBadsTimingTests
    {
        [UnityTest]
        public IEnumerator PausingBattleFreezesWaveChargeAndInFlightHitUntilResume()
        {
            int previousRate = Time.captureFramerate;
            Time.captureFramerate = 60;
            try
            {
                yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
                var clock = Object.FindObjectOfType<BBadsBattleClock>();
                var runner = Object.FindObjectOfType<ScenarioRunner>();
                var backpack = Object.FindObjectOfType<BackpackController>();
                var world = Object.FindObjectOfType<CombatWorld>();
                var pool = Object.FindObjectOfType<CombatPresentationPool>();
                var data = Object.FindObjectOfType<CsvRuntimeData>();
                Assert.That(backpack.TryPlaceNew("Fish1", new Vector2Int(3, 2), out var fish), Is.True);
                Assert.That(backpack.TryPlaceNew("CatRocket1", new Vector2Int(4, 1), out var cat), Is.True);
                var events = new List<string>();
                backpack.FishCycleCompleted += _ => events.Add("fish");
                runner.EnemySpawnRequested += _ => events.Add("wave");
                float previousHealth = world.PlayerBase.CurrentHealth;
                world.PlayerBase.HealthChanged += (current, _) =>
                {
                    if (current < previousHealth)
                        events.Add("hit");
                    previousHealth = current;
                };
                using var pauseObserver = clock.Host.Events.Subscribe<BBadsBattleClock.BattlePlaybackChanged>(
                    change => events.Add(change.IsPaused ? "paused" : "resumed"));

                yield return Until(() => runner.Elapsed >= 2.4f);
                Assert.That(data.Units.TryGet("EnemySkeletonArcher", out var archer), Is.True);
                Vector2 start = world.PlayerBase.Position + Vector2.right * 2.9f;
                Assert.That(pool.LaunchProjectile(archer, archer.AttackProfile, world, world.PlayerBase, start), Is.True);
                var projectile = Object.FindObjectOfType<CombatProjectile>();
                Vector3 position = projectile.transform.position;
                float waveElapsed = runner.Elapsed;
                float fishCooldown = fish.CooldownRemaining;
                float catCharge = cat.ActivationProgress;
                float health = world.PlayerBase.CurrentHealth;
                clock.SetPlaybackSpeed(0f);
                for (int frame = 0; frame < 45; frame++)
                    yield return null;

                Assert.That(runner.Elapsed, Is.EqualTo(waveElapsed));
                Assert.That(fish.CooldownRemaining, Is.EqualTo(fishCooldown));
                Assert.That(cat.ActivationProgress, Is.EqualTo(catCharge));
                Assert.That(projectile.transform.position, Is.EqualTo(position));
                Assert.That(world.PlayerBase.CurrentHealth, Is.EqualTo(health));
                Assert.That(events, Is.EqualTo(new[] { "paused" }));
                Assert.That(Time.timeScale, Is.EqualTo(1f));

                clock.SetPlaybackSpeed(1f);
                yield return Until(() => runner.Elapsed >= 3.15f);
                Assert.That(events, Is.EqualTo(new[] { "paused", "resumed", "fish", "hit", "wave" }));
                Assert.That(cat.ActivationProgress, Is.GreaterThan(catCharge));
                Assert.That(pool.ProjectileCounts.Returns, Is.EqualTo(1));

                int callbacks = 0;
                var previousHost = clock.Host;
                previousHost.Timers.Schedule(0.1d, () => callbacks++);
                clock.SetPlaybackSpeed(0f);
                Assert.That(BBadsSceneActions.TryExit(), Is.True);
                yield return Until(() => !BBadsSceneActions.IsTransitioning &&
                    SceneManager.GetActiveScene().path == BBadsSceneActions.ExitScene);
                Assert.That(previousHost.Timers.IsDisposed, Is.True);
                Assert.That(previousHost.Events.SubscriptionCount, Is.Zero);
                Assert.That(BBadsSceneActions.TryEnterBattle(), Is.True);
                yield return Until(() => !BBadsSceneActions.IsTransitioning &&
                    SceneManager.GetActiveScene().path == BBadsSceneActions.BattleScene);
                var newClock = Object.FindObjectOfType<BBadsBattleClock>();
                Assert.That(newClock.Host.Id, Is.Not.EqualTo(previousHost.Id));
                yield return Until(() => newClock.Elapsed >= 0.2d);
                Assert.That(callbacks, Is.Zero);
                Debug.Log("BBADS_TIMING paused,resumed,fish,hit,wave; oldTimerCallbacks=0");
            }
            finally { Time.captureFramerate = previousRate; }
        }

        internal static IEnumerator Until(Func<bool> condition)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for BBads state.");
        }
    }
}
