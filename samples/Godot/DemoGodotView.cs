using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Godot;
using AwithGameFrame.Samples.Shared;
using Godot;

/// <summary>Sample native layout. Its state, commands and localization are shared with Unity.</summary>
public partial class DemoGodotView : GodotView
{
    private DemoExperience experience;
    private string kind;
    private ViewBindingScope bindings;
    private readonly List<IDisposable> textBindings = new List<IDisposable>();
    private VBoxContainer panel;
    public void Configure(DemoExperience experience, string kind) { this.experience = experience; this.kind = kind; }
    public override Task ShowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (experience == null) throw new InvalidOperationException("Demo native view requires its shared application.");
        var background = new PanelContainer { Position = new Vector2(36, 36), CustomMinimumSize = new Vector2(740, kind == "settings" ? 540 : 380) };
        AddChild(background);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 20);
        background.AddChild(margin);
        panel = new VBoxContainer(); panel.AddThemeConstantOverride("separation", 10); margin.AddChild(panel);
        Localize(kind, AddLabel());
        var status = AddLabel(); var error = AddLabel();
        error.Modulate = new Color(1, 0.55f, 0.45f);
        bindings = new ViewBindingScope();
        bindings.Bind(experience.Status, nameof(experience.Status.Value), () => experience.Status.Value, value => status.Text = value);
        bindings.Bind(experience.Error, nameof(experience.Error.Value), () => experience.Error.Value, value => error.Text = value);
        if (kind == "menu") AddButton("enter", experience.EnterLevelAsync);
        if (kind == "playing") AddButton("complete", experience.CompleteLevelAsync);
        if (kind == "settings")
        {
            AddButton("language", experience.ToggleLanguageAsync); AddButton("volume", experience.CycleVolumeAsync);
            AddButton("mute", experience.ToggleMuteAsync); AddButton("binding", experience.ToggleBindingAsync);
            AddButton("save.close", experience.CloseSettingsAsync);
        }
        else AddButton("open.settings", experience.OpenSettingsAsync);
        Localize("hint", AddLabel());
        return Task.CompletedTask;
    }
    private Label AddLabel()
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(690, 28) };
        panel.AddChild(label); return label;
    }
    private void Localize(string key, Label label) => textBindings.Add(experience.Localization.BindText(key, value => label.Text = value));
    private void AddButton(string key, Func<Task> action)
    {
        // Keyboard control belongs to InputService; mouse clicks queue the same shared command.
        var button = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) };
        panel.AddChild(button);
        textBindings.Add(experience.Localization.BindText(key, value => button.Text = value));
        button.Pressed += () => experience.Request(action);
    }
    public override bool HandleInput(ViewInput input) => experience.HandleViewInput(kind, input);
    public override Task HideAsync(CancellationToken cancellationToken) { ReleaseBindings(); return Task.CompletedTask; }
    private void ReleaseBindings()
    {
        bindings?.Dispose(); bindings = null;
        foreach (var binding in textBindings) binding.Dispose(); textBindings.Clear();
    }
    public override void _ExitTree() => ReleaseBindings();
}
