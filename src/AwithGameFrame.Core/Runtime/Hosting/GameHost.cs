using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Timing;

namespace AwithGameFrame.Core.Hosting
{
    /// <summary>A single application session. Lifecycle calls belong to the host thread; dispatch worker requests.</summary>
    public sealed class GameHost
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _lifecycle = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> _insideLifecycle = new AsyncLocal<bool>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<IGameModule> _modules = new List<IGameModule>();
        private readonly List<IGameModule> _started = new List<IGameModule>();
        private readonly int _dispatchBudget;
        private GameHostState _state;
        private bool _stopRequested;
        private bool _ticking;
        private Task _stopTask;

        public GameHost(int dispatchBudgetPerTick = 64, int dispatcherCapacity = 4096, int timerCallbackBudgetPerTick = 64)
        {
            if (dispatchBudgetPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(dispatchBudgetPerTick));
            if (timerCallbackBudgetPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(timerCallbackBudgetPerTick));
            _dispatchBudget = dispatchBudgetPerTick;
            Services = new GameServices();
            Dispatcher = new MainThreadDispatcher(dispatcherCapacity);
            Clock = new GameClock();
            Timers = new TimerScheduler(Clock, timerCallbackBudgetPerTick, _lifetime.Token);
            Events = new EventBus();
            Services.Register(Clock);
            Services.Register(Timers);
            Services.Register(Events);
            Context = new GameContext(Services, Dispatcher, _lifetime.Token, Clock, Timers, Events);
        }

        public GameServices Services { get; }
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public event Action<HostEvent> Lifecycle;
        public MainThreadDispatcher Dispatcher { get; }
        public GameClock Clock { get; }
        public TimerScheduler Timers { get; }
        public EventBus Events { get; }
        public GameContext Context { get; }
        public GameHostState State { get { lock (_sync) return _state; } }

