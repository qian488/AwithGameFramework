using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Godot;
using AwithGameFrame.Samples.Shared;
using Godot;

public partial class DemoRoot : GameHostNode
{
    private DemoExperience experience;
    private string smokeDirectory;

    protected override void ConfigureHost(GameHost host)
    {
        // Unknown native memory sizes remain uncached; a product can supply its own conservative estimator.
        var resources = new ResourceStore(new GodotThreadedResourceLoader(), host.Dispatcher);
        var viewRoot = new Control { Name = "DemoViews" }; AddChild(viewRoot); viewRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var sceneRoot = new Node { Name = "DemoScenes" }; AddChild(sceneRoot);
        var audioRoot = new Node { Name = "DemoAudio" }; AddChild(audioRoot);
        bool smoke = OS.GetCmdlineUserArgs().Contains("--framework-smoke");
        if (smoke) smokeDirectory = ProjectSettings.GlobalizePath("user://ComposedValidation/" + Guid.NewGuid().ToString("N"));
        var keys = new DemoAssetKeys("res://Resources/DemoMenu.tscn", "res://Resources/DemoPlaying.tscn", "res://Resources/DemoSettings.tscn", "res://Resources/DemoLevel.tscn", "res://Resources/DemoTone.wav");
        var factory = new GodotViewFactory(viewRoot, (request, content) =>
        {
            if (!(content is DemoGodotView hooks)) throw new InvalidOperationException("Complete sample scene requires DemoGodotView.");
            hooks.Configure(experience, request.Key == keys.Menu ? "menu" : request.Key == keys.Settings ? "settings" : "playing");
        });
        experience = DemoExperience.Install(host, Godot.FileAccess.GetFileAsBytes("res://Configuration/LevelConfig.json"),
            smokeDirectory ?? ProjectSettings.GlobalizePath("user://SharedAdventure"), resources, factory, new GodotSceneBackend(sceneRoot),
            new GodotAudioBackend(audioRoot), new GodotInputBackend(GetWindow()), keys);
        if (smoke) experience.InputBackend.ScenarioFrame = new System.Collections.Generic.Dictionary<string, float>();
    }

    protected override async Task OnStartedAsync()
    {
        await using (var scope = new ResourceScope(Host.Services.Get<ResourceStore>()))
        {
            var first = await scope.LoadAsync<Resource>("res://Resources/DemoResource.tres");
            var second = await scope.LoadAsync<Resource>("res://Resources/DemoResource.tres");
            if (!ReferenceEquals(first.Value, second.Value)) throw new InvalidOperationException("Resource leases did not share a load.");
        }
        if (OS.GetCmdlineUserArgs().Contains("--framework-smoke"))
        {
            var directory = ProjectSettings.GlobalizePath("user://Validation/" + Guid.NewGuid().ToString("N"));
            try
            {
                var previousEngineScale = Engine.TimeScale;
                try
                {
                    Engine.TimeScale = 0;
                    var before = Host.Clock.UnscaledTime;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (Host.Clock.UnscaledTime <= before)
                        throw new InvalidOperationException("Godot engine time scale must not freeze the host's unscaled clock.");
                    GD.Print("GODOT_UNSCALED_CLOCK_PASSED");
                }
                finally { Engine.TimeScale = previousEngineScale; }
                await PresentationSmoke.RunAsync(Host, this);
                await InputAudioSmoke.RunAsync(Host, this);
                await ResourcePackSmoke.RunAsync(Host, this);
                await DemoExperienceScenario.RunAsync(experience, message => GD.Print(message));
                await DemoScenario.RunAsync(directory, Godot.FileAccess.GetFileAsBytes("res://Configuration/LevelConfig.json"), message => GD.Print(message));
                await ShutdownAsync();
                // Runs last: its final case emits the real root-window close signal for two hosts.
                await GameHostLifecycleSmoke.RunAsync(this);
                GD.Print("GODOT_HOST_PASSED");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                if (smokeDirectory != null && Directory.Exists(smokeDirectory)) Directory.Delete(smokeDirectory, true);
            }
            return;
        }

    }
}
