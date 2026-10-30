using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Gameplay.Settings;

namespace AwithGameFrame.Samples.Shared
{
    public sealed class DemoAssetKeys
    {
        public DemoAssetKeys(string menu, string playing, string settings, string level, string tone)
        { Menu = menu; Playing = playing; Settings = settings; Level = level; Tone = tone; }
        public string Menu { get; }
        public string Playing { get; }
        public string Settings { get; }
        public string Level { get; }
        public string Tone { get; }
    }

    /// <summary>Native input normally; deterministic frames during the cross-engine acceptance scenario.</summary>
    public sealed class DemoInputBackend : IInputBackend
    {
        private readonly IInputBackend native;
        public DemoInputBackend(IInputBackend native) => this.native = native ?? throw new ArgumentNullException(nameof(native));
        public IReadOnlyDictionary<string, float> ScenarioFrame { get; set; }
        public bool IsFocused => ScenarioFrame != null || native.IsFocused;
        public float ReadControl(string control) => ScenarioFrame == null ? native.ReadControl(control) : ScenarioFrame.TryGetValue(control, out var value) ? value : 0;
    }

    /// <summary>The graphical sample composition. Engine types and native event wiring stay in each host.</summary>
    public sealed class DemoExperience : GameModule
    {
        private readonly GameContext context;
        private readonly SemaphoreSlim commands = new SemaphoreSlim(1, 1);
        private readonly NavigationStack navigation;
        private IDisposable actionSubscription, settingsSubscription;
        private ViewHandle settingsView;
        private SceneHandle level;
        private AudioHandle tone;
        private bool stopping, completingLevel;

        private DemoExperience(GameHost host, DemoApplication application, ViewService views, SceneService scenes,
            AudioService audio, InputService input, LocalizationService localization, UserSettingsService settings,
            DemoInputBackend inputBackend, DemoAssetKeys assets)
        {
            context = host.Context; Application = application; Views = views; Scenes = scenes; Audio = audio;
            Input = input; Localization = localization; Settings = settings; InputBackend = inputBackend; Assets = assets;
            navigation = new NavigationStack(context);
        }

        public override string Id => "demo.experience";
        public override IReadOnlyCollection<string> Dependencies => new[] { "demo.application", "presentation", "settings" };
        public DemoApplication Application { get; }
        public ViewService Views { get; }
        public SceneService Scenes { get; }
        public AudioService Audio { get; }
        public InputService Input { get; }
        public LocalizationService Localization { get; }
        public UserSettingsService Settings { get; }
        public DemoInputBackend InputBackend { get; }
        public DemoAssetKeys Assets { get; }
        public ObservableValue<string> Status { get; } = new ObservableValue<string>("");
        public ObservableValue<string> Error { get; } = new ObservableValue<string>("");
        public int Actions { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsSettingsOpen => settingsView != null && settingsView.IsOpen;

        public static DemoExperience Install(GameHost host, byte[] configuration, string directory,
            ResourceStore resources, IViewFactory viewFactory, ISceneBackend sceneBackend,
            IAudioBackend audioBackend, IInputBackend nativeInput, DemoAssetKeys assets)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            var application = new DemoApplication(Path.Combine(directory, "progress"), configuration);
            var views = new ViewService(host.Context, viewFactory, resources);
            var scenes = new SceneService(host.Context, sceneBackend, resources);
            var audio = new AudioService(resources, audioBackend);
            var input = new InputService(host.Events, new ViewInputRouter(views));
            var gameplay = input.CreateContext("demo");
            gameplay.Bind("interact", "keyboard/space"); gameplay.Bind("settings", "keyboard/escape");
            var inputBackend = new DemoInputBackend(nativeInput);
            var localization = new LocalizationService(host.Context);
            localization.ReplaceDatasets(CreateCatalogs());
            var settings = new UserSettingsService(host.Context,
                new VersionedUserSettingsStore(new VersionedSaveService<UserSettingsData>(Path.Combine(directory, "settings"), new JsonSerializer(), 1)),
                new UserSettingsData(), localization, audio, input);
            var experience = new DemoExperience(host, application, views, scenes, audio, input, localization, settings, inputBackend, assets);
            host.AddModule(new ResourceModule(resources));
            host.AddModule(new AudioModule(audio));
            host.AddModule(new InputModule(input, inputBackend));
            host.AddModule(new LocalizationModule(localization));
            host.AddModule(new UserSettingsModule(settings));
            host.AddModule(new SceneModule(scenes));
            // View callbacks and bound native controls finish before scene/audio teardown.
            host.AddModule(new PresentationModule(views, dependencies: new[] { "resources", "scenes", "audio" }));
            host.AddModule(application);
            host.AddModule(experience);
            return experience;
        }