        public void AddModule(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (string.IsNullOrWhiteSpace(module.Id)) throw new ArgumentException("Module ID cannot be empty.", nameof(module));
            lock (_sync)
            {
                if (_state != GameHostState.Created) throw new InvalidOperationException("Register modules before starting the host.");
                foreach (var existing in _modules)
                    if (existing.Id == module.Id) throw new InvalidOperationException($"Duplicate module ID '{module.Id}'.");
                _modules.Add(module);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            RejectLifecycleReentry();
            lock (_sync)
            {
                if (_state != GameHostState.Created) throw new InvalidOperationException("A GameHost can only start once. Create a new host for a new session.");
                _state = GameHostState.Starting;
            }
            return StartCoreAsync(cancellationToken);
        }

        private async Task StartCoreAsync(CancellationToken cancellationToken)
        {
            await _lifecycle.WaitAsync();
            _insideLifecycle.Value = true;
            var elapsed = Stopwatch.StartNew();
            Emit(HostPhase.Start, HostEventStatus.Started);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            try
            {
                var ordered = OrderModules();
                foreach (var module in ordered)
                {
                    startup.Token.ThrowIfCancellationRequested();
                    lock (_sync) _started.Add(module); // Partial initialization must also be rolled back.
                    await RunModuleAsync(module, HostPhase.Start, () => module.StartAsync(Context, startup.Token));
                }
                startup.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_stopRequested) throw new OperationCanceledException(_lifetime.Token);
                    _state = GameHostState.Running;
                }
                Emit(HostPhase.Start, HostEventStatus.Completed, elapsed: elapsed.Elapsed.TotalSeconds);
            }
            catch (Exception failure)
            {
                var errors = new List<Exception>();
                CancelSession(errors);
                await StopStartedAsync(errors, true);
                DisposeSessionServices(errors);
                Dispatcher.Dispose();
                Services.Close();
                lock (_sync) _state = _stopRequested ? GameHostState.Stopped : GameHostState.Faulted;
                if (errors.Count != 0)
                {
                    errors.Insert(0, failure);
                    var aggregate = new AggregateException("Host startup failed and cleanup reported errors.", errors);
                    Emit(HostPhase.Start, HostEventStatus.Failed, elapsed: elapsed.Elapsed.TotalSeconds, error: aggregate);
                    throw aggregate;
                }
                Emit(HostPhase.Start, HostEventStatus.Failed, elapsed: elapsed.Elapsed.TotalSeconds, error: failure);
                throw;
            }
            finally
            {
                _insideLifecycle.Value = false;
                _lifecycle.Release();
            }
        }

        public Task PauseAsync(CancellationToken cancellationToken = default) => ChangePauseAsync(true, cancellationToken);
        public Task ResumeAsync(CancellationToken cancellationToken = default) => ChangePauseAsync(false, cancellationToken);

        private async Task ChangePauseAsync(bool pause, CancellationToken cancellationToken)
        {
            RejectLifecycleReentry();
            await _lifecycle.WaitAsync(cancellationToken);
            _insideLifecycle.Value = true;
            var changed = new List<IGamePauseModule>();
            var prior = pause ? GameHostState.Running : GameHostState.Paused;
            var phase = pause ? HostPhase.Pause : HostPhase.Resume;
            var elapsed = Stopwatch.StartNew();
            bool operationStarted = false;
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            try
            {
                lock (_sync)
                {
                    if (_state != prior) throw new InvalidOperationException($"Cannot {(pause ? "pause" : "resume")} a host in state {_state}.");
                    _state = pause ? GameHostState.Pausing : GameHostState.Resuming;
                }
                operationStarted = true;
                Emit(phase, HostEventStatus.Started);
                foreach (var module in _started)
                {
                    transition.Token.ThrowIfCancellationRequested();
                    if (!(module is IGamePauseModule pausable)) continue;
                    changed.Add(pausable);
                    await RunModuleAsync(module, phase, () => pause ? pausable.PauseAsync(Context, transition.Token) : pausable.ResumeAsync(Context, transition.Token));
                }
                transition.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_stopRequested) throw new OperationCanceledException(_lifetime.Token);
                    _state = pause ? GameHostState.Paused : GameHostState.Running;
                }
                Emit(phase, HostEventStatus.Completed, elapsed: elapsed.Elapsed.TotalSeconds);
            }
            catch (Exception failure)
            {
                // An invalid state request has not changed modules and must not rewrite the host state.
                if (State == (pause ? GameHostState.Pausing : GameHostState.Resuming))
                {
                    var errors = new List<Exception>();
                    for (int i = changed.Count - 1; i >= 0; i--)
                    {
                        if (StopRequested) break;
                        try
                        {
                            var module = (IGameModule)changed[i];
                            var pausable = changed[i];
                            await RunModuleAsync(module, pause ? HostPhase.Resume : HostPhase.Pause,
                                () => pause ? pausable.ResumeAsync(Context, CancellationToken.None) : pausable.PauseAsync(Context, CancellationToken.None), true);
                        }
                        catch (Exception ex) { errors.Add(ex); }
                    }
                    if (errors.Count != 0)
                    {
                        CancelSession(errors);
                        await StopStartedAsync(errors, true);
                        DisposeSessionServices(errors);
                        Dispatcher.Dispose();
                        Services.Close();
                        lock (_sync) _state = _stopRequested ? GameHostState.Stopped : GameHostState.Faulted;
                        errors.Insert(0, failure);
                        var aggregate = new AggregateException("Pause/resume failed and its rollback reported errors.", errors);
                        Emit(phase, HostEventStatus.Failed, elapsed: elapsed.Elapsed.TotalSeconds, error: aggregate);
                        throw aggregate;
                    }
                    lock (_sync) { if (!_stopRequested) _state = prior; }
                }
                if (operationStarted) Emit(phase, HostEventStatus.Failed, elapsed: elapsed.Elapsed.TotalSeconds, error: failure);
                throw;
            }
            finally
            {
                _insideLifecycle.Value = false;
                _lifecycle.Release();
            }
        }

        /// <summary>Cancels outstanding session work immediately, then waits for lifecycle callbacks and reverse cleanup.</summary>
        public Task StopAsync()
        {
            RejectLifecycleReentry();
            TaskCompletionSource<bool> completion;
            lock (_sync)
            {
                if (_stopTask != null) return _stopTask;
                if (_state == GameHostState.Stopped || _state == GameHostState.Faulted) return Task.CompletedTask;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = completion.Task;
                _stopRequested = true;
                _state = GameHostState.Stopping;
            }
            _ = CompleteStopAsync(completion);
            return completion.Task;
        }

        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            var elapsed = Stopwatch.StartNew();
            Emit(HostPhase.Stop, HostEventStatus.Started);
            var errors = new List<Exception>();
            CancelSession(errors);
            await _lifecycle.WaitAsync();
            _insideLifecycle.Value = true;
            try
            {
                await StopStartedAsync(errors);
                DisposeSessionServices(errors);
                Dispatcher.Dispose();
                Services.Close();
                lock (_sync) _state = GameHostState.Stopped;
                if (errors.Count != 0)
                {
                    var aggregate = new AggregateException("Host shutdown reported errors.", errors);
                    Emit(HostPhase.Stop, HostEventStatus.Failed, elapsed: elapsed.Elapsed.TotalSeconds, error: aggregate);
                    completion.TrySetException(aggregate);
                }
                else
                {
                    Emit(HostPhase.Stop, HostEventStatus.Completed, elapsed: elapsed.Elapsed.TotalSeconds);
                    completion.TrySetResult(true);
                }
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                _insideLifecycle.Value = false;
                _lifecycle.Release();
            }
        }

        /// <summary>Unscaled seconds. Simulation ticks run only while Running; optional cleanup maintenance runs while Paused.</summary>
        public void Tick(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Dispatcher.CheckAccess) throw new InvalidOperationException("Tick must run on the host's creating thread.");
            lock (_sync)
            {
                if (_ticking) throw new InvalidOperationException("Tick cannot be called recursively.");
                _ticking = true;
            }
            try
            {
                Dispatcher.RunPending(_dispatchBudget);
                IGameModule[] modules;
                List<Exception> errors = null;
                if (State == GameHostState.Running)
                {
                    Clock.Advance(deltaSeconds);
                    try { Timers.RunDue(() => State == GameHostState.Running); }
                    catch (AggregateException aggregate) { (errors ??= new List<Exception>()).AddRange(aggregate.InnerExceptions); }
                }
                else Clock.Freeze();
                var scaledDelta = Clock.ScaledDeltaTime;
                lock (_sync) modules = _state == GameHostState.Running ? _started.ToArray() : Array.Empty<IGameModule>();
                foreach (var module in modules)
                {
                    if (State != GameHostState.Running) break;
                    if (!(module is IGameTickModule tickable)) continue;
                    try { tickable.Tick(Context, scaledDelta); }
                    catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
                }
                lock (_sync) modules = _state == GameHostState.Paused ? _started.ToArray() : Array.Empty<IGameModule>();
                foreach (var module in modules)
                {
                    if (State != GameHostState.Paused) break;
                    if (!(module is IGameMaintenanceModule maintenance)) continue;
                    try { maintenance.Maintain(Context); }
                    catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
                }
                if (errors != null) throw new AggregateException("One or more timers or modules failed during Tick.", errors);
            }
            finally { lock (_sync) _ticking = false; }
        }

        private bool StopRequested { get { lock (_sync) return _stopRequested; } }

        private void RejectLifecycleReentry()
        {
            if (_insideLifecycle.Value) throw new InvalidOperationException("A module callback cannot reenter its host's lifecycle. Request changes through the dispatcher instead.");
        }

        private void CancelSession(List<Exception> errors)
        {
            try { _lifetime.Cancel(); }
            catch (Exception ex) { errors.Add(ex); }
            Clock.Stop();
            try { Timers.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
            Dispatcher.CancelPending();
        }

        private void DisposeSessionServices(List<Exception> errors)
        {
            try { Timers.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
            try { Events.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
            Clock.Stop();
        }

        private async Task StopStartedAsync(List<Exception> errors, bool rollback = false)
        {
            IGameModule[] started;
            lock (_sync)
            {
                started = _started.ToArray();
                _started.Clear();
            }
            for (int i = started.Length - 1; i >= 0; i--)
            {
                var module = started[i];
                try { await RunModuleAsync(module, HostPhase.Stop, () => module.StopAsync(Context, CancellationToken.None), rollback); }
                catch (Exception ex) { errors.Add(new InvalidOperationException($"Module '{started[i].Id}' failed to stop.", ex)); }
            }
        }

        private async Task RunModuleAsync(IGameModule module, HostPhase phase, Func<Task> callback, bool rollback = false)
        {
            var elapsed = Stopwatch.StartNew();
            Emit(phase, HostEventStatus.Started, module.Id, rollback: rollback);
            try
            {
                await callback();
                Emit(phase, HostEventStatus.Completed, module.Id, elapsed.Elapsed.TotalSeconds, rollback: rollback);
            }
            catch (Exception ex)
            {
                Emit(phase, HostEventStatus.Failed, module.Id, elapsed.Elapsed.TotalSeconds, ex, rollback);
                throw;
            }
        }

        private void Emit(HostPhase phase, HostEventStatus status, string moduleId = null, double elapsed = 0, Exception error = null, bool rollback = false)
        {
            var listeners = Lifecycle;
            if (listeners == null) return;
            var item = new HostEvent(Id, phase, status, moduleId, elapsed, State, error, rollback);
            foreach (Action<HostEvent> listener in listeners.GetInvocationList())
            {
                try { listener(item); }
                catch (Exception) { /* Diagnostics cannot interrupt application lifecycle or cleanup. */ }
            }
        }

        private List<IGameModule> OrderModules()
        {
            var modules = new Dictionary<string, IGameModule>(StringComparer.Ordinal);
            foreach (var module in _modules) modules.Add(module.Id, module);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            var ordered = new List<IGameModule>();
            foreach (var module in _modules) Visit(module, modules, visited, path, ordered);
            return ordered;
        }

        private static void Visit(IGameModule module, Dictionary<string, IGameModule> modules, HashSet<string> visited, List<string> path, List<IGameModule> ordered)
        {
            if (visited.Contains(module.Id)) return;
            if (path.Contains(module.Id)) throw new InvalidOperationException($"Module dependency cycle: {string.Join(" -> ", path)} -> {module.Id}.");
            path.Add(module.Id);
            if (module.Dependencies != null)
            {
                foreach (var dependency in module.Dependencies)
                {
                    if (dependency == null || !modules.TryGetValue(dependency, out var required))
                        throw new InvalidOperationException($"Module '{module.Id}' requires missing module '{dependency ?? "<null>"}'.");
                    Visit(required, modules, visited, path, ordered);
                }
            }
            path.RemoveAt(path.Count - 1);
            visited.Add(module.Id);
            ordered.Add(module);
        }
    }
}
