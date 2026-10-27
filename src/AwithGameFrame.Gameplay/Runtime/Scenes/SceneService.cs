using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;

namespace AwithGameFrame.Extensions.Scenes
{
    public enum SceneLoadMode { Single, Additive }

    public interface ISceneInstance : IAsyncDisposable
    {
        Task ActivateAsync(CancellationToken cancellationToken);
    }

    public interface ISceneBackend
    {
        /// <summary>Load without replacing unrelated scenes. A late native success must be returned for cleanup. The resource scope is borrowed; only the service disposes it.</summary>
        Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken cancellationToken);
    }

    public sealed class SceneHandle : IAsyncDisposable
    {
        private readonly SceneService owner;
        internal SceneHandle(SceneService owner, long id, string key, ISceneInstance instance, ResourceScope resources)
        { this.owner = owner; InstanceId = id; Key = key; Instance = instance; Resources = resources; }
        internal ISceneInstance Instance { get; }
        public long InstanceId { get; }
        public string Key { get; }
        internal ResourceScope Resources { get; }
        public bool IsLoaded => owner.Contains(this);
        public Task UnloadAsync() => owner.UnloadAsync(this);
        public ValueTask DisposeAsync() => new ValueTask(UnloadAsync());
    }

    /// <summary>Owns only scenes loaded through this instance, preserving the engine bootstrap and other hosts.</summary>
    public sealed class SceneService
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim transition = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> inside = new AsyncLocal<bool>();
        private readonly CancellationTokenSource lifetime;
        private readonly ISceneBackend backend;
        private readonly ResourceStore resources;
        private readonly List<SceneHandle> loaded = new List<SceneHandle>();
        private readonly List<Exception> cancellationErrors = new List<Exception>();
        private SceneHandle active;
        private long nextId;
        private bool stopped;
        private Task stopTask;

        public SceneService(GameContext context, ISceneBackend backend, ResourceStore resources)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.LifetimeToken);
        }

        public SceneHandle Active { get { lock (sync) return active; } }
        public IReadOnlyList<SceneHandle> Loaded { get { lock (sync) return Array.AsReadOnly(loaded.ToArray()); } }
        internal bool Contains(SceneHandle handle) { lock (sync) return loaded.Contains(handle); }

        public async Task<SceneHandle> LoadAsync(string key, SceneLoadMode mode = SceneLoadMode.Single, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Scene key cannot be empty.", nameof(key));
            if (mode != SceneLoadMode.Single && mode != SceneLoadMode.Additive) throw new ArgumentOutOfRangeException(nameof(mode));
            RejectReentry();
            using var operation = CreateOperation(cancellationToken);
            await transition.WaitAsync(operation.Token);
            inside.Value = true;
            ResourceScope scope = null;
            ISceneInstance instance = null;
            SceneHandle result = null;
            SceneHandle previousActive = null;
            bool committed = false;
            bool activationAttempted = false;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                lock (sync) { result = loaded.Find(item => item.Key == key); previousActive = active; }
                if (result == null)
                {
                    scope = new ResourceScope(resources);
                    instance = await backend.LoadAsync(key, scope, operation.Token);
                    if (instance == null) throw new InvalidOperationException($"Scene backend returned null for '{key}'.");
                    operation.Token.ThrowIfCancellationRequested();
                    activationAttempted = true;
                    await instance.ActivateAsync(operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    result = new SceneHandle(this, ++nextId, key, instance, scope);
                }
                else { activationAttempted = true; await result.Instance.ActivateAsync(operation.Token); }
                operation.Token.ThrowIfCancellationRequested();
                SceneHandle[] previous;
                lock (sync)
                {
                    if (stopped) throw new OperationCanceledException(operation.Token);
                    previous = loaded.ToArray();
                    if (!loaded.Contains(result)) loaded.Add(result);
                    active = result;
                    committed = true;
                }
                // After commitment, cancellation cannot abandon removal of the old owned scenes.
                if (mode == SceneLoadMode.Single)
                {
                    var errors = new List<Exception>();
                    for (int i = previous.Length - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(previous[i], result)) continue;
                        try { await UnloadCoreAsync(previous[i]); } catch (Exception ex) { errors.Add(ex); }
                    }
                    if (errors.Count != 0) throw new AggregateException("New scene is active, but previous scene cleanup reported errors. Inspect Loaded to retry.", errors);
                }
                return result;
            }
            catch (Exception failure)
            {
                if (!committed)
                {
                    var errors = new List<Exception> { failure };
                    if (instance != null) { try { await instance.DisposeAsync(); } catch (Exception ex) { errors.Add(ex); } }
                    if (scope != null) { try { await scope.DisposeAsync(); } catch (Exception ex) { errors.Add(ex); } }
                    if (activationAttempted && previousActive != null && !ReferenceEquals(previousActive, result) && Contains(previousActive))
                    { try { await previousActive.Instance.ActivateAsync(CancellationToken.None); } catch (Exception ex) { errors.Add(ex); } }
                    if (errors.Count > 1) throw new AggregateException("Scene loading and cleanup reported errors.", errors);
                }
                throw;
            }
            finally { inside.Value = false; transition.Release(); }
        }

        public async Task UnloadAsync(SceneHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            RejectReentry();
            await transition.WaitAsync();
            inside.Value = true;
            try
            {
                SceneHandle fallback = null;
                lock (sync)
                {
                    if (!loaded.Contains(handle)) return;
                    if (ReferenceEquals(active, handle))
                        for (int i = loaded.Count - 1; i >= 0; i--) if (!ReferenceEquals(loaded[i], handle)) { fallback = loaded[i]; break; }
                }
                if (fallback != null)
                {
                    await fallback.Instance.ActivateAsync(CancellationToken.None);
                    lock (sync) active = fallback;
                }
                await UnloadCoreAsync(handle);
                lock (sync) { if (active == null) active = fallback; }
            }
            finally { inside.Value = false; transition.Release(); }
        }

        private async Task UnloadCoreAsync(SceneHandle handle)
        {
            if (!Contains(handle)) return;
            // If native unloading fails, keep the handle and its resource scope for an explicit retry.
            await handle.Instance.DisposeAsync();
            lock (sync) { loaded.Remove(handle); if (ReferenceEquals(active, handle)) active = null; }
            await handle.Resources.DisposeAsync();
        }

        public Task StopAsync()
        {
            RejectReentry();
            TaskCompletionSource<bool> completion;
            lock (sync)
            {
                if (stopTask != null) return stopTask;
                stopped = true;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stopTask = completion.Task;
            }
            try { lifetime.Cancel(); } catch (Exception ex) { lock (sync) cancellationErrors.Add(ex); }
            _ = CompleteStopAsync(completion);
            return completion.Task;
        }

        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            await transition.WaitAsync();
            inside.Value = true;
            var errors = new List<Exception>();
            try
            {
                SceneHandle[] snapshot;
                lock (sync) { snapshot = loaded.ToArray(); errors.AddRange(cancellationErrors); }
                for (int i = snapshot.Length - 1; i >= 0; i--)
                {
                    try { await UnloadCoreAsync(snapshot[i]); } catch (Exception ex) { errors.Add(ex); }
                }
                lifetime.Dispose();
                if (errors.Count != 0) completion.TrySetException(new AggregateException("Scene service shutdown reported errors.", errors));
                else completion.TrySetResult(true);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { inside.Value = false; transition.Release(); }
        }

        private CancellationTokenSource CreateOperation(CancellationToken token)
        {
            lock (sync)
            {
                if (stopped) throw new ObjectDisposedException(nameof(SceneService));
                return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            }
        }
        private void RejectReentry()
        {
            if (inside.Value) throw new InvalidOperationException("Scene callbacks cannot reenter their service. Queue the next operation through the host dispatcher.");
        }
    }

    public sealed class SceneModule : GameModule
    {
        private readonly SceneService service;
        public SceneModule(SceneService service, string id = "scenes", IReadOnlyCollection<string> dependencies = null)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID cannot be empty.", nameof(id));
            Id = id;
            Dependencies = new List<string>(dependencies ?? new[] { "resources" }).AsReadOnly();
        }
        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); context.Services.Register(service); return Task.CompletedTask; }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => service.StopAsync();
    }
}
