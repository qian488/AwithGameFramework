using System;
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

        protected virtual void ConfigureHost(GameHost host) { }
        protected virtual Task OnStartedAsync() => Task.CompletedTask;

        public override async void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            Host = new GameHost();
            try
            {
                GetTree().AutoAcceptQuit = false;
                GetTree().Root.CloseRequested += CloseAsync;
                ConfigureHost(Host);
                await Host.StartAsync();
                if (closing) return;
                IsReady = true;
                await OnStartedAsync();
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception exception)
            {
                Failure = exception;
                GD.PushError(exception.ToString());
                try { await ShutdownAsync(); }
                catch (Exception cleanup) { GD.PushError(cleanup.ToString()); }
                if (IsInsideTree()) GetTree().Quit(1);
            }
        }

        public override void _Process(double delta)
        {
            if (Host != null)
            {
                try { Host.Tick(delta); }
                catch (Exception exception) { Failure = exception; GD.PushError(exception.ToString()); }
            }
        }

        public override async void _Notification(int what)
        {
            if (!IsReady || closing) return;
            try
            {
                if (what == NotificationApplicationPaused) await Host.PauseAsync();
                else if (what == NotificationApplicationResumed) await Host.ResumeAsync();
            }
            catch (Exception exception) { Failure = exception; GD.PushError(exception.ToString()); }
        }

        /// <summary>Await before removing this node or calling SceneTree.Quit.</summary>
        public async Task ShutdownAsync()
        {
            closing = true;
            IsReady = false;
            if (Host != null) await Host.StopAsync();
        }

        private async void CloseAsync()
        {
            try { await ShutdownAsync(); GetTree().Quit(); }
            catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
        }

        public override void _ExitTree()
        {
            var tree = GetTree();
            if (tree != null) tree.Root.CloseRequested -= CloseAsync;
            if (Host != null && !closing)
            {
                // Godot cannot await _ExitTree. Explicit shutdown/close is the guaranteed async path.
                var stop = ShutdownAsync();
                _ = ObserveExitAsync(stop);
            }
        }

        private static async Task ObserveExitAsync(Task stop)
        {
            try { await stop; }
            catch (Exception exception) { GD.PushError(exception.ToString()); }
        }
    }
}
