using System;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Godot;

/// <summary>A real Godot host used only by the native lifecycle regression scenario.</summary>
public partial class LifecycleProbeHost : GameHostNode
{
    internal GameModule Module;
    internal bool FailDuringConfigure;
    internal Func<GameContext, Task> Workflow;
    internal readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<bool> WorkflowFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<Exception> Failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureHost(GameHost host)
    {
        if (FailDuringConfigure) throw new InvalidOperationException("Expected lifecycle smoke configuration failure.");
        host.AddModule(Module);
    }

    protected override async Task OnStartedAsync()
    {
        Started.TrySetResult(true);
        try { if (Workflow != null) await Workflow(Host.Context); }
        finally { WorkflowFinished.TrySetResult(true); }
    }

    protected override Task OnFailureAsync(Exception exception)
    {
        // An application can present a recoverable startup error instead of quitting the tree.
        // The regression observes this expected failure without logging it as an engine error.
        Failed.TrySetResult(exception);
        return Task.CompletedTask;
    }
}
