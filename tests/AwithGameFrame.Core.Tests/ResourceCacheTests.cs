using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class ResourceCacheTests
    {
        private sealed class Loader : IResourceLoader
        {
            public readonly List<string> Released = new List<string>();
            public int Loads;
            public long Size = 4;
            public bool FailRelease;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token)
            {
                Loads++;
                return Task.FromResult(new LoadedResource(new object(), () =>
                {
                    Released.Add(key);
                    return FailRelease ? Task.FromException(new InvalidOperationException("release failed")) : Task.CompletedTask;
                }, Size));
            }
        }

        [Test]
        public async Task IdleResourceIsReusedAndTrimmedWithoutTouchingLiveLeases()
        {
            var loader = new Loader();
            await using var store = new ResourceStore(loader, cacheOptions: new ResourceCacheOptions(8));
            var first = await store.LoadAsync<object>("a");
            var value = first.Value;
            await first.DisposeAsync();
            var again = await store.LoadAsync<object>("a");
            Assert.That(again.Value, Is.SameAs(value));
            Assert.That(loader.Loads, Is.EqualTo(1));
            await store.TrimCacheAsync();
            Assert.That(loader.Released, Is.Empty);
            Assert.That(again.Value, Is.SameAs(value));
            await again.DisposeAsync();
            await store.TrimCacheAsync();
            Assert.That(loader.Released, Is.EqualTo(new[] { "a" }));
            Assert.That(store.Cache.EstimatedBytes, Is.Zero);
        }

        [Test]
        public async Task BudgetEvictsLeastRecentlyReturnedResource()
        {
            var loader = new Loader();
            await using var store = new ResourceStore(loader, cacheOptions: new ResourceCacheOptions(8, 2));
            await store.PreloadAsync<object>("a");
            await store.PreloadAsync<object>("b");
            var a = await store.LoadAsync<object>("a");
            await a.DisposeAsync();
            await store.PreloadAsync<object>("c");
            Assert.That(loader.Released, Is.EqualTo(new[] { "b" }));
            Assert.That(store.Cache.EstimatedBytes, Is.EqualTo(8));
            var reloaded = await store.LoadAsync<object>("b");
            Assert.That(loader.Loads, Is.EqualTo(4));
            await reloaded.DisposeAsync();
        }

        [TestCase(0)]
        [TestCase(9)]
        public async Task UnknownOrOversizeResourceIsReleasedInsteadOfRetained(long bytes)
        {
            var loader = new Loader { Size = bytes };
            await using var store = new ResourceStore(loader, cacheOptions: new ResourceCacheOptions(8));
            var lease = await store.LoadAsync<object>("a");
            await lease.DisposeAsync();
            Assert.That(loader.Released, Is.EqualTo(new[] { "a" }));
            Assert.That(store.Cache.Entries, Is.Zero);
        }

        [Test]
        public async Task MaximumByteBudgetDoesNotOverflow()
        {
            var loader = new Loader { Size = long.MaxValue };
            await using var store = new ResourceStore(loader, cacheOptions: new ResourceCacheOptions(long.MaxValue));
            await store.PreloadAsync<object>("a");
            await store.PreloadAsync<object>("b");
            Assert.That(loader.Released, Is.EqualTo(new[] { "a" }));
            Assert.That(store.Cache.EstimatedBytes, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public async Task EvictionFailureRemainsObservableAtFinalShutdown()
        {
            var loader = new Loader { FailRelease = true };
            var store = new ResourceStore(loader, cacheOptions: new ResourceCacheOptions(4));
            await store.PreloadAsync<object>("a");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await store.TrimCacheAsync());
            Assert.ThrowsAsync<AggregateException>(async () => await store.DisposeAsync());
            Assert.That(loader.Released, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void PreloadRequiresExplicitCachePolicy()
        {
            var store = new ResourceStore(new Loader());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await store.PreloadAsync<object>("a"));
            store.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
