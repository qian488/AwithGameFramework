using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Core.Flow
{
    public interface IGameFlow
    {
        string Id { get; }
        Task EnterAsync(GameContext context, CancellationToken cancellationToken);
        Task ExitAsync(GameContext context, CancellationToken cancellationToken);
    }

    public interface IGameTickFlow
    {
        void Tick(GameContext context, double deltaSeconds);
    }

    public abstract class GameFlow : IGameFlow, IGameTickFlow
    {
        public virtual string Id => GetType().FullName;
        public virtual Task EnterAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task ExitAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual void Tick(GameContext context, double deltaSeconds) { }
    }

    /// <summary>Serial exit/enter transitions. A newer switch cancels an unfinished older request.</summary>
    public sealed class FlowMachine
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _transition = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> _insideTransition = new AsyncLocal<bool>();
        private readonly GameContext _context;
        private readonly List<Exception> _cancellationErrors = new List<Exception>();
        private CancellationTokenSource _request;
        private IGameFlow _current;
        private bool _stopped;
        private Task _stopTask;

        public FlowMachine(GameContext context) => _context = context ?? throw new ArgumentNullException(nameof(context));
        public IGameFlow Current { get { lock (_sync) return _current; } }

        public Task SwitchAsync(IGameFlow next, CancellationToken cancellationToken = default)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (string.IsNullOrWhiteSpace(next.Id)) throw new ArgumentException("Flow ID cannot be empty.", nameof(next));
            RejectReentry();
            // A cancelled request must not evict an otherwise healthy current flow or cancel another transition.
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            if (_context.LifetimeToken.IsCancellationRequested) return Task.FromCanceled(_context.LifetimeToken);
            CancellationTokenSource previous;
            CancellationTokenSource request;
            lock (_sync)
            {
                if (_stopped) throw new ObjectDisposedException(nameof(FlowMachine));
                request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _context.LifetimeToken);
                previous = _request;
                _request = request;
            }
            CancelRequest(previous);
            return SwitchCoreAsync(next, request);
        }

        private async Task SwitchCoreAsync(IGameFlow next, CancellationTokenSource request)
        {
            bool enteredGate = false;
            bool entering = false;
            try
            {
                await _transition.WaitAsync(request.Token);
                enteredGate = true;
                _insideTransition.Value = true;
                request.Token.ThrowIfCancellationRequested();
                IGameFlow previous;
                lock (_sync)
                {
                    previous = _current;
                    _current = null;
                }
                if (previous != null) await previous.ExitAsync(_context, CancellationToken.None);
                request.Token.ThrowIfCancellationRequested();
                entering = true;
                await next.EnterAsync(_context, request.Token);
                lock (_sync)
                {
                    request.Token.ThrowIfCancellationRequested();
                    _current = next;
                }
                entering = false;
            }
            catch (Exception failure)
            {
                if (entering)
                {
                    try { await next.ExitAsync(_context, CancellationToken.None); }
                    catch (Exception cleanup) { throw new AggregateException($"Flow '{next.Id}' failed to enter and exit.", failure, cleanup); }
                }
                throw;
            }
            finally
            {
                if (enteredGate)
                {
                    _insideTransition.Value = false;
                    _transition.Release();
                }
                lock (_sync) { if (ReferenceEquals(_request, request)) _request = null; }
                request.Dispose();
            }
        }

        public void Tick(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (_context.LifetimeToken.IsCancellationRequested) return;
            if (Current is IGameTickFlow tickable) tickable.Tick(_context, deltaSeconds);
        }

        public Task StopAsync()
        {
            RejectReentry();
            CancellationTokenSource request;
            TaskCompletionSource<bool> completion;
            lock (_sync)
            {
                if (_stopTask != null) return _stopTask;
                _stopped = true;
                request = _request;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = completion.Task;
            }
            CancelRequest(request);
            _ = CompleteStopAsync(completion);
            return completion.Task;
        }

        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            await _transition.WaitAsync();
            _insideTransition.Value = true;
            try
            {
                IGameFlow current;
                lock (_sync)
                {
                    current = _current;
                    _current = null;
                }
                try { if (current != null) await current.ExitAsync(_context, CancellationToken.None); }
                catch (Exception ex) { lock (_sync) _cancellationErrors.Add(ex); }
                lock (_sync)
                {
                    if (_cancellationErrors.Count == 0) completion.TrySetResult(true);
                    else completion.TrySetException(new AggregateException("Flow shutdown reported errors.", _cancellationErrors.ToArray()));
                }
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                _insideTransition.Value = false;
                _transition.Release();
            }
        }

        private void CancelRequest(CancellationTokenSource request)
        {
            if (request == null) return;
            try { request.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { lock (_sync) _cancellationErrors.Add(ex); }
        }

        private void RejectReentry()
        {
            if (_insideTransition.Value) throw new InvalidOperationException("Flow callbacks cannot reenter their own machine. Request transitions through the host dispatcher.");
        }
    }
}
