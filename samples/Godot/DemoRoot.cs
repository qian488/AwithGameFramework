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
    private DemoApplication application;
    private Label status;
    private Label error;
    private Button play;
    private Button complete;
    private bool busy;

    protected override void ConfigureHost(GameHost host)
    {
        host.AddModule(new ResourceModule(new ResourceStore(new GodotResourceLoader(), host.Dispatcher)));
        application = new DemoApplication(ProjectSettings.GlobalizePath("user://Saves"), Godot.FileAccess.GetFileAsBytes("res://Configuration/LevelConfig.json"));
        host.AddModule(application);
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
                await DemoScenario.RunAsync(directory, Godot.FileAccess.GetFileAsBytes("res://Configuration/LevelConfig.json"), message => GD.Print(message));
                await ShutdownAsync();
                GD.Print("GODOT_HOST_PASSED");
                GetTree().Quit();
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            return;
        }

        var panel = new VBoxContainer { Position = new Vector2(48, 48), Size = new Vector2(704, 350) };
        AddChild(panel);
        panel.AddChild(new Label { Text = "AwithGameFrame / Godot" });
        status = new Label();
        panel.AddChild(status);
        play = new Button { Text = "Enter level" };
        complete = new Button { Text = "Complete level and save" };
        var quit = new Button { Text = "Save is persistent. Exit" };
        panel.AddChild(play);
        panel.AddChild(complete);
        panel.AddChild(quit);
        error = new Label();
        panel.AddChild(error);
        play.Pressed += () => RunCommand(() => application.EnterLevelAsync());
        complete.Pressed += () => RunCommand(() => application.CompleteLevelAsync());
        quit.Pressed += async () => { await ShutdownAsync(); GetTree().Quit(); };
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (status == null) return;
        status.Text = "Phase: " + application.Phase + "\nCoins: " + application.Coins + " / Completed: " + application.CompletedLevels + "\nFrames: " + application.Frames;
        play.Disabled = busy || application.Phase != "Menu";
        complete.Disabled = busy || application.Phase != "Playing";
    }

    private async void RunCommand(Func<Task> command)
    {
        if (busy) return;
        busy = true;
        try { error.Text = ""; await command(); }
        catch (Exception exception) { error.Text = exception.Message; GD.PushError(exception.ToString()); }
        finally { busy = false; }
    }
}
