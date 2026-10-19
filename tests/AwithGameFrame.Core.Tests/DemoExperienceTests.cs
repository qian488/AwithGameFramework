using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Samples.Shared;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class DemoExperienceTests
    {
        private sealed class NativeFixture
        {
            public readonly GameHost Host = new GameHost();
            public readonly List<string> Cleanup = new List<string>();
            public readonly List<Voice> Voices = new List<Voice>();
            public DemoExperience Experience;
            public int Loaded, Released;
            public NativeFixture(PumpContext pump, string directory)
            {
                pump.Hosts.Add(Host);
                var resources = new ResourceStore(new Loader(this));
                Experience = DemoExperience.Install(Host,
                    File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Configuration/LevelConfig.json")),
                    directory, resources, new ViewFactory(this), new SceneBackend(this), new AudioBackend(this), new InputBackend(),
                    new DemoAssetKeys("menu", "playing", "settings", "level", "tone"));
            }
        }
        private sealed class Loader : IResourceLoader
        {
            private readonly NativeFixture fixture;
            public Loader(NativeFixture fixture) => this.fixture = fixture;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token)
            {
                fixture.Loaded++;
                return Task.FromResult(new LoadedResource(key, () => {
                    fixture.Released++; fixture.Cleanup.Add("asset:" + key); return Task.CompletedTask;
                }));
            }
        }
        private sealed class ViewFactory : IViewFactory
        {
            private readonly NativeFixture fixture;
            public ViewFactory(NativeFixture fixture) => this.fixture = fixture;
            public async Task<IView> CreateAsync(ViewRequest request, ResourceScope resources, CancellationToken token)
            { await resources.LoadAsync<string>("view/" + request.Key, token); return new View(fixture, request.Key); }
        }
        private sealed class View : IView
        {
            private readonly NativeFixture fixture;
            private readonly string kind;
            public View(NativeFixture fixture, string kind) { this.fixture = fixture; this.kind = kind; }
            public Task ShowAsync(CancellationToken token) => Task.CompletedTask;
            public Task HideAsync(CancellationToken token) => Task.CompletedTask;
            public void ApplyState(ViewState state) { }
            public bool HandleInput(ViewInput input) => fixture.Experience.HandleViewInput(kind, input);
            public ValueTask DisposeAsync() { fixture.Cleanup.Add("view:" + kind); return default; }
        }
        private sealed class SceneBackend : ISceneBackend
        {
            private readonly NativeFixture fixture;
            public SceneBackend(NativeFixture fixture) => this.fixture = fixture;
            public async Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken token)
            { await resources.LoadAsync<string>("scene/" + key, token); return new Scene(fixture, key); }
        }
        private sealed class Scene : ISceneInstance
        {
            private readonly NativeFixture fixture;
            private readonly string key;
            public Scene(NativeFixture fixture, string key) { this.fixture = fixture; this.key = key; }
            public Task ActivateAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() { fixture.Cleanup.Add("scene:" + key); return default; }
        }
        private sealed class AudioBackend : IAudioBackend
        {
            private readonly NativeFixture fixture;
            public AudioBackend(NativeFixture fixture) => this.fixture = fixture;
            public async Task<IAudioVoice> CreateAsync(string key, ResourceScope resources, bool loop, CancellationToken token)
            {
                await resources.LoadAsync<string>("audio/" + key, token);
                var voice = new Voice(fixture, key); fixture.Voices.Add(voice); return voice;
            }
        }
        private sealed class Voice : IAudioVoice
        {
            private readonly NativeFixture fixture;
            private readonly string key;
            public Voice(NativeFixture fixture, string key) { this.fixture = fixture; this.key = key; }
            public float Volume;
            public bool IsPlaying { get; private set; }
            public void SetVolume(float volume) { Volume = volume; }
            public void Play() { IsPlaying = true; }
            public void Pause() { }
            public void Resume() { }
            public void Stop() { IsPlaying = false; }
            public void Dispose() { fixture.Cleanup.Add("voice:" + key); }
        }
        private sealed class InputBackend : IInputBackend
        {
            public bool IsFocused => true;
            public float ReadControl(string control) => 0;
        }

        [Test]
        public void CompleteCompositionScenarioRestoresProgressAndSettingsInNewHost() => Run(async pump =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "awith-experience-" + Guid.NewGuid().ToString("N"));
            var first = new NativeFixture(pump, directory);
            try
            {
                await first.Host.StartAsync();
                string report = null;
                await DemoExperienceScenario.RunAsync(first.Experience, text => report = text);
                Assert.That(report, Does.Contain("COMPOSED_APPLICATION_PASSED"));
                Assert.That(first.Experience.Application.Coins, Is.EqualTo(10));
                Assert.That(first.Experience.Application.CompletedLevels, Is.EqualTo(1));
                Assert.That(first.Experience.Actions, Is.EqualTo(2));
                Assert.That(first.Experience.Settings.Snapshot.Language, Is.EqualTo("zh-CN"));
                Assert.That(first.Experience.Settings.IsDirty, Is.False);
                await first.Host.StopAsync();
                Assert.That(first.Released, Is.EqualTo(first.Loaded)); Assert.That(first.Host.Events.SubscriptionCount, Is.Zero);

                var second = new NativeFixture(pump, directory);
                try
                {
                    await second.Host.StartAsync();
                    Assert.That(second.Experience.Application.Coins, Is.EqualTo(10));
                    Assert.That(second.Experience.Application.CompletedLevels, Is.EqualTo(1));
                    Assert.That(second.Experience.Application.Phase, Is.EqualTo("Menu"));
                    Assert.That(second.Experience.Settings.Snapshot.Language, Is.EqualTo("zh-CN"));
                    Assert.That(second.Experience.Localization.CurrentLanguage, Is.EqualTo("zh-CN"));
                    Assert.That(second.Experience.Settings.Snapshot.MasterVolume, Is.EqualTo(.35f));
                    Assert.That(second.Experience.Input.CaptureBindings()[0].Control, Is.EqualTo("keyboard/enter"));
                    await second.Experience.EnterLevelAsync();
                    Assert.That(second.Voices[0].Volume, Is.EqualTo(.07f).Within(.0001f));
                }
                finally { await second.Host.StopAsync(); }
                Assert.That(second.Released, Is.EqualTo(second.Loaded));
            }
            finally { await first.Host.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });

        [Test]
        public void ShutdownWithOpenModalReleasesViewsBeforeSceneAudioAndTheirResources() => Run(async pump =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "awith-experience-stop-" + Guid.NewGuid().ToString("N"));
            var fixture = new NativeFixture(pump, directory);
            try
            {
                await fixture.Host.StartAsync(); await fixture.Experience.EnterLevelAsync(); await fixture.Experience.OpenSettingsAsync();
                fixture.Experience.Settings.SetLanguage("zh-CN"); fixture.Experience.Settings.SetMasterVolume(.6f);
                fixture.Cleanup.Clear();
                await fixture.Host.StopAsync();
                int playing = fixture.Cleanup.IndexOf("view:playing"), settings = fixture.Cleanup.IndexOf("view:settings");
                int scene = fixture.Cleanup.IndexOf("scene:level"), voice = fixture.Cleanup.IndexOf("voice:tone");
                Assert.That(playing, Is.GreaterThanOrEqualTo(0)); Assert.That(settings, Is.GreaterThanOrEqualTo(0));
                Assert.That(scene, Is.GreaterThan(playing)); Assert.That(scene, Is.GreaterThan(settings));
                Assert.That(voice, Is.GreaterThan(playing)); Assert.That(voice, Is.GreaterThan(settings));
                Assert.That(fixture.Cleanup.IndexOf("asset:scene/level"), Is.GreaterThan(scene));
                Assert.That(fixture.Cleanup.IndexOf("asset:audio/tone"), Is.GreaterThan(voice));
                Assert.That(fixture.Loaded, Is.EqualTo(fixture.Released));
                Assert.That(fixture.Host.Events.SubscriptionCount, Is.Zero);
                var reopened = new NativeFixture(pump, directory);
                try
                {
                    await reopened.Host.StartAsync();
                    Assert.That(reopened.Experience.Settings.Snapshot.Language, Is.EqualTo("zh-CN"));
                    Assert.That(reopened.Experience.Settings.Snapshot.MasterVolume, Is.EqualTo(.6f));
                    Assert.That(reopened.Experience.IsSettingsOpen, Is.False);
                }
                finally { await reopened.Host.StopAsync(); }
            }
            finally { await fixture.Host.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });

        private static void Run(Func<PumpContext, Task> scenario)
        {
            var previous = SynchronizationContext.Current; var pump = new PumpContext(); SynchronizationContext.SetSynchronizationContext(pump);
            try
            {
                var task = scenario(pump); var timeout = Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Composition did not finish on its host thread.");
                    pump.Pump();
                }
                task.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        private sealed class PumpContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> queue = new ConcurrentQueue<(SendOrPostCallback, object)>();
            public readonly List<GameHost> Hosts = new List<GameHost>();
            public override void Post(SendOrPostCallback callback, object state) => queue.Enqueue((callback, state));
            public void Pump()
            {
                foreach (var host in Hosts)
                    if (host.State != GameHostState.Stopped && host.State != GameHostState.Faulted) host.Dispatcher.RunPending(64);
                if (queue.TryDequeue(out var work)) work.Callback(work.State); else Thread.Yield();
            }
        }
    }
}
