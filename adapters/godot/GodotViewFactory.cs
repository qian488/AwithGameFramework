using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using Godot;

namespace AwithGameFrame.Godot
{
    public sealed class GodotViewFactory : IViewFactory
    {
        private readonly Control parent;
        private readonly Action<ViewRequest, Control> initialize;
        private readonly int thread = System.Environment.CurrentManagedThreadId;
        public GodotViewFactory(Control parent) : this(parent, null) { }
        public GodotViewFactory(Control parent, Action<ViewRequest, Control> initialize)
        { this.parent = parent ?? throw new ArgumentNullException(nameof(parent)); this.initialize = initialize; }
        public async Task<IView> CreateAsync(ViewRequest request, ResourceScope resources, CancellationToken token)
        {
            Verify();
            var asset = await resources.LoadAsync<PackedScene>(request.Key, token);
            Verify(); token.ThrowIfCancellationRequested();
            Control root = null;
            try
            {
                if (!GodotObject.IsInstanceValid(parent)) throw new ObjectDisposedException("Godot view parent");
                root = new Control { Name = "View", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
                parent.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                if (request.Modal)
                {
                    var shield = new ColorRect { Name = "ModalShield", Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Stop };
                    root.AddChild(shield); shield.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                }
                var instance = asset.Value.Instantiate();
                if (!(instance is Control content)) { instance.Free(); throw new InvalidOperationException("A Godot UI PackedScene must have a Control root."); }
                root.AddChild(content);
                initialize?.Invoke(request, content);
                return new View(root, content as GodotView, thread);
            }
            catch { if (GodotObject.IsInstanceValid(root)) root.Free(); throw; }
        }
        private void Verify() { if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Godot views require the creating thread."); }

        private sealed class View : IView
        {
            private readonly Control root;
            private readonly GodotView hooks;
            private readonly int thread;
            private Task disposing;
            public View(Control root, GodotView hooks, int thread) { this.root = root; this.hooks = hooks; this.thread = thread; }
            private void Verify() { if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Godot view lifecycle requires the creating thread."); }
            public async Task ShowAsync(CancellationToken token)
            { Verify(); token.ThrowIfCancellationRequested(); root.Show(); if (hooks != null) await hooks.ShowAsync(token); }
            public async Task HideAsync(CancellationToken token)
            {
                Verify();
                try { if (GodotObject.IsInstanceValid(hooks)) await hooks.HideAsync(token); }
                finally { Verify(); if (GodotObject.IsInstanceValid(root)) root.Hide(); }
            }
            public void ApplyState(ViewState state)
            {
                Verify(); if (!GodotObject.IsInstanceValid(root)) return;
                root.GetParent().MoveChild(root, state.Order);
                root.ProcessMode = state.IsInputEnabled ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
                var focused = root.GetViewport().GuiGetFocusOwner();
                bool ownsFocus = focused != null && (focused == root || root.IsAncestorOf(focused));
                if (!state.IsFocused && ownsFocus) focused.ReleaseFocus();
                else if (state.IsFocused && !ownsFocus) FindFocusable(root)?.GrabFocus();
                if (GodotObject.IsInstanceValid(hooks)) hooks.OnPresentationChanged(state);
            }
            private static Control FindFocusable(Node node)
            {
                if (node is Control control && control.IsVisibleInTree() && control.FocusMode != Control.FocusModeEnum.None) return control;
                foreach (var child in node.GetChildren()) { var found = FindFocusable(child); if (found != null) return found; }
                return null;
            }
            public bool HandleInput(ViewInput input) { Verify(); return GodotObject.IsInstanceValid(hooks) && hooks.HandleInput(input); }
            public ValueTask DisposeAsync()
            {
                Verify();
                if (disposing == null) disposing = DisposeCoreAsync();
                return new ValueTask(disposing);
            }
            private async Task DisposeCoreAsync()
            {
                if (!GodotObject.IsInstanceValid(root)) return;
                root.Hide(); root.ProcessMode = Node.ProcessModeEnum.Disabled;
                if (!root.IsInsideTree()) { root.Free(); return; }
                var exited = root.ToSignal(root, Node.SignalName.TreeExited);
                root.QueueFree();
                await exited;
            }
        }
    }
}
