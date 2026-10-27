using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using Godot;

/// <summary>Actual nodes, application notifications, dispatcher pumping and root-window close signals.</summary>
public static class GameHostLifecycleSmoke
{
    public static async Task RunAsync(Node parent)
    {
        var tree = parent.GetTree();
        bool originalPolicy = tree.AutoAcceptQuit;
        try
        {
            await VerifyPolicyReleaseAsync(parent, true);
            await VerifyPolicyReleaseAsync(parent, false);
            tree.AutoAcceptQuit = originalPolicy;
            await VerifyInitializationFailureAsync(parent);
            await VerifyStartupPauseAsync(parent);
            await VerifyLatestStartupStateAsync(parent);
            await VerifyDetachedCleanupAsync(parent);
            await VerifyApplicationWorkflowShutdownAsync(parent);
            await VerifyCoordinatedQuitAsync(parent);
            GD.Print("GODOT_HOST_LIFECYCLE_PASSED");
        }
        finally { tree.AutoAcceptQuit = originalPolicy; }
    }

    private static async Task VerifyPolicyReleaseAsync(Node parent, bool initial)
    {
        var tree = parent.GetTree();
        tree.AutoAcceptQuit = initial;
        var module = new ProbeModule();
        var probe = Add(parent, module);
        try
        {
            await UntilAsync(parent, () => probe.Started.Task.IsCompleted);
            Require(!tree.AutoAcceptQuit, "An active host takes ownership of automatic close");
            Task first = probe.ShutdownAsync();
            Require(ReferenceEquals(first, probe.ShutdownAsync()), "Repeated shutdown joins the same task");
            await first;
            Require(module.Stops == 1 && tree.AutoAcceptQuit == initial, "The last stopped host restores the original close policy");
            parent.RemoveChild(probe);
            probe.QueueFree();
            await FrameAsync(parent);
            Require(tree.AutoAcceptQuit == initial, "Removing the stopped host preserves the restored policy");
        }
        finally { await CleanupAsync(probe, module); }
    }

    private static async Task VerifyInitializationFailureAsync(Node parent)
    {
        bool original = parent.GetTree().AutoAcceptQuit;
        foreach (bool failDuringConfigure in new[] { true, false })
        {
            var module = new ProbeModule { FailDuringStart = !failDuringConfigure };
            var probe = new LifecycleProbeHost { Name = "FailedHost", Module = module, FailDuringConfigure = failDuringConfigure };
            parent.AddChild(probe);
            try
            {
                await UntilAsync(parent, () => probe.Failed.Task.IsCompleted);
                Require(probe.Failure != null && !probe.IsReady &&
                    probe.Host.State == (failDuringConfigure ? GameHostState.Stopped : GameHostState.Faulted),
                    "Failed configuration/startup closes the partial host before the application failure callback");
                Require(module.Stops == (failDuringConfigure ? 0 : 1), "Partial module startup is rolled back once");
                Require(parent.GetTree().AutoAcceptQuit == original, "Failed initialization releases close-policy ownership");
            }
            finally { await CleanupAsync(probe, module); }
        }
    }

    private static async Task VerifyStartupPauseAsync(Node parent)
    {
        var module = new ProbeModule { StartGate = NewGate() };
        var probe = Add(parent, module);
        try
        {
            Require(probe.Host.State == GameHostState.Starting && !probe.IsReady, "The regression pauses a genuinely pending startup");
            probe.Notification((int)Node.NotificationApplicationPaused);
            module.StartGate.SetResult(true);
            await UntilAsync(parent, () => probe.Started.Task.IsCompleted);
            Require(probe.Host.State == GameHostState.Paused && module.Pauses == 1 && module.Resumes == 0,
                "A pause received during startup is replayed before OnStartedAsync");
            probe.Notification((int)Node.NotificationApplicationResumed);
            probe.Notification((int)Node.NotificationApplicationResumed);
            await UntilAsync(parent, () => probe.Host.State == GameHostState.Running);
            Require(module.Resumes == 1 && probe.Failure == null, "Duplicate resume notifications do not resume a running host");

            module.PauseGate = NewGate();
            probe.Notification((int)Node.NotificationApplicationPaused);
            Require(probe.Host.State == GameHostState.Pausing, "Pause transition is genuinely asynchronous");
            probe.Notification((int)Node.NotificationApplicationResumed);
            probe.Notification((int)Node.NotificationApplicationPaused);
            module.PauseGate.SetResult(true);
            await UntilAsync(parent, () => probe.Host.State == GameHostState.Paused);
            Require(module.Pauses == 2 && module.Resumes == 1 && probe.Failure == null,
                "Notifications received during a transition converge to the latest state");
        }
        finally { await CleanupAsync(probe, module); }
    }

