using System.Collections;
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
    public sealed class BBadsFrameworkIntegrationTests
    {
        [UnityTest]
        public IEnumerator CurrentSourceRunsBattleRestartDefeatExitAndReentry()
        {
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.ExitScene);
            string failure = "BBads smoke did not report completion.";
            yield return BBadsSmokeProbe.Run(result => failure = result);
            Assert.That(failure, Is.Null);
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.ExitScene);
        }

        [UnityTest]
        public IEnumerator ShortenedScenarioCompletesOnceAndDoesNotReschedule()
        {
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
            var runner = Object.FindObjectOfType<ScenarioRunner>();
            var clock = Object.FindObjectOfType<BBadsBattleClock>();
            var shortened = ScriptableObject.CreateInstance<ScenarioDefinition>();
            var original = runner.Scenario;
            shortened.LoadRuntimeData(original.ScenarioId, new[] { original.EnemySpawns[0] },
                new[] { original.ItemSpawns[0] }, original.SummonPositions);
            int enemies = 0, items = 0, completions = 0;
            runner.EnemySpawnRequested += _ => enemies++;
            runner.ItemSpawnRequested += _ => items++;
            runner.Completed += () => completions++;
            try
            {
                runner.SetScenario(shortened);
                runner.Play();
                clock.SetPlaybackSpeed(2f);
                yield return BBadsTimingTests.Until(() => completions > 0);
                Assert.That(runner.IsPlaying, Is.False);
                double end = clock.Elapsed;
                yield return BBadsTimingTests.Until(() => clock.Elapsed > end + 0.2d);
                Assert.That((enemies, items, completions), Is.EqualTo((1, 1, 1)));
                Debug.Log("BBADS_TIMELINE enemies=1 items=1 completed=1");
            }
            finally { Object.Destroy(shortened); }
        }
    }
}
