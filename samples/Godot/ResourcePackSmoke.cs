using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Godot;
using Godot;

public static class ResourcePackSmoke
{
    public static async Task RunAsync(GameHost host, Node parent)
    {
        var arguments = OS.GetCmdlineUserArgs();
        var index = Array.IndexOf(arguments, "--content-catalog");
        if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Content verification requires --content-catalog <pack-catalog.json>.");
        var catalogPath = Path.GetFullPath(arguments[index + 1]);
        var catalog = GodotPackCatalog.FromJson(File.ReadAllText(catalogPath));
        var loader = new GodotPackResourceLoader(catalog, Path.Combine(Path.GetDirectoryName(catalogPath), catalog.PackKey));
        var owner = new Node { Name = "PackSmoke" };
        parent.AddChild(owner);
        var viewRoot = new Control(); owner.AddChild(viewRoot);
        var sceneRoot = new Node(); owner.AddChild(sceneRoot);
        var resources = new ResourceStore(loader, host.Dispatcher);
        var views = new ViewService(host.Context, new GodotViewFactory(viewRoot), resources);
        var scenes = new SceneService(host.Context, new GodotSceneBackend(sceneRoot), resources);
        try
        {
            await using (var scope = new ResourceScope(resources))
            {
                var texture = await scope.LoadAsync<Texture2D>("content/texture");
                if (texture.Value.GetWidth() != 16) throw new InvalidOperationException("Imported PCK texture has unexpected dimensions.");
                var scene = await scenes.LoadAsync("content/scene");
                var sprite = sceneRoot.GetChild(0).GetNode<Sprite2D>("Tile");
                if (sprite.Texture == null || sprite.Texture.GetWidth() != 16) throw new InvalidOperationException("PCK scene dependency did not resolve.");
                if (!(sprite.Material is CanvasItemMaterial) || !sprite.Material.ResourcePath.StartsWith(catalog.ResourceRoot, StringComparison.Ordinal))
                    throw new InvalidOperationException("PCK material dependency escaped its namespace.");
                if (sceneRoot.GetChild(0).GetMeta("original_uid").AsString() != "uid://c4ncpr3q5avhk" ||
                    sceneRoot.GetChild(0).GetMeta("declaration_text").AsString() != "Literal declaration:\n[ext_resource uid=\"uid://c4ncpr3q5avhk\"]")
                    throw new InvalidOperationException("PCK build changed a user string containing UID text.");
                var view = await views.OpenAsync(new ViewRequest("content/view"));
                if (viewRoot.GetChildCount() != 1) throw new InvalidOperationException("PCK view was not instantiated.");
                await view.CloseAsync();
                await scene.UnloadAsync();
            }
            if (viewRoot.GetChildCount() != 0 || sceneRoot.GetChildCount() != 0) throw new InvalidOperationException("PCK native instances were retained.");
            // Same immutable pack can serve another store; the native mount remains process-owned.
            await using (var second = new ResourceStore(new GodotPackResourceLoader(catalog, Path.Combine(Path.GetDirectoryName(catalogPath), catalog.PackKey)), host.Dispatcher))
            await using (var scope = new ResourceScope(second))
            {
                var texture = await scope.LoadAsync<Texture2D>("content/texture");
                if (texture.Value.GetWidth() != 16) throw new InvalidOperationException("Mounted pack could not be reused.");
            }
            GD.Print("GODOT_RESOURCE_PACK_PASSED");
        }
        finally
        {
            try { await views.StopAsync(); }
            finally
            {
                try { await scenes.StopAsync(); }
                finally { await resources.DisposeAsync(); owner.Free(); }
            }
        }
    }
}