        public override async Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            Application.Changed += Refresh;
            actionSubscription = context.Events.Subscribe<InputActionEvent>(message =>
            {
                if (message.ContextId != "demo" || message.Action != "interact" || message.Phase != InputActionPhase.Pressed || Application.Phase != "Playing") return;
                Actions++; Refresh();
            });
            settingsSubscription = context.Events.Subscribe<UserSettingsChanged>(_ => Refresh());
            await navigation.NavigateAsync(new ViewFlow(Views, new ViewRequest(Assets.Menu)), cancellationToken);
            Refresh();
        }

        public Task EnterLevelAsync() => ExecuteAsync(async token =>
        {
            if (completingLevel) await CompleteLevelCoreAsync(token);
            if (Application.Phase != "Menu") return;
            level = await Scenes.LoadAsync(Assets.Level, cancellationToken: token);
            await Application.EnterLevelAsync(cancellationToken: token);
            await navigation.NavigateAsync(new ViewFlow(Views, new ViewRequest(Assets.Playing)), token);
            tone = await Audio.PlayAsync(new AudioRequest(Assets.Tone, volume: 0.2f, loop: true), token);
        });

        public Task CompleteLevelAsync() => ExecuteAsync(async token =>
        {
            if ((Application.Phase != "Playing" && !completingLevel) || IsSettingsOpen) return;
            await CompleteLevelCoreAsync(token);
        });

