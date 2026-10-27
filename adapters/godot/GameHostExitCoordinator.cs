using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>Owns one SceneTree's close policy while any host still needs asynchronous cleanup.</summary>
    internal partial class GameHostExitCoordinator : Node
    {
        internal sealed class Registration
        {
            private readonly GameHostExitCoordinator owner;
            internal readonly GameHost Host;
            internal readonly Func<Task> Stop;
            internal bool Detached;
            private bool released;

            internal Registration(GameHostExitCoordinator owner, GameHost host, Func<Task> stop)
            { this.owner = owner; Host = host; Stop = stop; }

            internal void Detach() { Detached = true; }
            internal void Release()
            {
                if (released) return;
                released = true;
                owner.registrations.Remove(this);
                owner.RetireIfUnused();
            }
        }

        private static readonly Dictionary<ulong, GameHostExitCoordinator> coordinators = new Dictionary<ulong, GameHostExitCoordinator>();
        private readonly HashSet<Registration> registrations = new HashSet<Registration>();
        private SceneTree tree;
        private ulong treeId;
        private bool previousAutoAcceptQuit;
        private bool retired, quitRequested;
        private int exitCode;
        private Task quitTask;
        private ulong lastProcessMicroseconds;

        internal static Registration Register(SceneTree tree, GameHost host, Func<Task> stop)
        {
            var owner = GetOrCreate(tree);
            if (owner.quitRequested) throw new InvalidOperationException("Cannot start another host while its SceneTree is closing.");
            var registration = new Registration(owner, host, stop);
            owner.registrations.Add(registration);
            return registration;
        }

        internal static Task RequestQuitAsync(SceneTree tree, int code)
        {
            if (tree == null || !GodotObject.IsInstanceValid(tree)) return Task.CompletedTask;
            return GetOrCreate(tree).RequestQuitAsync(code);
        }

        private static GameHostExitCoordinator GetOrCreate(SceneTree tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            ulong id = tree.GetInstanceId();
            if (coordinators.TryGetValue(id, out var existing)) return existing;
            var owner = new GameHostExitCoordinator
            {
                Name = "AwithHostExitCoordinator",
                ProcessMode = ProcessModeEnum.Always,
                tree = tree,
                treeId = id,
                previousAutoAcceptQuit = tree.AutoAcceptQuit,
                lastProcessMicroseconds = global::Godot.Time.GetTicksUsec()
            };
            coordinators.Add(id, owner);
            tree.AutoAcceptQuit = false;
            tree.Root.CloseRequested += owner.OnCloseRequested;
            // A child can register from _Ready while the root is still adding its scene children.
            tree.Root.CallDeferred(Node.MethodName.AddChild, owner);
            return owner;
        }

        private async void OnCloseRequested()
        {
            try { await RequestQuitAsync(0); }
            catch (Exception exception) { GD.PushError(exception.ToString()); }
        }

        private Task RequestQuitAsync(int code)
        {
            if (code != 0) exitCode = code;
            if (quitTask != null) return quitTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Host cancellation/stop callbacks can synchronously emit another CloseRequested signal.
            quitTask = completion.Task;
            quitRequested = true;
            _ = StopAllAndQuitAsync(completion);
            return quitTask;
        }

        private async Task StopAllAndQuitAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                // Initiate every stop before waiting, including hosts whose startup is still pending.
                await Task.WhenAll(registrations.ToArray().Select(StopOneAsync));
                RestorePolicy();
                QueueFree();
                if (GodotObject.IsInstanceValid(tree)) tree.Quit(exitCode);
                completion.TrySetResult(true);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        private async Task StopOneAsync(Registration registration)
        {
            try { await registration.Stop(); }
            catch (Exception exception)
            {
                exitCode = 1;
                GD.PushError(exception.ToString());
            }
        }

        public override void _Process(double delta)
        {
            var now = global::Godot.Time.GetTicksUsec();
            var seconds = (now - lastProcessMicroseconds) / 1_000_000.0;
            lastProcessMicroseconds = now;
            foreach (var registration in registrations.ToArray())
            {
                if (!registration.Detached || registration.Host.State == GameHostState.Stopped || registration.Host.State == GameHostState.Faulted) continue;
                try { registration.Host.Tick(seconds); }
                catch (Exception exception) { GD.PushError(exception.ToString()); }
            }
        }

        private void RetireIfUnused()
        {
            if (registrations.Count != 0 || quitRequested || retired) return;
            RestorePolicy();
            QueueFree();
        }

        private void RestorePolicy()
        {
            if (retired) return;
            retired = true;
            coordinators.Remove(treeId);
            if (!GodotObject.IsInstanceValid(tree)) return;
            tree.Root.CloseRequested -= OnCloseRequested;
            tree.AutoAcceptQuit = previousAutoAcceptQuit;
        }

        public override void _ExitTree()
        {
            RestorePolicy();
        }
    }
}
