using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>Godot owns nodes/rendering; this node drives an isolated, engine-independent host.</summary>
    public partial class GameHostNode : Node
    {
        public GameHost Host { get; private set; }
        public bool IsReady { get; private set; }
        public Exception Failure { get; private set; }
        private bool closing;
        private bool applicationPaused;
        private ulong lastProcessMicroseconds;
        private readonly SemaphoreSlim pauseGate = new SemaphoreSlim(1, 1);
        private GameHostExitCoordinator.Registration exitRegistration;
        private SceneTree owningTree;
        private Task shutdownTask;

        protected virtual void ConfigureHost(GameHost host) { }
        /// <summary>Application workflow, not a module lifecycle hook. Observe Host.Context.LifetimeToken; shutdown does not join this callback.</summary>
        protected virtual Task OnStartedAsync() => Task.CompletedTask;
        /// <summary>Override to present a recoverable startup error. The default closes every host before quitting.</summary>
        protected virtual Task OnFailureAsync(Exception exception)
        {
            GD.PushError(exception.ToString());
            return GameHostExitCoordinator.RequestQuitAsync(owningTree, 1);
        }

        public override async void _Ready()
        {
            if (closing) return;
            ProcessMode = ProcessModeEnum.Always;
            lastProcessMicroseconds = global::Godot.Time.GetTicksUsec();
            Host = new GameHost();
            bool applicationWorkflowStarted = false;
            try
            {
                owningTree = GetTree();
                exitRegistration = GameHostExitCoordinator.Register(owningTree, Host, ShutdownAsync);
                ConfigureHost(Host);
                await Host.StartAsync();
                if (closing) return;
                IsReady = true;
                await ApplyPauseStateAsync();
                if (closing) return;
                applicationWorkflowStarted = true;
                await OnStartedAsync();
            }
            catch (OperationCanceledException) when (closing) { }
            catch (ObjectDisposedException) when (closing && applicationWorkflowStarted) { }
            catch (Exception exception)
            {
                Failure = exception;
                try { await ShutdownAsync(); }
                catch (Exception cleanup) { Failure = new AggregateException(exception, cleanup); }
                try { await OnFailureAsync(Failure); }
                catch (Exception callback)
                {
                    GD.PushError(callback.ToString());
                    await GameHostExitCoordinator.RequestQuitAsync(owningTree, 1);
                }
            }
        }

        public override void _Process(double delta)
        {
            // Godot's delta already includes Engine.TimeScale. The host owns its own time scale.
            var now = global::Godot.Time.GetTicksUsec();
            var unscaledSeconds = (now - lastProcessMicroseconds) / 1_000_000.0;
            lastProcessMicroseconds = now;
            if (Host != null)
            {
                try { Host.Tick(unscaledSeconds); }
                catch (Exception exception) { Failure = exception; GD.PushError(exception.ToString()); }
            }
        }

        public override async void _Notification(int what)
        {
            if (what == NotificationApplicationPaused) applicationPaused = true;
            else if (what == NotificationApplicationResumed)
            {
                applicationPaused = false;
                lastProcessMicroseconds = global::Godot.Time.GetTicksUsec();
            }
            else return;
            if (!IsReady || closing) return;
            try { await ApplyPauseStateAsync(); }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception exception) { Failure = exception; GD.PushError(exception.ToString()); }
        }

        private async Task ApplyPauseStateAsync()
        {
            await pauseGate.WaitAsync();
            try
            {
                while (!closing)
                {
                    if (applicationPaused && Host.State == GameHostState.Running) await Host.PauseAsync();
                    else if (!applicationPaused && Host.State == GameHostState.Paused) await Host.ResumeAsync();
                    else break;
                }
            }
            finally { pauseGate.Release(); }
        }

        /// <summary>Await before removing this node or calling SceneTree.Quit.</summary>
        public Task ShutdownAsync()
        {
            if (shutdownTask != null) return shutdownTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Stop synchronously cancels the lifetime token. Callbacks may request shutdown/close again.
            shutdownTask = completion.Task;
            closing = true;
            IsReady = false;
            _ = CompleteShutdownAsync(completion);
            return shutdownTask;
        }

        private async Task CompleteShutdownAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                try { if (Host != null) await Host.StopAsync(); }
                finally
                {
                    exitRegistration?.Release();
                    exitRegistration = null;
                }
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        public override void _ExitTree()
        {
            if (exitRegistration != null)
            {
                // The tree coordinator keeps pumping a detached host until asynchronous cleanup finishes.
                // Explicitly awaited shutdown is still required before destroying native resources it uses.
                exitRegistration.Detach();
                _ = ObserveExitAsync(ShutdownAsync());
            }
        }

        private static async Task ObserveExitAsync(Task stop)
        {
            try { await stop; }
            catch (Exception exception) { GD.PushError(exception.ToString()); }
        }
    }
}