        private async Task CompleteLevelCoreAsync(CancellationToken token)
        {
            completingLevel = true;
            if (Application.Phase == "Playing") await Application.CompleteLevelAsync(token);
            await navigation.BackAsync(token);
            var errors = new List<Exception>();
            if (level != null)
            {
                try { await level.UnloadAsync(); }
                catch (Exception error) { errors.Add(error); }
                finally { if (!level.IsLoaded) level = null; }
            }
            if (tone != null)
            {
                var endingTone = tone;
                tone = null; // Audio owns and completes cleanup even when StopAsync reports errors.
                try { await endingTone.StopAsync(); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Level completion cleanup reported errors. Retry completion before entering another level.", errors);
            completingLevel = false;
        }

        public Task OpenSettingsAsync() => ExecuteAsync(async token =>
        {
            settingsView = await Views.OpenAsync(new ViewRequest(Assets.Settings, 100, modal: true), token);
        });

        public Task CloseSettingsAsync() => ExecuteAsync(async token =>
        {
            await Settings.SaveAsync(token);
            if (settingsView != null) { await settingsView.CloseAsync(); settingsView = null; }
        });

        public bool HandleViewInput(string kind, ViewInput input)
        {
            if (!(input.Payload is InputActionEvent action)) return false;
            if (action.Action == "settings")
            {
                if (action.Phase == InputActionPhase.Pressed) Request(IsSettingsOpen ? CloseSettingsAsync : OpenSettingsAsync);
                return true;
            }
            if (kind == "menu" && action.Action == "interact")
            {
                if (action.Phase == InputActionPhase.Pressed) Request(EnterLevelAsync);
                return true;
            }
            return kind == "settings";
        }

        /// <summary>Native events and logical hooks queue commands so they cannot reenter input/view callbacks.</summary>
        public void Request(Func<Task> command) => _ = DispatchAsync(command);
        private async Task DispatchAsync(Func<Task> command)
        {
            try
            {
                Task operation = Task.CompletedTask;
                await context.Dispatcher.InvokeAsync(() => operation = command(), context.LifetimeToken);
                await operation;
            }
            catch (OperationCanceledException) when (context.LifetimeToken.IsCancellationRequested) { }
            catch (Exception error) { Error.Value = error.Message; }
        }

        public Task ToggleLanguageAsync() { Settings.SetLanguage(Settings.Snapshot.Language == "en" ? "zh-CN" : "en"); return Task.CompletedTask; }
        public Task ToggleMuteAsync() { Settings.SetMasterMuted(!Settings.Snapshot.MasterMuted); return Task.CompletedTask; }
        public Task CycleVolumeAsync() { Settings.SetMasterVolume(Settings.Snapshot.MasterVolume > 0.5f ? 0.35f : 1); return Task.CompletedTask; }
        public Task ToggleBindingAsync()
        {
            var current = Input.CaptureBindings();
            foreach (var binding in current)
                if (binding.ContextId == "demo" && binding.Action == "interact")
                    Settings.Rebind("demo", "interact", binding.Control == "keyboard/space" ? "keyboard/enter" : "keyboard/space");
            return Task.CompletedTask;
        }

        private async Task ExecuteAsync(Func<CancellationToken, Task> operation)
        {
            if (stopping) throw new ObjectDisposedException(nameof(DemoExperience));
            await commands.WaitAsync(context.LifetimeToken);
            IsBusy = true;
            try { context.LifetimeToken.ThrowIfCancellationRequested(); Error.Value = ""; await operation(context.LifetimeToken); }
            finally { IsBusy = false; try { Refresh(); } finally { commands.Release(); } }
        }

        private void Refresh()
        {
            var settings = Settings.Snapshot;
            var binding = settings.InputBindings.Find(item => item.ContextId == "demo" && item.Action == "interact");
            Status.Value = Localization.GetFormat("status", Application.Phase, Application.Coins, Application.CompletedLevels, Actions)
                + "\n" + Localization.GetFormat("settings.summary", settings.Language, settings.MasterVolume, settings.MasterMuted, binding?.Control);
        }

        public override async Task StopAsync(GameContext context, CancellationToken cancellationToken)
        {
            stopping = true;
            await commands.WaitAsync();
            try
            {
                Application.Changed -= Refresh;
                actionSubscription?.Dispose(); settingsSubscription?.Dispose();
                var errors = new List<Exception>();
                if (settingsView != null)
                {
                    try { await settingsView.CloseAsync(); } catch (Exception error) { errors.Add(error); }
                    settingsView = null;
                }
                try { await navigation.StopAsync(); } catch (Exception error) { errors.Add(error); }
                if (errors.Count != 0) throw new AggregateException("Demo view shutdown reported errors.", errors);
            }
            finally { commands.Release(); }
        }

        private static IEnumerable<LocalizationDataset> CreateCatalogs()
        {
            var keys = new[] { "menu", "playing", "settings", "enter", "complete", "open.settings", "language", "volume", "mute", "binding", "save.close", "hint", "status", "settings.summary" };
            var english = new[] { "Awith / Shared adventure", "Level", "Settings", "Enter level", "Complete and save", "Settings (Esc)", "Switch language", "Change volume", "Toggle mute", "Space / Enter", "Save and close", "Use Space to interact. Esc opens settings. Progress and settings return next session.", "{0} | Coins: {1} | Completed: {2} | Actions: {3}", "Language: {0} | Volume: {1:0.00} | Muted: {2} | Key: {3}" };
            var chinese = new[] { "Awith / 共享冒险", "关卡", "设置", "进入关卡", "完成并保存", "设置（Esc）", "切换语言", "调整音量", "切换静音", "空格 / 回车", "保存并关闭", "空格执行关卡动作，Esc 打开设置。进度与设置会在下次启动恢复。", "{0} | 金币：{1} | 完成：{2} | 动作：{3}", "语言：{0} | 音量：{1:0.00} | 静音：{2} | 按键：{3}" };
            foreach (var language in new[] { "en", "zh-CN" })
            {
                var dataset = new LocalizationDataset { LanguageCode = language };
                var values = language == "en" ? english : chinese;
                for (int i = 0; i < keys.Length; i++) dataset.Entries.Add(new LocalizationEntry { Key = keys[i], Value = values[i] });
                yield return dataset;
            }
        }
    }

    public static class DemoExperienceScenario
    {
        public static async Task RunAsync(DemoExperience experience, Action<string> report)
        {
            var frame = new Dictionary<string, float>();
            experience.InputBackend.ScenarioFrame = frame;
            try
            {
                await experience.EnterLevelAsync();
                Require(experience.Scenes.Loaded.Count == 1 && experience.Audio.ActiveCount == 1, "Native level and voice start");
                string control = null;
                foreach (var binding in experience.Input.CaptureBindings())
                    if (binding.ContextId == "demo" && binding.Action == "interact") control = binding.Control;
                frame[control] = 1;
                experience.Input.ProcessFrame(frame).ThrowIfFailed();
                int actions = experience.Actions;
                Require(actions > 0, "Gameplay receives native-host action");
                await experience.OpenSettingsAsync();
                experience.Input.ProcessFrame(frame).ThrowIfFailed();
                frame[control] = 0; experience.Input.ProcessFrame(frame).ThrowIfFailed();
                frame[control] = 1; experience.Input.ProcessFrame(frame).ThrowIfFailed();
                Require(experience.IsSettingsOpen && experience.Actions == actions, "Modal consumes held gameplay action");
                await experience.CloseSettingsAsync();
                experience.Input.ProcessFrame(frame).ThrowIfFailed();
                Require(experience.Actions == actions, "Closing modal does not reactivate held key");
                frame[control] = 0; experience.Input.ProcessFrame(frame).ThrowIfFailed();
                frame[control] = 1; experience.Input.ProcessFrame(frame).ThrowIfFailed();
                Require(experience.Actions == actions + 1, "Released and repressed gameplay key works");
                frame[control] = 0; experience.Input.ProcessFrame(frame).ThrowIfFailed();
                await experience.OpenSettingsAsync();
                await experience.ToggleLanguageAsync(); await experience.CycleVolumeAsync(); await experience.ToggleBindingAsync();
                await experience.CloseSettingsAsync();
                Require(!experience.Settings.IsDirty, "Settings persist language, volume and input mapping");
                await experience.CompleteLevelAsync();
                Require(experience.Application.Phase == "Menu" && experience.Scenes.Loaded.Count == 0 && experience.Audio.ActiveCount == 0, "Level completes, saves and releases native scene/audio");
                report?.Invoke("COMPOSED_APPLICATION_PASSED actions=" + experience.Actions + " language=" + experience.Settings.Snapshot.Language);
            }
            finally { experience.InputBackend.ScenarioFrame = null; }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Composed application: " + message); }
    }
}
