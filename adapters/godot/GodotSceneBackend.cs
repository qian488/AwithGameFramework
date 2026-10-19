using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>Owns PackedScene instances below a persistent parent; never replaces the host's root scene.</summary>
    public sealed class GodotSceneBackend : ISceneBackend
    {
        private readonly Node parent;
        private readonly int thread = System.Environment.CurrentManagedThreadId;
        public GodotSceneBackend(Node parent) => this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
        public async Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken token)
        {
            Verify();
            var asset = await resources.LoadAsync<PackedScene>(key, token);
            Verify(); token.ThrowIfCancellationRequested();
            Node instance = null;
            try
            {
                if (!GodotObject.IsInstanceValid(parent)) throw new ObjectDisposedException("Godot scene parent");
                instance = asset.Value.Instantiate();
                instance.ProcessMode = Node.ProcessModeEnum.Disabled;
                SetVisible(instance, false);
                parent.AddChild(instance);
                return new Instance(instance, thread);
            }
            catch { if (GodotObject.IsInstanceValid(instance)) instance.Free(); throw; }
        }
        private void Verify() { if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Godot scenes require the creating thread."); }
        private static void SetVisible(Node node, bool visible)
        {
            if (node is CanvasItem canvas) canvas.Visible = visible;
            else if (node is Node3D spatial) spatial.Visible = visible;
        }

        private sealed class Instance : ISceneInstance
        {
            private readonly Node root;
            private readonly int thread;
            private Task disposing;
            public Instance(Node root, int thread) { this.root = root; this.thread = thread; }
            private void Verify() { if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Godot scene lifecycle requires the creating thread."); }
            public Task ActivateAsync(CancellationToken token)
            {
                Verify(); token.ThrowIfCancellationRequested();
                if (!GodotObject.IsInstanceValid(root)) throw new ObjectDisposedException("Godot scene");
                root.ProcessMode = Node.ProcessModeEnum.Inherit; SetVisible(root, true);
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                Verify();
                if (disposing == null || disposing.IsFaulted || disposing.IsCanceled) disposing = DisposeCoreAsync();
                return new ValueTask(disposing);
            }
            private async Task DisposeCoreAsync()
            {
                if (!GodotObject.IsInstanceValid(root)) return;
                root.ProcessMode = Node.ProcessModeEnum.Disabled; SetVisible(root, false);
                if (!root.IsInsideTree()) { root.Free(); return; }
                var exited = root.ToSignal(root, Node.SignalName.TreeExited);
                root.QueueFree();
                await exited;
            }
        }
    }
}
