using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Core.Resources
{
    /// <summary>Shares loads and optionally retains idle resources in a bounded LRU cache.</summary>
    public sealed class ResourceStore : IAsyncDisposable
    {
        private readonly struct Identity : IEquatable<Identity>
        {
            public Identity(string key, Type type) { Key = key; Type = type; }
            public string Key { get; }
            public Type Type { get; }
            public bool Equals(Identity other) => Key == other.Key && Type == other.Type;
            public override bool Equals(object obj) => obj is Identity other && Equals(other);
            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key) * 397 ^ Type.GetHashCode();
        }

        private sealed class Entry
        {
            public Entry(Identity identity) => Identity = identity;
            public readonly Identity Identity;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly TaskCompletionSource<LoadedResource> Ready = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public LoadedResource Resource;
            public Task ReleaseTask;
            public int Waiters, References;
            public bool Detached, LoadFinished, ReleaseFinished;
            public LinkedListNode<Entry> CacheNode;
        }

        private readonly object _sync = new object();
        private readonly IResourceLoader _loader;
        private readonly MainThreadDispatcher _dispatcher;
        private readonly ResourceCacheOptions _cacheOptions;
        private readonly LinkedList<Entry> _idle = new LinkedList<Entry>();
        private long _cachedBytes;
        private readonly Dictionary<Identity, Entry> _active = new Dictionary<Identity, Entry>();
        private readonly HashSet<Entry> _entries = new HashSet<Entry>();
        private readonly List<Exception> _cleanupErrors = new List<Exception>();
        private bool _closing;
        private Task _disposeTask;

        public ResourceStore(IResourceLoader loader, MainThreadDispatcher dispatcher = null, ResourceCacheOptions cacheOptions = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _dispatcher = dispatcher;
            _cacheOptions = cacheOptions ?? new ResourceCacheOptions(0, 0);
        }

        public ResourceCacheSnapshot Cache { get { lock (_sync) return new ResourceCacheSnapshot(_idle.Count, _cachedBytes); } }

        /// <summary>Loads once and returns its lease to the idle cache. A positive cache budget is required.</summary>
        public async Task PreloadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            if (_cacheOptions.MaxBytes == 0 || _cacheOptions.MaxEntries == 0)
                throw new InvalidOperationException("Preloading requires an enabled idle cache.");
            var lease = await LoadAsync<T>(key, cancellationToken);
            await lease.DisposeAsync();
        }

        /// <summary>Evicts idle values, oldest first. Native cleanup may need continued host ticks.</summary>
        public Task TrimCacheAsync(long targetBytes = 0)
        {
            if (targetBytes < 0) throw new ArgumentOutOfRangeException(nameof(targetBytes));
            var releases = new List<Entry>();
            lock (_sync)
            {
                if (_closing) throw new ObjectDisposedException(nameof(ResourceStore));
                CollectEvictions(releases, targetBytes, _cacheOptions.MaxEntries);
            }
            return ReleaseAllAsync(releases);
        }

        public Task<ResourceLease<T>> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Resource key cannot be empty.", nameof(key));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<ResourceLease<T>>(cancellationToken);
            Entry entry;
            bool start = false;
            lock (_sync)
            {
                if (_closing) throw new ObjectDisposedException(nameof(ResourceStore));
                var identity = new Identity(key, typeof(T));
                if (!_active.TryGetValue(identity, out entry))
                {
                    entry = new Entry(identity);
                    _active.Add(identity, entry);
                    _entries.Add(entry);
                    start = true;
                }
                RemoveIdle(entry);
                entry.Waiters++;
            }
            if (start) _ = LoadEntryAsync(entry);
            return AcquireAsync<T>(entry, cancellationToken);
        }

        private async Task<ResourceLease<T>> AcquireAsync<T>(Entry entry, CancellationToken cancellationToken) where T : class
        {
            bool acquired = false;
            try
            {
                var resource = await WaitForCallerAsync(entry.Ready.Task, cancellationToken);
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_closing || entry.Detached) throw new ObjectDisposedException(nameof(ResourceStore));
                    entry.Waiters--;
                    entry.References++;
                    acquired = true;
                    return new ResourceLease<T>(entry.Identity.Key, (T)resource.Value,
                        () => IsValid(entry), () => ReleaseLeaseAsync(entry));
                }
            }
            finally
            {
                if (!acquired)
                {
                    bool unused;
                    lock (_sync)
                    {
                        entry.Waiters--;
                        unused = entry.Waiters == 0 && entry.References == 0;
                        if (unused) Detach(entry);
                    }
                    if (unused)
                    {
                        CancelLoad(entry);
                        ObserveRelease(entry);
                    }
                }
            }
        }

        private async Task LoadEntryAsync(Entry entry)
        {
            LoadedResource resource = null;
            try
            {
                resource = await OnHostAsync(() => _loader.LoadAsync(entry.Identity.Key, entry.Identity.Type, entry.Cancellation.Token), entry.Cancellation.Token);
                if (resource == null) throw new InvalidOperationException($"Loader returned no resource for '{entry.Identity.Key}'.");
                if (!entry.Identity.Type.IsInstanceOfType(resource.Value))
                    throw new InvalidOperationException($"Resource '{entry.Identity.Key}' returned {resource.Value.GetType().FullName}, expected {entry.Identity.Type.FullName}.");
                lock (_sync)
                {
                    entry.Resource = resource;
                    if (!entry.Detached && !_closing) entry.Ready.TrySetResult(resource);
                    else entry.Ready.TrySetCanceled();
                }
            }
            catch (Exception failure)
            {
                lock (_sync)
                {
                    entry.Resource = resource;
                    Detach(entry);
                }
                if (resource != null)
                {
                    try { await ReleaseEntryAsync(entry); }
                    catch (Exception cleanup) { failure = new AggregateException("Resource load and release both failed.", failure, cleanup); }
                }
                if (failure is OperationCanceledException cancelled) entry.Ready.TrySetCanceled(cancelled.CancellationToken);
                else
                {
                    entry.Ready.TrySetException(failure);
                    _ = entry.Ready.Task.Exception; // A caller may already have cancelled its own wait.
                }
            }
            finally
            {
                bool detached;
                lock (_sync)
                {
                    entry.LoadFinished = true;
                    detached = entry.Detached;
                    FinishIfReleased(entry);
                }
                entry.Cancellation.Dispose();
                if (detached) ObserveRelease(entry);
            }
        }

        private Task ReleaseLeaseAsync(Entry entry)
        {
            bool unused;
            var releases = new List<Entry>();
            lock (_sync)
            {
                entry.References--;
                unused = entry.Waiters == 0 && entry.References == 0;
                if (unused && !entry.Detached)
                {
                    var size = entry.Resource?.EstimatedSizeBytes ?? 0;
                    if (!_closing && size > 0 && size <= _cacheOptions.MaxBytes && _cacheOptions.MaxEntries > 0)
                    {
                        CollectEvictions(releases, _cacheOptions.MaxBytes - size, _cacheOptions.MaxEntries - 1);
                        entry.CacheNode = _idle.AddLast(entry);
                        _cachedBytes += size;
                        unused = false;
                    }
                    else Detach(entry);
                }
                if ((unused || entry.Detached) && !releases.Contains(entry)) releases.Add(entry);
            }
            return ReleaseAllAsync(releases);
        }

        private void RemoveIdle(Entry entry)
        {
            if (entry.CacheNode == null) return;
            _idle.Remove(entry.CacheNode);
            entry.CacheNode = null;
            _cachedBytes -= entry.Resource.EstimatedSizeBytes;
        }

        private void CollectEvictions(List<Entry> releases, long maxBytes, int maxEntries)
        {
            while (_idle.Count > 0 && (_cachedBytes > maxBytes || _idle.Count > maxEntries))
            {
                var entry = _idle.First.Value;
                Detach(entry);
                releases.Add(entry);
            }
        }

        private Task ReleaseAllAsync(List<Entry> entries)
        {
            if (entries.Count == 0) return Task.CompletedTask;
            var tasks = new Task[entries.Count];
            for (int i = 0; i < entries.Count; i++) tasks[i] = ReleaseEntryAsync(entries[i]);
            return Task.WhenAll(tasks);
        }

        private bool IsValid(Entry entry) { lock (_sync) return !_closing && !entry.Detached; }

        // Call while holding _sync. Existing leases may still be held when the entire store is closing.
        private void Detach(Entry entry)
        {
            RemoveIdle(entry);
            entry.Detached = true;
            if (_active.TryGetValue(entry.Identity, out var current) && ReferenceEquals(current, entry)) _active.Remove(entry.Identity);
            FinishIfReleased(entry);
        }

        private void FinishIfReleased(Entry entry)
        {
            if (entry.Detached && entry.LoadFinished && (entry.Resource == null || entry.ReleaseFinished))
            {
                _entries.Remove(entry);
                entry.Finished.TrySetResult(true);
            }
        }

        private Task ReleaseEntryAsync(Entry entry)
        {
            TaskCompletionSource<bool> completion;
            lock (_sync)
            {
                if (entry.ReleaseTask != null) return entry.ReleaseTask;
                if (entry.Resource == null) return Task.CompletedTask;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.ReleaseTask = completion.Task;
            }
            _ = CompleteReleaseAsync(entry, completion);
            return completion.Task;
        }

        private async Task CompleteReleaseAsync(Entry entry, TaskCompletionSource<bool> completion)
        {
            try
            {
                await OnHostAsync(async () => { await entry.Resource.ReleaseAsync(); return true; }, CancellationToken.None, true);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                lock (_sync) _cleanupErrors.Add(ex);
                completion.TrySetException(ex);
                _ = completion.Task.Exception;
            }
            finally
            {
                lock (_sync)
                {
                    entry.ReleaseFinished = true;
                    FinishIfReleased(entry);
                }
            }
        }

        private void ObserveRelease(Entry entry)
        {
            // Completion errors are retained and reported by DisposeAsync, even after all callers cancel.
            _ = ReleaseEntryAsync(entry);
        }

        private void CancelLoad(Entry entry)
        {
            try { entry.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { lock (_sync) _cleanupErrors.Add(ex); }
        }

        public ValueTask DisposeAsync()
        {
            Entry[] entries;
            TaskCompletionSource<bool> completion;
            lock (_sync)
            {
                if (_disposeTask != null) return new ValueTask(_disposeTask);
                _closing = true;
                entries = new Entry[_entries.Count];
                _entries.CopyTo(entries);
                foreach (var entry in entries) { Detach(entry); entry.Ready.TrySetCanceled(); }
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }
            _ = CompleteDisposeAsync(entries, completion);
            return new ValueTask(completion.Task);
        }

        private async Task CompleteDisposeAsync(Entry[] entries, TaskCompletionSource<bool> completion)
        {
            try
            {
                foreach (var entry in entries) { CancelLoad(entry); ObserveRelease(entry); }
                foreach (var entry in entries) await entry.Finished.Task;
                lock (_sync)
                {
                    if (_cleanupErrors.Count == 0) completion.TrySetResult(true);
                    else completion.TrySetException(new AggregateException("Resource cleanup reported errors.", _cleanupErrors.ToArray()));
                }
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }

        private async Task<T> OnHostAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, bool cleanup = false)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess) return await operation();
            Task<T> pending = null;
            if (cleanup) await _dispatcher.InvokeCleanupAsync(() => pending = operation());
            else await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending = operation();
            }, CancellationToken.None); // Once started, retain the backend task so late resources can be released.
            if (pending == null) throw new InvalidOperationException("Resource backend returned a null Task.");
            return await pending;
        }

        private static async Task<T> WaitForCallerAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await operation;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
            if (await Task.WhenAny(operation, cancelled.Task) != operation) cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            return await operation;
        }
    }
}
