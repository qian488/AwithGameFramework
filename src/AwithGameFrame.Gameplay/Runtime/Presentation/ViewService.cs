using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>Explicit view ownership, ordering, focus and logical input. Call from the host synchronization context.</summary>
    public sealed class ViewService
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim transition = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> inside = new AsyncLocal<bool>();
        private readonly CancellationTokenSource lifetime;
        private readonly IViewFactory factory;
        private readonly ResourceStore resources;
        private readonly List<ViewHandle> views = new List<ViewHandle>();
        private readonly List<Exception> cancellationErrors = new List<Exception>();
        private ViewHandle focused;
        private long nextId;
        private bool stopped, routing;
        private Task stopTask;

        public ViewService(GameContext context, IViewFactory factory, ResourceStore resources)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.LifetimeToken);
        }

        public ViewHandle Focused { get { lock (sync) return focused; } }
        public IReadOnlyList<ViewHandle> OpenViews { get { lock (sync) return Array.AsReadOnly(views.ToArray()); } }
        internal bool Contains(ViewHandle handle) { lock (sync) return views.Contains(handle); }

        public async Task<ViewHandle> OpenAsync(ViewRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RejectReentry();
            using var operation = CreateOperation(cancellationToken);
            await transition.WaitAsync(operation.Token);
            inside.Value = true;
            ResourceScope scope = null;
            IView view = null;
            ViewHandle added = null;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    foreach (var existing in views)
                    {
                        if (existing.Request.Key != request.Key) continue;
                        if (existing.Request.Layer != request.Layer || existing.Request.Modal != request.Modal || existing.Request.TakesFocus != request.TakesFocus)
                            throw new InvalidOperationException($"View '{request.Key}' is already open with different presentation options.");
                        return existing;
                    }
                }
                scope = new ResourceScope(resources);
                view = await factory.CreateAsync(request, scope, operation.Token);
                if (view == null) throw new InvalidOperationException($"View factory returned null for '{request.Key}'.");
                operation.Token.ThrowIfCancellationRequested();
                await view.ShowAsync(operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (stopped) throw new OperationCanceledException(operation.Token);
                    added = new ViewHandle(this, ++nextId, request, view, scope);
                    views.Add(added);
                }
                ApplyStates();
                return added;
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                if (added != null) lock (sync) views.Remove(added);
                if (view != null) await CleanupViewAsync(view, scope, errors);
                else if (scope != null) { try { await scope.DisposeAsync(); } catch (Exception ex) { errors.Add(ex); } }
                if (added != null) { try { ApplyStates(); } catch (Exception ex) { errors.Add(ex); } }
                if (errors.Count > 1) throw new AggregateException("View opening and cleanup reported errors.", errors);
                throw;
            }
            finally { inside.Value = false; transition.Release(); }
        }

        public async Task CloseAsync(ViewHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            RejectReentry();
            await transition.WaitAsync();
            inside.Value = true;
            try
            {
                bool removed;
                lock (sync) removed = views.Remove(handle);
                if (!removed) return;
                var errors = new List<Exception>();
                try { ApplyStates(); } catch (Exception ex) { errors.Add(ex); }
                await CleanupViewAsync(handle.View, handle.Resources, errors);
                if (errors.Count != 0) throw new AggregateException("View closing reported errors.", errors);
            }
            finally { inside.Value = false; transition.Release(); }
        }

        /// <summary>Routes top to bottom. An unhandled modal still consumes the action, shielding gameplay beneath it.</summary>
        public bool RouteInput(ViewInput input)
        {
            ViewHandle[] snapshot;
            lock (sync)
            {
                if (stopped) return false;
                if (routing) throw new InvalidOperationException("View input cannot recursively route input.");
                routing = true;
                snapshot = views.ToArray();
            }
            try
            {
                for (int i = snapshot.Length - 1; i >= 0; i--)
                {
                    if (!Contains(snapshot[i])) continue;
                    if (snapshot[i].View.HandleInput(input)) return true;
                    if (snapshot[i].Request.Modal) return true;
                }
                return false;
            }
            finally { lock (sync) routing = false; }
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
                ViewHandle[] snapshot;
                lock (sync)
                {
                    snapshot = views.ToArray(); views.Clear(); focused = null;
                    errors.AddRange(cancellationErrors);
                }
                for (int i = snapshot.Length - 1; i >= 0; i--) await CleanupViewAsync(snapshot[i].View, snapshot[i].Resources, errors);
                lifetime.Dispose();
                if (errors.Count != 0) completion.TrySetException(new AggregateException("View service shutdown reported errors.", errors));
                else completion.TrySetResult(true);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { inside.Value = false; transition.Release(); }
        }

        private CancellationTokenSource CreateOperation(CancellationToken token)
        {
            lock (sync)
            {
                if (stopped) throw new ObjectDisposedException(nameof(ViewService));
                return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            }
        }

        private void ApplyStates()
        {
            ViewHandle[] snapshot;
            IReadOnlyList<ViewLayoutEntry> layout;
            lock (sync)
            {
                var facts = new List<ViewLayoutItem>(views.Count);
                var handles = new Dictionary<long, ViewHandle>();
                foreach (var handle in views)
                {
                    facts.Add(new ViewLayoutItem(handle.InstanceId, handle.Request));
                    handles.Add(handle.InstanceId, handle);
                }
                layout = ViewLayoutRules.Calculate(facts);
                snapshot = new ViewHandle[layout.Count];
                views.Clear(); focused = null;
                for (int i = 0; i < layout.Count; i++)
                {
                    var handle = handles[layout[i].InstanceId];
                    snapshot[i] = handle; views.Add(handle);
                    if (layout[i].State.IsFocused) focused = handle;
                }
            }
            var errors = new List<Exception>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].View.ApplyState(layout[i].State); }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count != 0) throw new AggregateException("View state update reported errors.", errors);
        }

        private static async Task CleanupViewAsync(IView view, ResourceScope scope, List<Exception> errors)
        {
            try { await view.HideAsync(CancellationToken.None); } catch (Exception ex) { errors.Add(ex); }
            try { await view.DisposeAsync(); } catch (Exception ex) { errors.Add(ex); }
            if (scope != null) { try { await scope.DisposeAsync(); } catch (Exception ex) { errors.Add(ex); } }
        }

        private void RejectReentry()
        {
            if (inside.Value) throw new InvalidOperationException("View callbacks cannot reenter their service. Queue the next operation through the host dispatcher.");
            lock (sync)
                if (routing) throw new InvalidOperationException("View input callbacks cannot change view lifetime. Queue the operation through the host dispatcher.");
        }
    }
}
