using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Flow;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>
    /// Transactional navigation history over a private FlowMachine. Flows own their views and resources
    /// and must support re-entry when returning or rolling back a failed navigation.
    /// </summary>
    public sealed class NavigationStack
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim transition = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> insideNavigation = new AsyncLocal<bool>();
        private readonly GameContext context;
        private readonly FlowMachine machine;
        private readonly List<IGameFlow> history = new List<IGameFlow>();
        private readonly List<Exception> cancellationErrors = new List<Exception>();
        private IGameFlow committedCurrent;
        private CancellationTokenSource request;
        private CancellationTokenSource rollback;
        private bool stopped;
        private Task stopTask;

        public NavigationStack(GameContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            machine = new FlowMachine(context);
        }

        /// <summary>The actually entered flow, which can be null while exiting, entering or after failed restoration.</summary>
        public IGameFlow Current => machine.Current;
        /// <summary>A snapshot of committed return destinations, from oldest to newest.</summary>
        public IReadOnlyList<IGameFlow> History { get { lock (sync) return Array.AsReadOnly(history.ToArray()); } }
        public bool CanGoBack { get { lock (sync) return history.Count != 0; } }
        public bool IsStopped { get { lock (sync) return stopped; } }

        public Task NavigateAsync(IGameFlow next, CancellationToken cancellationToken = default) =>
            RequestAsync(NavigationKind.Navigate, ValidateFlow(next), cancellationToken);

        public Task ReplaceAsync(IGameFlow next, CancellationToken cancellationToken = default) =>
            RequestAsync(NavigationKind.Replace, ValidateFlow(next), cancellationToken);

        /// <summary>Returns false when no return destination exists.</summary>
        public Task<bool> BackAsync(CancellationToken cancellationToken = default) =>
            RequestAsync(NavigationKind.Back, null, cancellationToken);

        public void Tick(double deltaSeconds) => machine.Tick(deltaSeconds);

        private Task<bool> RequestAsync(NavigationKind kind, IGameFlow next, CancellationToken cancellationToken)
        {
            RejectReentry();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<bool>(cancellationToken);
            if (context.LifetimeToken.IsCancellationRequested) return Task.FromCanceled<bool>(context.LifetimeToken);
            CancellationTokenSource previous;
            CancellationTokenSource previousRollback;
            CancellationTokenSource nextRequest;
            lock (sync)
            {
                if (stopped) throw new ObjectDisposedException(nameof(NavigationStack));
                nextRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.LifetimeToken);
                previous = request;
                previousRollback = rollback;
                request = nextRequest;
            }
            Cancel(previous);
            Cancel(previousRollback);
            return ChangeAsync(kind, next, nextRequest);
        }

        private async Task<bool> ChangeAsync(NavigationKind kind, IGameFlow next, CancellationTokenSource operation)
        {
            bool enteredGate = false;
            IGameFlow previous = null;
            try
            {
                // An already scheduled request must observe prior cleanup before handling cancellation.
                // Otherwise cancelling the newest queued request can leave every older request superseded,
                // with nobody responsible for restoring the last committed destination.
                await transition.WaitAsync();
                enteredGate = true;
                insideNavigation.Value = true;
                lock (sync)
                {
                    previous = committedCurrent;
                    operation.Token.ThrowIfCancellationRequested();
                    if (kind == NavigationKind.Back)
                    {
                        if (history.Count == 0) return false;
                        next = history[history.Count - 1];
                    }
                }
                if (!ReferenceEquals(machine.Current, next)) await machine.SwitchAsync(next, operation.Token);
                lock (sync)
                {
                    if (stopped || !ReferenceEquals(request, operation))
                        throw new OperationCanceledException("Navigation was superseded or stopped.", operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    if (kind == NavigationKind.Back) history.RemoveAt(history.Count - 1);
                    else if (kind == NavigationKind.Navigate && previous != null && !ReferenceEquals(previous, next)) history.Add(previous);
                    committedCurrent = next;
                }
                return true;
            }
            catch (Exception failure)
            {
                if (enteredGate && previous != null && !ReferenceEquals(machine.Current, previous))
                {
                    try { await RestoreAsync(previous, operation); }
                    catch (Exception restoration)
                    {
                        // Being superseded during restoration is ordinary cancellation; genuine restoration failures are preserved.
                        if (!(restoration is OperationCanceledException) || IsLatest(operation))
                            throw new AggregateException("Navigation and restoration both failed.", failure, restoration);
                    }
                }
                throw;
            }
            finally
            {
                if (enteredGate)
                {
                    insideNavigation.Value = false;
                    transition.Release();
                }
                lock (sync) { if (ReferenceEquals(request, operation)) request = null; }
                operation.Dispose();
            }
        }

        private async Task RestoreAsync(IGameFlow previous, CancellationTokenSource operation)
        {
            CancellationTokenSource restoration;
            lock (sync)
            {
                if (stopped || !ReferenceEquals(request, operation) || context.LifetimeToken.IsCancellationRequested) return;
                restoration = CancellationTokenSource.CreateLinkedTokenSource(context.LifetimeToken);
                rollback = restoration;
            }
            try { await machine.SwitchAsync(previous, restoration.Token); }
            finally
            {
                lock (sync) { if (ReferenceEquals(rollback, restoration)) rollback = null; }
                restoration.Dispose();
            }
        }

        private bool IsLatest(CancellationTokenSource operation)
        {
            lock (sync) return !stopped && ReferenceEquals(request, operation) && !context.LifetimeToken.IsCancellationRequested;
        }

        public Task StopAsync()
        {
            RejectReentry();
            CancellationTokenSource active;
            CancellationTokenSource restoring;
            TaskCompletionSource<bool> completion;
            lock (sync)
            {
                if (stopTask != null) return stopTask;
                stopped = true;
                active = request;
                restoring = rollback;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stopTask = completion.Task;
            }
            Cancel(active);
            Cancel(restoring);
            _ = CompleteStopAsync(completion);
            return completion.Task;
        }

        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            insideNavigation.Value = true;
            try
            {
                try { await machine.StopAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                await transition.WaitAsync();
                try
                {
                    lock (sync)
                    {
                        history.Clear();
                        committedCurrent = null;
                        failures.AddRange(cancellationErrors);
                    }
                }
                finally { transition.Release(); }
                if (failures.Count != 0) completion.TrySetException(new AggregateException("Navigation shutdown reported errors.", failures));
                else completion.TrySetResult(true);
            }
            catch (Exception failure) { completion.TrySetException(failure); }
            finally { insideNavigation.Value = false; }
        }

        private void Cancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception failure) { lock (sync) cancellationErrors.Add(failure); }
        }

        private void RejectReentry()
        {
            if (insideNavigation.Value)
                throw new InvalidOperationException("Flow callbacks cannot reenter navigation. Queue navigation through the host dispatcher.");
        }

        private static IGameFlow ValidateFlow(IGameFlow flow)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            if (string.IsNullOrWhiteSpace(flow.Id)) throw new ArgumentException("Flow ID cannot be empty.", nameof(flow));
            return flow;
        }

        private enum NavigationKind { Navigate, Replace, Back }
    }
}