    private static async Task VerifyLatestStartupStateAsync(Node parent)
    {
        var module = new ProbeModule { StartGate = NewGate() };
        var probe = Add(parent, module);
        try
        {
            probe.Notification((int)Node.NotificationApplicationPaused);
            probe.Notification((int)Node.NotificationApplicationResumed);
            module.StartGate.SetResult(true);
            await UntilAsync(parent, () => probe.Started.Task.IsCompleted);
            Require(probe.Host.State == GameHostState.Running && module.Pauses == 0 && module.Resumes == 0 && probe.Failure == null,
                "Startup replays the latest desired state rather than obsolete transitions");
        }
        finally { await CleanupAsync(probe, module); }
    }

    private static async Task VerifyDetachedCleanupAsync(Node parent)
    {
        var tree = parent.GetTree();
        bool original = tree.AutoAcceptQuit;
        foreach (bool duringStartup in new[] { false, true })
        {
            var module = new ProbeModule { StartGate = duringStartup ? NewGate() : null, StopGate = NewGate(), DispatchDuringStop = true };
            var probe = Add(parent, module);
            try
            {
                if (!duringStartup) await UntilAsync(parent, () => probe.Started.Task.IsCompleted);
                parent.RemoveChild(probe);
                Require(!tree.AutoAcceptQuit && probe.Host.State == GameHostState.Stopping,
                    "A detached host retains close ownership while its cleanup is pending");
                module.StopGate.SetResult(true);
                await UntilAsync(parent, () => module.StopDispatched && probe.Host.State == GameHostState.Stopped);
                Require(module.Stops == 1 && probe.Failure == null && tree.AutoAcceptQuit == original,
                    "The coordinator pumps detached startup/running cleanup and releases ownership after one stop");
            }
            finally { await CleanupAsync(probe, module); }
        }
    }

    private static async Task VerifyCoordinatedQuitAsync(Node parent)
    {
        var tree = parent.GetTree();
        int cancellationCloses = 0, stopCloses = 0;
        Task reentrantShutdown = null;
        var fastModule = new ProbeModule();
        var slowModule = new ProbeModule
        {
            StopGate = NewGate(), DispatchDuringStop = true,
            StopStarting = () => { stopCloses++; tree.Root.EmitSignal(Window.SignalName.CloseRequested); }
        };
        var fast = Add(parent, fastModule);
        var slow = Add(parent, slowModule);
        using var cancellationClose = fast.Host.Context.LifetimeToken.Register(() =>
        {
            cancellationCloses++;
            reentrantShutdown = fast.ShutdownAsync();
            tree.Root.EmitSignal(Window.SignalName.CloseRequested);
        });
        try
        {
            await UntilAsync(parent, () => fast.Started.Task.IsCompleted && slow.Started.Task.IsCompleted);
            tree.Root.EmitSignal(Window.SignalName.CloseRequested);
            tree.Root.EmitSignal(Window.SignalName.CloseRequested);
            await UntilAsync(parent, () => fast.Host.State == GameHostState.Stopped && slowModule.Stops == 1);
            await FrameAsync(parent);
            await FrameAsync(parent);
            Require(slow.Host.State == GameHostState.Stopping && !tree.AutoAcceptQuit && cancellationCloses == 1 && stopCloses == 1 &&
                ReferenceEquals(reentrantShutdown, fast.ShutdownAsync()),
                "Synchronous cancellation/stop reentry joins the published shutdown and cannot bypass the slow host");
            slowModule.StopGate.SetResult(true);
            await Task.WhenAll(fast.ShutdownAsync(), slow.ShutdownAsync());
            Require(fastModule.Stops == 1 && slowModule.Stops == 1 && slowModule.StopDispatched &&
                fast.Host.State == GameHostState.Stopped && slow.Host.State == GameHostState.Stopped,
                "Window close joins every host exactly once, including final dispatcher cleanup");
        }
        finally
        {
            await CleanupAsync(fast, fastModule);
            await CleanupAsync(slow, slowModule);
        }
    }

