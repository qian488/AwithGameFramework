using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using BBads.Combat;
using BBads.Timeline;
using NUnit.Framework;
using Test.Client.BBads;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Test.Client.Tests
{
    public sealed class BBadsHostTransitionTests
    {
        [UnityTearDown]
        public IEnumerator ResetTransition()
        {
            yield return WaitForTransition();
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.ExitScene, LoadSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator EntryRestartExitAndReentryLoadOnceAndRetireOldHosts()
        {
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.ExitScene, LoadSceneMode.Single);
            Scene sample = SceneManager.GetActiveScene();
            var loads = new List<Scene>();
            void RecordLoad(Scene scene, LoadSceneMode mode)
            {
                if (mode == LoadSceneMode.Additive)
                    loads.Add(scene);
            }

            SceneManager.sceneLoaded += RecordLoad;
            try
            {
                DoubleClick(sample, BBadsSceneActions.EnterButton);
                yield return WaitForTransition();

                Scene first = AssertDestination(sample, BBadsSceneActions.BattleScene, loads, 1);
                GameHost firstHost = AssertNavigationHost(first);
                GameHost firstBattle = GetRunningBattleHost(first);
                var firstRunner = BBadsSceneActions.FindInScene<ScenarioRunner>(first);
                int firstCallbacks = 0;
                int battleSubscriptions = firstBattle.Events.SubscriptionCount;
                using var firstSubscription = firstBattle.Events.Subscribe<int>(_ => firstCallbacks++);
                Assert.That(firstBattle.Events.SubscriptionCount, Is.EqualTo(battleSubscriptions + 1));
                Assert.That(firstBattle.Events.Publish(1).InvocationCount, Is.EqualTo(1));

                // The public service reuses an existing key even in Single mode.
                var firstScenes = firstHost.Services.Get<SceneService>();
                SceneHandle firstHandle = firstScenes.Active;
                var sameKey = firstScenes.LoadAsync(BBadsSceneActions.BattleScene);
                Assert.That(sameKey.Status, Is.EqualTo(TaskStatus.RanToCompletion), sameKey.Exception?.ToString());
                Assert.That(sameKey.Result, Is.SameAs(firstHandle));
                Assert.That(loads, Has.Count.EqualTo(1));

                DoubleClick(first, BBadsSceneActions.RestartButton);
                yield return WaitForTransition();

                Scene second = AssertDestination(first, BBadsSceneActions.BattleScene, loads, 2);
                Assert.That(firstRunner == null, Is.True, "The original Unity runner must be destroyed.");
                AssertRetired(first, firstHost);
                AssertStopped(firstBattle);
                Assert.That(firstScenes.Loaded, Is.Empty);
                GameHost secondHost = AssertNavigationHost(second);
                Assert.That(secondHost, Is.Not.SameAs(firstHost));
                GameHost secondBattle = GetRunningBattleHost(second);
                Assert.That(secondBattle, Is.Not.SameAs(firstBattle));
                Assert.That(secondBattle.Events.SubscriptionCount, Is.EqualTo(battleSubscriptions));
                Assert.That(secondBattle.Events.Publish(2).InvocationCount, Is.Zero);
                Assert.That(firstCallbacks, Is.EqualTo(1));
                using var secondSubscription = secondBattle.Events.Subscribe<int>(_ => { });

                DoubleClick(second, BBadsSceneActions.ExitButton);
                yield return WaitForTransition();

                Scene returned = AssertDestination(second, BBadsSceneActions.ExitScene, loads, 3);
                AssertRetired(second, secondHost);
                AssertStopped(secondBattle);
                Assert.That(BBadsSceneActions.FindInScene<ScenarioRunner>(returned), Is.Null);
                GameHost returnedHost = AssertNavigationHost(returned);

                Click(returned, BBadsSceneActions.EnterButton);
                yield return WaitForTransition();

                Scene third = AssertDestination(returned, BBadsSceneActions.BattleScene, loads, 4);
                AssertRetired(returned, returnedHost);
                Assert.That(AssertNavigationHost(third).Events.SubscriptionCount, Is.Zero);
                Assert.That(GetRunningBattleHost(third).Events.SubscriptionCount, Is.EqualTo(battleSubscriptions));
                Assert.That(BBadsSceneActions.FindInScene<ScenarioRunner>(third), Is.Not.Null);
                TestContext.WriteLine($"Native loads: {loads.Count}; battle subscriptions per new session: {battleSubscriptions}.");
            }
            finally
            {
                SceneManager.sceneLoaded -= RecordLoad;
            }
        }

        [UnityTest]
        public IEnumerator FailedRestartUnloadsTheCandidateAndTheSameButtonCanRetry()
        {
            // Opening the battle directly is a supported editor entry.
            yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene, LoadSceneMode.Single);
            Scene source = SceneManager.GetActiveScene();
            var sourceRunner = BBadsSceneActions.FindInScene<ScenarioRunner>(source);
            var sourceInput = BBadsSceneActions.FindInScene<EventSystem>(source);
            var sourceListener = BBadsSceneActions.FindInScene<AudioListener>(source);
            GameHost sourceBattle = GetRunningBattleHost(source);
            int sourceCallbacks = 0;
            using var sourceSubscription = sourceBattle.Events.Subscribe<int>(_ => sourceCallbacks++);
            var loads = new List<Scene>();
            void RecordLoad(Scene scene, LoadSceneMode mode)
            {
                if (mode == LoadSceneMode.Additive && scene.path == BBadsSceneActions.BattleScene)
                {
                    Assert.That(sourceInput.enabled, Is.False);
                    Assert.That(sourceListener.enabled, Is.False);
                    loads.Add(scene);
                }
            }

            SceneManager.sceneLoaded += RecordLoad;
            var originalFactory = BBadsSceneActions.CreateSceneBackend;
            try
            {
                var injected = new FailAfterActivationBackend(originalFactory());
                BBadsSceneActions.CreateSceneBackend = () => injected;
                LogAssert.Expect(LogType.Error,
                    new Regex("BBads transition to .* failed: .*BBads injected activation failure"));
                Click(source, BBadsSceneActions.RestartButton);
                yield return WaitForTransition();

                Assert.That(injected.PartiallyActivatedBattle, Is.Not.Null);
                AssertStopped(injected.PartiallyActivatedBattle);
                Assert.That(loads, Has.Count.EqualTo(1), "The failure must follow a genuine Unity load.");
                Assert.That(loads[0].isLoaded, Is.False, "Rollback must unload the partially activated candidate.");
                Assert.That(BBadsSceneActions.GetNavigationHost(loads[0]), Is.Null);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(source));
                Assert.That(source.isLoaded, Is.True);
                Assert.That(sourceInput.isActiveAndEnabled, Is.True);
                Assert.That(sourceListener.isActiveAndEnabled, Is.True);
                Assert.That(BBadsSceneActions.FindInScene<ScenarioRunner>(source), Is.SameAs(sourceRunner));
                Assert.That(GetRunningBattleHost(source), Is.SameAs(sourceBattle));
                Assert.That(sourceBattle.Events.Publish(1).InvocationCount, Is.EqualTo(1));
                Assert.That(sourceCallbacks, Is.EqualTo(1));

                BBadsSceneActions.CreateSceneBackend = originalFactory;
                Click(source, BBadsSceneActions.RestartButton);
                yield return WaitForTransition();

                Scene retry = AssertDestination(source, BBadsSceneActions.BattleScene, loads, 2);
                Assert.That(BBadsSceneActions.FindInScene<ScenarioRunner>(retry), Is.Not.Null);
                Assert.That(sourceRunner == null, Is.True);
                AssertStopped(sourceBattle);
                Assert.That(GetRunningBattleHost(retry).Events.Publish(1).InvocationCount, Is.Zero);
                Assert.That(AssertNavigationHost(retry).Events.SubscriptionCount, Is.Zero);
                TestContext.WriteLine($"Native loads across rollback and retry: {loads.Count}.");
            }
            finally
            {
                BBadsSceneActions.CreateSceneBackend = originalFactory;
                SceneManager.sceneLoaded -= RecordLoad;
            }
        }

        static IEnumerator WaitForTransition()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (BBadsSceneActions.IsTransitioning && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(BBadsSceneActions.IsTransitioning, Is.False, "BBads transition timed out.");
        }

        static Scene AssertDestination(Scene source, string path, List<Scene> loads, int totalLoads)
        {
            Scene destination = SceneManager.GetActiveScene();
            Assert.That(destination.path, Is.EqualTo(path));
            Assert.That(destination.handle, Is.Not.EqualTo(source.handle));
            Assert.That(source.isLoaded, Is.False);
            Assert.That(loads, Has.Count.EqualTo(totalLoads));
            Assert.That(loads[totalLoads - 1], Is.EqualTo(destination));
            return destination;
        }

        static GameHost AssertNavigationHost(Scene scene)
        {
            GameHost host = BBadsSceneActions.GetNavigationHost(scene);
            Assert.That(host, Is.Not.Null);
            Assert.That(host.State, Is.EqualTo(GameHostState.Running));
            var service = host.Services.Get<SceneService>();
            Assert.That(service.Loaded, Has.Count.EqualTo(1));
            Assert.That(service.Active.Key, Is.EqualTo(scene.path));
            return host;
        }

        static void AssertRetired(Scene scene, GameHost host)
        {
            Assert.That(BBadsSceneActions.GetNavigationHost(scene), Is.Null);
            AssertStopped(host);
        }

        static GameHost GetRunningBattleHost(Scene scene)
        {
            var clock = BBadsSceneActions.FindInScene<BBadsBattleClock>(scene);
            Assert.That(clock, Is.Not.Null);
            Assert.That(clock.Host.State, Is.EqualTo(GameHostState.Running));
            return clock.Host;
        }

        static void AssertStopped(GameHost host)
        {
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
            Assert.That(host.Context.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(host.Events.IsDisposed, Is.True);
            Assert.That(host.Events.SubscriptionCount, Is.Zero);
        }

        static void DoubleClick(Scene scene, string name)
        {
            Click(scene, name);
            Assert.That(BBadsSceneActions.IsTransitioning, Is.True);
            Click(scene, name);
        }

        static void Click(Scene scene, string name)
        {
            Button button = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Button>())
                .Single(item => item.name == name);
            Assert.That(button.IsInteractable(), Is.True);
            button.onClick.Invoke();
        }

        sealed class FailAfterActivationBackend : ISceneBackend
        {
            readonly ISceneBackend backend;

            public FailAfterActivationBackend(ISceneBackend backend) => this.backend = backend;

            public GameHost PartiallyActivatedBattle { get; private set; }

            public async Task<ISceneInstance> LoadAsync(string key, ResourceScope scope, CancellationToken token) =>
                new FailingScene(await backend.LoadAsync(key, scope, token), this);

            sealed class FailingScene : ISceneInstance
            {
                readonly ISceneInstance scene;
                readonly FailAfterActivationBackend owner;

                public FailingScene(ISceneInstance scene, FailAfterActivationBackend owner)
                {
                    this.scene = scene;
                    this.owner = owner;
                }

                public async Task ActivateAsync(CancellationToken token)
                {
                    await scene.ActivateAsync(token);
                    owner.PartiallyActivatedBattle = GetRunningBattleHost(SceneManager.GetActiveScene());
                    throw new InvalidOperationException("BBads injected activation failure");
                }

                public ValueTask DisposeAsync() => scene.DisposeAsync();
            }
        }
    }
}
