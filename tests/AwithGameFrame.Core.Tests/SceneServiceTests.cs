using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class SceneServiceTests
    {
        private sealed class Loader : IResourceLoader
        {
            public Action<string> Released;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token) => Task.FromResult(new LoadedResource(key, () => { Released?.Invoke(key); return Task.CompletedTask; }));
        }
        private sealed class Scene : ISceneInstance
        {
            public string Key; public List<string> Events;
            public Func<Task> Activate, Unload;
            public Task ActivateAsync(CancellationToken token) { Events?.Add("activate:" + Key); return Activate?.Invoke() ?? Task.CompletedTask; }
            public ValueTask DisposeAsync() { Events?.Add("unload:" + Key); return new ValueTask(Unload?.Invoke() ?? Task.CompletedTask); }
        }
        private sealed class Backend : ISceneBackend
        {
            public int Calls;
            public Func<string, ResourceScope, CancellationToken, Task<ISceneInstance>> Load;
            public Task<ISceneInstance> LoadAsync(string key, ResourceScope scope, CancellationToken token) { Calls++; return Load(key, scope, token); }
        }

        [Test]
        public async Task AdditivePreservesOwnedScenesAndSingleActivatesBeforeRemovingThem()
        {
            var events = new List<string>(); var host = new GameHost(); var store = new ResourceStore(new Loader());
            var backend = new Backend { Load = (key, _, _) => { events.Add("load:" + key); return Task.FromResult<ISceneInstance>(new Scene { Key = key, Events = events }); } };
            var service = new SceneService(host.Context, backend, store);
            var a = await service.LoadAsync("a"); var b = await service.LoadAsync("b", SceneLoadMode.Additive);
            Assert.That(service.Loaded.Count, Is.EqualTo(2)); Assert.That(service.Active, Is.SameAs(b));
            var c = await service.LoadAsync("c");
            Assert.That(service.Loaded, Is.EqualTo(new[] { c })); Assert.That(a.IsLoaded || b.IsLoaded, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "load:a", "activate:a", "load:b", "activate:b", "load:c", "activate:c", "unload:b", "unload:a" }));
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task CancelledLateSceneUnloadsBeforeReturningAndPreservesPreviousActiveScene()
        {
            var events = new List<string>(); var host = new GameHost(); var store = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<ISceneInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new SceneService(host.Context, new Backend { Load = (key, _, _) => key == "late" ? pending.Task : Task.FromResult<ISceneInstance>(new Scene { Key = key, Events = events }) }, store);
            var previous = await service.LoadAsync("old");
            using var cancel = new CancellationTokenSource(); var load = service.LoadAsync("late", cancellationToken: cancel.Token); cancel.Cancel();
            pending.SetResult(new Scene { Key = "late", Events = events });
            Assert.CatchAsync<OperationCanceledException>(async () => await load.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.That(service.Active, Is.SameAs(previous)); Assert.That(service.Loaded.Count, Is.EqualTo(1));
            Assert.That(events, Does.Contain("unload:late")); Assert.That(events, Does.Not.Contain("activate:late"));
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task CancellationDuringActivationRestoresPreviousNativeActiveScene()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            using var cancel = new CancellationTokenSource();
            string nativeActive = null;
            var backend = new Backend { Load = (key, _, _) => Task.FromResult<ISceneInstance>(new Scene
            {
                Key = key,
                Activate = () => { nativeActive = key; if (key == "cancelled") cancel.Cancel(); return Task.CompletedTask; }
            }) };
            var service = new SceneService(host.Context, backend, store);
            var original = await service.LoadAsync("original");
            Assert.CatchAsync<OperationCanceledException>(() => service.LoadAsync("cancelled", cancellationToken: cancel.Token));
            Assert.That(nativeActive, Is.EqualTo("original"));
            Assert.That(service.Active, Is.SameAs(original));
            Assert.That(service.Loaded, Is.EqualTo(new[] { original }));
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task FailedActivationDisposesNativeBeforeSceneAssets()
        {
            var events = new List<string>(); var host = new GameHost(); var store = new ResourceStore(new Loader { Released = _ => events.Add("asset") });
            var service = new SceneService(host.Context, new Backend { Load = async (_, scope, token) =>
            {
                await scope.LoadAsync<string>("owned", token);
                return new Scene { Key = "bad", Events = events, Activate = () => throw new FormatException("activate") };
            } }, store);
            Assert.ThrowsAsync<FormatException>(() => service.LoadAsync("bad"));
            Assert.That(events, Is.EqualTo(new[] { "activate:bad", "unload:bad", "asset" }));
            Assert.That(service.Loaded, Is.Empty);
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task SingleCleanupFailureKeepsCommittedNewSceneAndRetainsFailedOldSceneForRetry()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader()); bool fail = true;
            var backend = new Backend { Load = (key, _, _) => Task.FromResult<ISceneInstance>(new Scene { Key = key, Unload = () => key == "old" && fail ? Task.FromException(new FormatException("unload")) : Task.CompletedTask }) };
            var service = new SceneService(host.Context, backend, store);
            var old = await service.LoadAsync("old");
            Assert.ThrowsAsync<AggregateException>(() => service.LoadAsync("new"));
            Assert.That(service.Active.Key, Is.EqualTo("new")); Assert.That(old.IsLoaded, Is.True);
            fail = false; await old.UnloadAsync(); Assert.That(old.IsLoaded, Is.False);
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task UnloadingActiveSceneActivatesFallbackAndReleasesScopeAfterNativeScene()
        {
            var events = new List<string>(); var host = new GameHost(); var store = new ResourceStore(new Loader { Released = key => events.Add("asset:" + key) });
            var backend = new Backend { Load = async (key, scope, token) => { await scope.LoadAsync<string>(key, token); return new Scene { Key = key, Events = events }; } };
            var service = new SceneService(host.Context, backend, store);
            var a = await service.LoadAsync("a"); var b = await service.LoadAsync("b", SceneLoadMode.Additive);
            events.Clear(); await b.UnloadAsync();
            Assert.That(service.Active, Is.SameAs(a));
            Assert.That(events, Is.EqualTo(new[] { "activate:a", "unload:b", "asset:b" }));
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task StopWaitsForLateNativeLoadAndAttemptsAllOwnedSceneCleanup()
        {
            var events = new List<string>(); var host = new GameHost(); var store = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<ISceneInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new SceneService(host.Context, new Backend { Load = (key, _, _) => key == "late" ? pending.Task : Task.FromResult<ISceneInstance>(new Scene { Key = key, Events = events }) }, store);
            await service.LoadAsync("old"); var loading = service.LoadAsync("late"); var stop = service.StopAsync();
            Assert.That(stop.IsCompleted, Is.False);
            pending.SetResult(new Scene { Key = "late", Events = events });
            Assert.CatchAsync<OperationCanceledException>(async () => await loading.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(events, Is.EqualTo(new[] { "activate:old", "unload:late", "unload:old" }));
            Assert.That(service.Loaded, Is.Empty); Assert.That(service.StopAsync(), Is.SameAs(stop));
            await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task DuplicateQueuedLoadsReuseNativeSceneAndUnloadIsIdempotent()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<ISceneInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Backend { Load = (_, _, _) => pending.Task };
            var service = new SceneService(host.Context, backend, store);
            var first = service.LoadAsync("same"); var second = service.LoadAsync("same"); pending.SetResult(new Scene());
            var a = await first; Assert.That(await second, Is.SameAs(a)); Assert.That(backend.Calls, Is.EqualTo(1));
            await a.UnloadAsync(); await a.UnloadAsync();
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }
    }
}