    private static async Task VerifyApplicationWorkflowShutdownAsync(Node parent)
    {
        foreach (bool disposedAfterShutdown in new[] { false, true })
        {
            var module = new ProbeModule();
            var survivorModule = new ProbeModule();
            var releaseWorkflow = NewGate();
            var probe = new LifecycleProbeHost
            {
                Name = "ApplicationWorkflowProbe", Module = module,
                Workflow = async context =>
                {
                    await releaseWorkflow.Task;
                    Require(context.LifetimeToken.IsCancellationRequested, "Application workflow observes the stopped host lifetime");
                    if (disposedAfterShutdown) throw new ObjectDisposedException("Application resource released during shutdown");
                    context.LifetimeToken.ThrowIfCancellationRequested();
                }
            };
            parent.AddChild(probe);
            var survivor = Add(parent, survivorModule);
            try
            {
                await UntilAsync(parent, () => probe.Started.Task.IsCompleted && survivor.Started.Task.IsCompleted);
                await probe.ShutdownAsync();
                Require(!probe.WorkflowFinished.Task.IsCompleted && probe.Host.State == GameHostState.Stopped,
                    "Shutdown does not wait for application workflow, which may itself await ShutdownAsync");
                releaseWorkflow.SetResult(true);
                await UntilAsync(parent, () => probe.WorkflowFinished.Task.IsCompleted);
                await FrameAsync(parent);
                Require(probe.Failure == null && !probe.Failed.Task.IsCompleted && survivor.Host.State == GameHostState.Running,
                    "Late cancellation/disposal after external shutdown is not another host's startup failure");
            }
            finally
            {
                releaseWorkflow.TrySetResult(true);
                await CleanupAsync(probe, module);
                await CleanupAsync(survivor, survivorModule);
            }
        }
    }

    private static LifecycleProbeHost Add(Node parent, ProbeModule module)
    {
        var probe = new LifecycleProbeHost { Name = "LifecycleProbe", Module = module };
        parent.AddChild(probe);
        return probe;
    }

    private static async Task CleanupAsync(LifecycleProbeHost probe, ProbeModule module)
    {
        module.StartGate?.TrySetResult(true);
        module.PauseGate?.TrySetResult(true);
        module.StopGate?.TrySetResult(true);
        await probe.ShutdownAsync();
        if (GodotObject.IsInstanceValid(probe) && !probe.IsQueuedForDeletion()) probe.QueueFree();
    }

    private static async Task UntilAsync(Node parent, Func<bool> condition)
    {
        for (int frame = 0; frame < 240; frame++)
        {
            if (condition()) return;
            await FrameAsync(parent);
        }
        throw new InvalidOperationException("Godot host lifecycle smoke timed out waiting for a lifecycle state.");
    }

    private static async Task FrameAsync(Node parent) => await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    private static TaskCompletionSource<bool> NewGate() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Godot host lifecycle smoke: " + message); }

    private sealed class ProbeModule : GameModule
    {
        internal TaskCompletionSource<bool> StartGate, StopGate, PauseGate;
        internal int Pauses, Resumes, Stops;
        internal bool DispatchDuringStop, StopDispatched, FailDuringStart;
        internal Action StopStarting;

        public override async Task StartAsync(GameContext context, CancellationToken token)
        {
            if (StartGate != null) await StartGate.Task.WaitAsync(token);
            if (FailDuringStart) throw new InvalidOperationException("Expected lifecycle smoke module startup failure.");
        }
        public override async Task PauseAsync(GameContext context, CancellationToken token)
        {
            Pauses++;
            if (PauseGate != null) await PauseGate.Task.WaitAsync(token);
        }
        public override Task ResumeAsync(GameContext context, CancellationToken token)
        { Resumes++; return Task.CompletedTask; }
        public override async Task StopAsync(GameContext context, CancellationToken token)
        {
            Stops++;
            StopStarting?.Invoke();
            if (StopGate != null) await StopGate.Task;
            if (DispatchDuringStop) await context.Dispatcher.InvokeAsync(() => StopDispatched = true);
        }
    }
}
