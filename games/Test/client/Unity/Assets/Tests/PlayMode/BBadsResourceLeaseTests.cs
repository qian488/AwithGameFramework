using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Unity.Hosting;
using BBads.Combat;
using BBads.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    public sealed class BBadsResourceLeaseTests
    {
        const string UnitKey = "BBads/Unit";
        const string SpriteKey = "BBads/Items/Cat_Gangster1";
        const string UnitId = "lease-test";

        readonly List<ResourceStore> stores = new List<ResourceStore>();
        readonly List<RecordingLoader> loaders = new List<RecordingLoader>();
        readonly List<Object> fixtures = new List<Object>();

        [UnityTest]
        public IEnumerator TwoConsumersShareRealAssetsAndTheLastReleaseAllowsReacquire()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            var loader = NewLoader("shared");
            var store = NewStore(loader);
            var first = new ResourceScope(store);
            var second = new ResourceScope(store);
            var firstUnit = first.LoadAsync<GameObject>(UnitKey);
            var secondUnit = second.LoadAsync<GameObject>(UnitKey);
            yield return Await(Task.WhenAll(firstUnit, secondUnit));
            var firstSprite = first.LoadAsync<Sprite>(SpriteKey);
            var secondSprite = second.LoadAsync<Sprite>(SpriteKey);
            yield return Await(Task.WhenAll(firstSprite, secondSprite));

            var prefab = firstUnit.Result.Value;
            var sprite = firstSprite.Result.Value;
            var firstConsumer = NewConsumer(prefab, sprite);
            var secondConsumer = NewConsumer(prefab, sprite);
            Assert.That(loader.Requests, Has.Count.EqualTo(2));
            Assert.That(firstUnit.Result, Is.Not.SameAs(secondUnit.Result));
            Assert.That(firstSprite.Result, Is.Not.SameAs(secondSprite.Result));
            Assert.That(secondUnit.Result.Value, Is.SameAs(prefab));
            Assert.That(secondSprite.Result.Value, Is.SameAs(sprite));
            Assert.That(prefab.GetComponent<CombatActor>(), Is.Not.Null);
            Assert.That(sprite.rect.width, Is.GreaterThan(0f));

            Object.Destroy(firstConsumer.gameObject);
            yield return WaitFor(() => firstConsumer == null, "first consumer destruction");
            yield return Await(first.DisposeAsync().AsTask());
            Assert.Throws<ObjectDisposedException>(() => { _ = firstUnit.Result.Value; });
            Assert.Throws<ObjectDisposedException>(() => { _ = firstSprite.Result.Value; });
            Assert.That(loader.Events, Has.None.StartsWith("release:"));
            Assert.That(secondUnit.Result.Value, Is.SameAs(prefab));
            Assert.That(secondConsumer.sprite, Is.SameAs(secondSprite.Result.Value));
            Assert.That(secondConsumer.sprite != null, Is.True);
            Record("first consumer destroyed and scope closed, second consumer still uses both assets");

            Object.Destroy(secondConsumer.gameObject);
            yield return WaitFor(() => secondConsumer == null, "second consumer destruction");
            loader.OnRelease = () => Assert.That(firstConsumer == null && secondConsumer == null, Is.True);
            yield return Await(second.DisposeAsync().AsTask());
            CollectionAssert.AreEqual(new[] { "release:" + SpriteKey, "release:" + UnitKey },
                loader.Events.FindAll(entry => entry.StartsWith("release:", StringComparison.Ordinal)));
            Assert.Throws<ObjectDisposedException>(() => { _ = secondSprite.Result.Value; });
            AssertSpriteMatchesNativeRelease(sprite);

            var third = new ResourceScope(store);
            var thirdUnit = third.LoadAsync<GameObject>(UnitKey);
            var thirdSprite = third.LoadAsync<Sprite>(SpriteKey);
            yield return Await(Task.WhenAll(thirdUnit, thirdSprite));
            Assert.That(loader.Requests, Has.Count.EqualTo(4));
            Assert.That(thirdUnit.Result.Value.GetComponent<CombatActor>(), Is.Not.Null);
            Assert.That(thirdSprite.Result.Value != null, Is.True);
            yield return Await(third.DisposeAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator CancelledScopeReleasesLateSpriteWithoutInvalidatingTheNewScope()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            var loader = NewLoader("sprite sessions", holdDelivery: true);
            var store = NewStore(loader);
            var oldScope = new ResourceScope(store);
            var pending = oldScope.LoadAsync<Sprite>(SpriteKey);
            var oldRequest = loader.Requests[0];
            yield return WaitFor(() => oldRequest.NativeReady, "old native Sprite load");
            yield return Await(oldScope.DisposeAsync().AsTask());
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(oldRequest.Cancellation.IsCancellationRequested, Is.True);
            Assert.That(oldRequest.Releases, Is.Zero);
            Record("old Sprite scope closed while its native result was held");

            var newScope = new ResourceScope(store);
            var current = newScope.LoadAsync<Sprite>(SpriteKey);
            var newRequest = loader.Requests[1];
            yield return WaitFor(() => newRequest.NativeReady, "new native Sprite load");
            newRequest.Deliver();
            yield return Await(current);
            var sprite = current.Result.Value;
            oldRequest.Deliver();
            yield return WaitFor(() => oldRequest.Releases == 1, "late Sprite release");
            Assert.That(current.Result.Value, Is.SameAs(sprite));
            Assert.That(sprite != null, Is.True);
            Assert.That(newRequest.Releases, Is.Zero);

            yield return Await(newScope.DisposeAsync().AsTask());
            Assert.That(newRequest.Releases, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => { _ = current.Result.Value; });
            AssertSpriteMatchesNativeRelease(sprite);
        }

        [UnityTest]
        public IEnumerator SpawnerCancelsOnExitAndDestroysActiveAndIdleUnitsBeforeRelease()
        {
            yield return SceneManager.LoadSceneAsync("BBadsBattle");
            var oldLoader = NewLoader("old battle", holdDelivery: true);
            var oldSpawner = NewSpawner(oldLoader, out var oldRoot);
            Assert.That(oldSpawner.Spawn(UnitId, Vector2.left), Is.Null);
            var oldRequest = oldLoader.Requests[0];
            yield return WaitFor(() => oldRequest.NativeReady, "old native Unit load");
            Assert.That(oldRoot.childCount, Is.Zero);

            yield return SceneManager.LoadSceneAsync("SampleScene");
            yield return WaitFor(() => oldRequest.Cancellation.IsCancellationRequested, "exit cancellation");
            Assert.That(oldSpawner == null && oldRoot == null, Is.True);
            Assert.That(oldSpawner.UnitPoolCounts.Created, Is.Zero);
            Assert.That(oldRequest.Releases, Is.Zero);
            Record("exited BBadsBattle with the old spawn cancelled and no unit pool created");

            yield return SceneManager.LoadSceneAsync("BBadsBattle");
            var clock = Object.FindObjectOfType<BBadsBattleClock>();
            yield return WaitFor(() => !clock.IsPaused, "reentered battle clock");
            clock.SetPlaybackSpeed(0f);
            var newLoader = NewLoader("new battle", holdDelivery: true);
            var newSpawner = NewSpawner(newLoader, out var newRoot);
            Assert.That(newSpawner.Spawn(UnitId, Vector2.left), Is.Null);
            Assert.That(newSpawner.Spawn(UnitId, Vector2.right), Is.Null);
            var newRequest = newLoader.Requests[0];
            yield return WaitFor(() => newRequest.NativeReady, "new native Unit load");
            newRequest.Deliver();
            for (int frame = 0; frame < 8; frame++)
                yield return null;
            Assert.That(newSpawner.UnitPoolCounts.Created, Is.Zero, "No pending spawn may run while paused.");
            clock.SetPlaybackSpeed(1f);
            yield return WaitFor(() => newSpawner.UnitPoolCounts.Active == 2, "new battle spawns after resume");
            var poolRoot = newRoot.GetChild(0);
            var returning = poolRoot.GetChild(0).GetComponent<CombatActor>();
            var active = poolRoot.GetChild(1).GetComponent<CombatActor>();
            Assert.That(returning.Position.x, Is.LessThan(active.Position.x));

            oldRequest.Deliver();
            yield return WaitFor(() => oldRequest.Releases == 1, "late Unit release");
            Assert.That(newSpawner.UnitPoolCounts, Is.EqualTo((2, 2, 0, 2, 0, 2)));
            Assert.That(newRequest.Releases, Is.Zero);

            returning.ReceiveDamage(returning.CurrentHealth);
            yield return WaitFor(() => newSpawner.UnitPoolCounts.Idle == 1, "unit return to pool");
            Assert.That(returning.gameObject.activeSelf, Is.False);
            Assert.That(active.gameObject.activeSelf, Is.True);
            Assert.That(newSpawner.UnitPoolCounts, Is.EqualTo((2, 2, 1, 1, 1, 2)));
            newLoader.OnRelease = () =>
            {
                Assert.That(newRoot != null && poolRoot == null && active == null && returning == null, Is.True);
                Record("active unit, idle unit and pool hierarchy destroyed before the prefab lease returned");
            };
            Object.Destroy(newSpawner);
            yield return WaitFor(() => newRequest.Releases == 1, "new battle pool and lease cleanup");
            Assert.That(newSpawner.UnitPoolCounts, Is.EqualTo((2, 2, 1, 0, 0, 2)));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var loader in loaders)
                foreach (var request in loader.Requests)
                    request.Deliver();
            foreach (var fixture in fixtures)
                if (fixture != null)
                    Object.Destroy(fixture);
            yield return null;
            foreach (var store in stores)
                yield return Await(store.DisposeAsync().AsTask());
            fixtures.Clear();
            stores.Clear();
            loaders.Clear();
            if (SceneManager.GetActiveScene().name == "BBadsBattle")
                yield return SceneManager.LoadSceneAsync("SampleScene");
        }

        CombatSpawner NewSpawner(RecordingLoader loader, out Transform root)
        {
            var world = Object.FindObjectOfType<CombatWorld>();
            Assert.That(world, Is.Not.Null);
            var owner = new GameObject("Resource Test Spawner");
            owner.SetActive(false);
            root = new GameObject("Resource Test Actors").transform;
            var catalog = UnitCatalog.CreateRuntime(new[] { new UnitDefinition { id = UnitId, moveSpeed = 0f } });
            fixtures.Add(owner);
            fixtures.Add(root.gameObject);
            fixtures.Add(catalog);
            var spawner = owner.AddComponent<CombatSpawner>();
            spawner.SetUnitCatalog(catalog);
            spawner.Configure(world, null, root, null, valueResourceLoader: loader);
            owner.SetActive(true);
            return spawner;
        }

        SpriteRenderer NewConsumer(GameObject prefab, Sprite sprite)
        {
            var instance = Object.Instantiate(prefab);
            fixtures.Add(instance);
            var renderer = instance.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }

        RecordingLoader NewLoader(string name, bool holdDelivery = false)
        {
            var loader = new RecordingLoader(name, holdDelivery);
            loaders.Add(loader);
            return loader;
        }

        ResourceStore NewStore(RecordingLoader loader)
        {
            var store = new ResourceStore(loader);
            stores.Add(store);
            return store;
        }

        static IEnumerator Await(Task task)
        {
            yield return WaitFor(() => task.IsCompleted, "resource task");
            task.GetAwaiter().GetResult();
        }

        static IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float timeout = Time.realtimeSinceStartup + 15f;
            while (!condition() && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(condition(), Is.True, $"Timed out waiting for {description}.");
        }

        static void Record(string message) => Debug.Log("[BBadsResourceLease] " + message);

        static void AssertSpriteMatchesNativeRelease(Sprite released)
        {
            bool leaseReleaseInvalidatedObject = released == null;
            var control = Resources.Load<Sprite>(SpriteKey);
            Assert.That(control != null, Is.True);
            Resources.UnloadAsset(control);
            bool nativeReleaseInvalidatedObject = control == null;
            Record($"Sprite compares equal to null after release by lease={leaseReleaseInvalidatedObject}, direct Unity={nativeReleaseInvalidatedObject}");
            Assert.That(leaseReleaseInvalidatedObject, Is.EqualTo(nativeReleaseInvalidatedObject));
        }

        sealed class RecordingLoader : IResourceLoader
        {
            public sealed class Request
            {
                public readonly TaskCompletionSource<bool> Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                public CancellationToken Cancellation;
                public bool NativeReady;
                public int Releases;

                public void Deliver() => Gate.TrySetResult(true);
            }

            readonly string name;
            readonly bool holdDelivery;
            readonly int unityThread = Environment.CurrentManagedThreadId;
            public readonly List<Request> Requests = new List<Request>();
            public readonly List<string> Events = new List<string>();
            public Action OnRelease;

            public RecordingLoader(string name, bool holdDelivery)
            {
                this.name = name;
                this.holdDelivery = holdDelivery;
            }

            public Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(unityThread));
                var request = new Request { Cancellation = cancellationToken };
                Requests.Add(request);
                RecordEvent("request", key);
                if (!holdDelivery)
                    request.Deliver();
                if (resourceType == typeof(GameObject))
                    return LoadNative<GameObject>(key, request);
                Assert.That(resourceType, Is.EqualTo(typeof(Sprite)));
                return LoadNative<Sprite>(key, request);
            }

            async Task<LoadedResource> LoadNative<T>(string key, Request request) where T : Object
            {
                // A separate native store provides a public lease for each actual Unity backend call.
                // Keep it until the outer store releases the deliberately delayed result.
                var native = new ResourceStore(new UnityResourceLoader());
                try
                {
                    var lease = await native.LoadAsync<T>(key, request.Cancellation);
                    request.NativeReady = true;
                    RecordEvent("native-ready", key);
                    await request.Gate.Task;
                    RecordEvent("deliver", key);
                    return new LoadedResource(lease.Value, async () =>
                    {
                        try
                        {
                            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(unityThread));
                            OnRelease?.Invoke();
                        }
                        finally
                        {
                            await lease.DisposeAsync();
                            await native.DisposeAsync();
                            RecordEvent("release", key);
                            request.Releases++;
                        }
                    });
                }
                catch
                {
                    await native.DisposeAsync();
                    throw;
                }
            }

            void RecordEvent(string stage, string key)
            {
                Events.Add($"{stage}:{key}");
                Record($"{name} {stage} {key}");
            }
        }
    }
}
