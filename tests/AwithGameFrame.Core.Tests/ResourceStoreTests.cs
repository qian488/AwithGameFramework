using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class ResourceStoreTests
    {
        private sealed class Loader : IResourceLoader
        {
            public int Calls;
            public Func<string, Type, CancellationToken, Task<LoadedResource>> Load;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token)
            {
                Interlocked.Increment(ref Calls);
                return Load(key, type, token);
            }
        }

        private static LoadedResource Loaded(object value, Action release) => new LoadedResource(value, () => { release(); return Task.CompletedTask; });

        [Test]
        public async Task ConcurrentCallersShareLoadAndLastLeaseReleasesExactlyOnce()
        {
            var pending = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            int released = 0;
            var loader = new Loader { Load = (_, _, _) => pending.Task };
            var store = new ResourceStore(loader);
            var first = store.LoadAsync<object>("same");
            var second = store.LoadAsync<object>("same");
            var value = new object();
            pending.SetResult(Loaded(value, () => released++));
            var a = await first;
            var b = await second;
            Assert.That(loader.Calls, Is.EqualTo(1));
            Assert.That(a.Value, Is.SameAs(b.Value));
            await a.DisposeAsync();
            Assert.That(released, Is.Zero);
            await b.DisposeAsync();
            await b.DisposeAsync();
            Assert.That(released, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => { _ = b.Value; });
            loader.Load = (_, _, _) => Task.FromResult(Loaded(new object(), () => released++));
            var reloaded = await store.LoadAsync<object>("same");
            Assert.That(loader.Calls, Is.EqualTo(2));
            await reloaded.DisposeAsync();
            await store.DisposeAsync();
            Assert.That(released, Is.EqualTo(2));
        }

        [Test]
        public async Task CancellingOneCallerDoesNotCancelSharedBackendLoad()
        {
            var pending = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken backend = default;
            int released = 0;
            var loader = new Loader { Load = (_, _, token) => { backend = token; return pending.Task; } };
            var store = new ResourceStore(loader);
            using var cancellation = new CancellationTokenSource();
            var cancelled = store.LoadAsync<object>("same", cancellation.Token);
            var surviving = store.LoadAsync<object>("same");
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => cancelled);
            Assert.That(backend.IsCancellationRequested, Is.False);
            pending.SetResult(Loaded(new object(), () => released++));
            var lease = await surviving;
            Assert.That(released, Is.Zero);
            await lease.DisposeAsync();
            await store.DisposeAsync();
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public async Task LastCancelledWaiterCancelsBackendAndLateSuccessIsReleased()
        {
            var pending = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken backend = default;
            int released = 0;
            var loader = new Loader { Load = (_, _, token) => { backend = token; return pending.Task; } };
            var store = new ResourceStore(loader);
            using var cancellation = new CancellationTokenSource();
            var cancelled = store.LoadAsync<object>("late", cancellation.Token);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => cancelled);
            Assert.That(backend.IsCancellationRequested, Is.True);
            pending.SetResult(Loaded(new object(), () => Interlocked.Increment(ref released)));
            await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedLoadCanRetryAndWrongValueTypeIsReleased()
        {
            int released = 0;
            var loader = new Loader { Load = (_, _, _) => Task.FromException<LoadedResource>(new FormatException("load")) };
            var store = new ResourceStore(loader);
            Assert.ThrowsAsync<FormatException>(() => store.LoadAsync<string>("key"));
            loader.Load = (_, _, _) => Task.FromResult(Loaded(new object(), () => released++));
            var mismatch = Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync<string>("key"));
            Assert.That(mismatch.Message, Does.Contain("expected System.String"));
            Assert.That(released, Is.EqualTo(1));
            loader.Load = (_, _, _) => Task.FromResult(Loaded("correct", () => released++));
            var lease = await store.LoadAsync<string>("key");
            Assert.That(lease.Value, Is.EqualTo("correct"));
            await lease.DisposeAsync();
            await store.DisposeAsync();
            Assert.That(loader.Calls, Is.EqualTo(3));
            Assert.That(released, Is.EqualTo(2));
        }

        [Test]
        public async Task ResourceIdentityIncludesRequestedType()
        {
            int released = 0;
            var loader = new Loader { Load = (_, type, _) => Task.FromResult(Loaded(type == typeof(string) ? "text" : new object(), () => released++)) };
            var store = new ResourceStore(loader);
            var text = await store.LoadAsync<string>("same-key");
            var data = await store.LoadAsync<object>("same-key");
            Assert.That(loader.Calls, Is.EqualTo(2));
            await text.DisposeAsync();
            await data.DisposeAsync();
            await store.DisposeAsync();
            Assert.That(released, Is.EqualTo(2));
        }

        [Test]
        public async Task ClosingStoreInvalidatesLeasesAndWaitsForLateBackendCleanup()
        {
            var pending = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            int released = 0;
            var loader = new Loader { Load = (key, _, _) => key == "pending" ? pending.Task : Task.FromResult(Loaded(new object(), () => released++)) };
            var store = new ResourceStore(loader);
            var lease = await store.LoadAsync<object>("held");
            var wait = store.LoadAsync<object>("pending");
            var dispose = store.DisposeAsync().AsTask();
            Assert.That(dispose.IsCompleted, Is.False);
            Assert.Throws<ObjectDisposedException>(() => { _ = lease.Value; });
            Assert.CatchAsync<OperationCanceledException>(() => wait);
            pending.SetResult(Loaded(new object(), () => released++));
            await dispose.WaitAsync(TimeSpan.FromSeconds(3));
            await lease.DisposeAsync();
            Assert.That(released, Is.EqualTo(2));
            Assert.Throws<ObjectDisposedException>(() => store.LoadAsync<object>("late"));
        }

        [Test]
        public async Task ScopeReleasesOwnedLeasesInReverseOrderAndPreservesExternalLease()
        {
            var released = new List<string>();
            var loader = new Loader { Load = (key, _, _) => Task.FromResult(Loaded(new object(), () => released.Add(key))) };
            var store = new ResourceStore(loader);
            var outside = await store.LoadAsync<object>("shared");
            var scope = new ResourceScope(store);
            var scopedShared = await scope.LoadAsync<object>("shared");
            var scopedOnly = await scope.LoadAsync<object>("own");
            await scope.DisposeAsync();
            Assert.That(released, Is.EqualTo(new[] { "own" }));
            Assert.That(outside.Value, Is.Not.Null);
            Assert.Throws<ObjectDisposedException>(() => { _ = scopedShared.Value; });
            Assert.Throws<ObjectDisposedException>(() => { _ = scopedOnly.Value; });
            await outside.DisposeAsync();
            await store.DisposeAsync();
            Assert.That(released, Is.EqualTo(new[] { "own", "shared" }));
        }

        [Test]
        public async Task ScopeCloseCancelsPendingLoadsAndDoesNotRetainLateLease()
        {
            var pending = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            int released = 0;
            var store = new ResourceStore(new Loader { Load = (_, _, _) => pending.Task });
            var scope = new ResourceScope(store);
            var load = scope.LoadAsync<object>("pending");
            await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.CatchAsync<OperationCanceledException>(() => load);
            pending.SetResult(Loaded(new object(), () => released++));
            await store.DisposeAsync();
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public async Task CleanupFailureDoesNotSkipOtherResources()
        {
            var released = new List<string>();
            var store = new ResourceStore(new Loader
            {
                Load = (key, _, _) => Task.FromResult(new LoadedResource(new object(), () =>
                {
                    released.Add(key);
                    if (key == "bad") throw new FormatException("release");
                    return Task.CompletedTask;
                }))
            });
            var scope = new ResourceScope(store);
            await scope.LoadAsync<object>("good");
            await scope.LoadAsync<object>("bad");
            Assert.ThrowsAsync<AggregateException>(() => scope.DisposeAsync().AsTask());
            Assert.That(released, Is.EqualTo(new[] { "bad", "good" }));
            Assert.ThrowsAsync<AggregateException>(() => store.DisposeAsync().AsTask());
        }

        [Test]
        public void BackendLoadAndReleaseRunOnDispatcherOwnerThread()
        {
            using var dispatcher = new MainThreadDispatcher();
            int owner = Environment.CurrentManagedThreadId, loadedOn = 0, releasedOn = 0;
            var store = new ResourceStore(new Loader
            {
                Load = (_, _, _) =>
                {
                    loadedOn = Environment.CurrentManagedThreadId;
                    return Task.FromResult(Loaded(new object(), () => releasedOn = Environment.CurrentManagedThreadId));
                }
            }, dispatcher);
            var work = Task.Run(async () =>
            {
                var lease = await store.LoadAsync<object>("threaded");
                await lease.DisposeAsync();
                await store.DisposeAsync();
            });
            var timeout = Stopwatch.StartNew();
            while (!work.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(3)) { dispatcher.RunPending(8); Thread.Yield(); }
            Assert.That(work.IsCompleted, Is.True, "Dispatcher work did not finish.");
            work.GetAwaiter().GetResult();
            Assert.That(loadedOn, Is.EqualTo(owner));
            Assert.That(releasedOn, Is.EqualTo(owner));
        }

        [Test]
        public void HostResourceModuleReleasesBackendBeforeDispatcherCloses()
        {
            var host = new GameHost();
            int released = 0;
            var store = new ResourceStore(new Loader { Load = (_, _, _) => Task.FromResult(Loaded(new object(), () => released++)) }, host.Dispatcher);
            host.AddModule(new ResourceModule(store));
            host.StartAsync().GetAwaiter().GetResult();
            Assert.That(host.Services.Get<ResourceStore>(), Is.SameAs(store));
            var lease = store.LoadAsync<object>("held").GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();
            Assert.That(released, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => { _ = lease.Value; });
            lease.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void QueuedNativeReleaseSurvivesFullQueueAndHostStopCancellation()
        {
            var host = new GameHost(dispatcherCapacity: 1);
            int released = 0;
            var store = new ResourceStore(new Loader { Load = (_, _, _) => Task.FromResult(Loaded(new object(), () => released++)) }, host.Dispatcher);
            host.AddModule(new ResourceModule(store));
            host.StartAsync().GetAwaiter().GetResult();
            var lease = store.LoadAsync<object>("native").GetAwaiter().GetResult();
            var applicationWork = host.Dispatcher.InvokeAsync(() => Assert.Fail("normal work should be cancelled by stop"));
            Task release = null;
            Task.Run(() => { release = lease.DisposeAsync().AsTask(); }).GetAwaiter().GetResult();
            Assert.That(release.IsCompleted, Is.False);
            var stop = host.StopAsync();
            Assert.That(applicationWork.IsCanceled, Is.True);
            var timeout = Stopwatch.StartNew();
            while (!stop.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(3)) { host.Tick(0); Thread.Yield(); }
            Assert.That(stop.IsCompleted, Is.True);
            stop.GetAwaiter().GetResult();
            release.GetAwaiter().GetResult();
            Assert.That(released, Is.EqualTo(1));
        }
    }
}
