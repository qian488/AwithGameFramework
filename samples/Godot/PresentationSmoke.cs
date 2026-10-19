using System;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Godot;
using Godot;

/// <summary>Exercises actual PackedScenes, Control focus, and QueueFree ownership.</summary>
public static class PresentationSmoke
{
    public static async Task RunAsync(GameHost host, Node parent)
    {
        var owner = new Node { Name = "PresentationSmoke" };
        parent.AddChild(owner);
        var viewRoot = new Control { Name = "Views", Size = new Vector2(640, 360) };
        owner.AddChild(viewRoot);
        var sceneRoot = new Node { Name = "Scenes" };
        owner.AddChild(sceneRoot);
        var store = new ResourceStore(new GodotResourceLoader(), host.Dispatcher);
        var views = new ViewService(host.Context, new GodotViewFactory(viewRoot), store);
        var scenes = new SceneService(host.Context, new GodotSceneBackend(sceneRoot), store);
        try
        {
            var modal = await views.OpenAsync(new ViewRequest("res://Resources/SmokeModal.tscn", 10, modal: true));
            var background = await views.OpenAsync(new ViewRequest("res://Resources/SmokeView.tscn"));
            var lower = (Control)viewRoot.GetChild(0);
            var upper = (Control)viewRoot.GetChild(1);
            Require(lower.GetChild(0).Name == "SmokeView", "View layer order");
            Require(lower.ProcessMode == Node.ProcessModeEnum.Disabled && upper.HasNode("ModalShield"), "Native modal input barrier");
            Require(upper.IsAncestorOf(upper.GetViewport().GuiGetFocusOwner()), "Native modal focus");
            Require(views.Focused == modal && views.RouteInput(new ViewInput("unhandled")), "Logical modal barrier");
            var model = new ObservableValue<string>("initial");
            var nativeTarget = upper.GetNode<Button>("SmokeModal/FocusTarget");
            using (var bindings = new ViewBindingScope())
            {
                bindings.Bind(model, nameof(model.Value), () => model.Value, text => nativeTarget.Text = text);
                model.Value = "updated";
                Require(nativeTarget.Text == "updated", "Explicit model binding updates a native view");
            }
            model.Value = "detached";
            Require(nativeTarget.Text == "updated", "Binding disposal detaches the native setter");
            await modal.CloseAsync();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(upper) && viewRoot.GetChildCount() == 1, "Closed native view freed");
            Require(views.Focused == background && lower.ProcessMode == Node.ProcessModeEnum.Inherit, "Background input restored");
            Require(lower.IsAncestorOf(lower.GetViewport().GuiGetFocusOwner()), "Native background focus restored");
            await background.CloseAsync();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(lower) && viewRoot.GetChildCount() == 0, "All native views freed");
            bool scopeClosed = false;
            try { await background.Resources.LoadAsync<PackedScene>(background.Request.Key); }
            catch (ObjectDisposedException) { scopeClosed = true; }
            Require(scopeClosed, "Closed view releases its resource scope");

            var a = await scenes.LoadAsync("res://Resources/SmokeSceneA.tscn");
            var nativeA = sceneRoot.GetChild(0);
            var b = await scenes.LoadAsync("res://Resources/SmokeSceneB.tscn", SceneLoadMode.Additive);
            var nativeB = sceneRoot.GetChild(1);
            Require(sceneRoot.GetChildCount() == 2 && scenes.Active == b, "Native additive scenes coexist");
            Require(await scenes.LoadAsync(a.Key) == a, "Single reuses an owned native scene");
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(nativeB) && GodotObject.IsInstanceValid(nativeA) && !b.IsLoaded, "Single frees other owned native scenes");
            await a.UnloadAsync();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(nativeA) && sceneRoot.GetChildCount() == 0, "Native scene explicitly freed");
            Require(GodotObject.IsInstanceValid(parent) && parent.IsInsideTree() && host.State == GameHostState.Running, "Persistent host survives scene replacement");
            GD.Print("GODOT_PRESENTATION_SCENES_PASSED");
        }
        finally
        {
            try { await views.StopAsync(); }
            finally
            {
                try { await scenes.StopAsync(); }
                finally { await store.DisposeAsync(); owner.QueueFree(); }
            }
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Presentation smoke: " + message); }
}
