using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Gameplay.Settings;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class UserSettingsTests
    {
        private sealed class MemoryStore : IUserSettingsStore
        {
            private readonly JsonSerializer json = new JsonSerializer();
            public UserSettingsData Data;
            public int Saves;
            public Exception SaveError;
            public Func<UserSettingsData, Task> Saving;
            public Func<Task<UserSettingsData>> Loading;
            public Task<UserSettingsData> LoadAsync(CancellationToken token) => Loading == null ? Task.FromResult(Clone(Data)) : Loading();
            public async Task SaveAsync(UserSettingsData settings, CancellationToken token)
            {
                Saves++;
                if (SaveError != null) throw SaveError;
                if (Saving != null) await Saving(Clone(settings));
                Data = Clone(settings);
            }
            private UserSettingsData Clone(UserSettingsData data) => data == null ? null :
                json.DeserializeAsync<UserSettingsData>(json.SerializeAsync(data).GetAwaiter().GetResult()).GetAwaiter().GetResult();
        }
        private sealed class Loader : IResourceLoader
        {
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token)
                => Task.FromResult(new LoadedResource(new object(), () => Task.CompletedTask));
        }
        private sealed class Voice : IAudioVoice
        {
            public float Volume; public bool IsPlaying { get; private set; }
            public void SetVolume(float volume) { Volume = volume; }
            public void Play() { IsPlaying = true; }
            public void Stop() { IsPlaying = false; }
            public void Pause() { }
            public void Resume() { }
            public void Dispose() { }
        }
        private sealed class AudioBackend : IAudioBackend
        {
            public Voice Last;
            public Task<IAudioVoice> CreateAsync(string key, ResourceScope scope, bool loop, CancellationToken token)
            { Last = new Voice(); return Task.FromResult<IAudioVoice>(Last); }
        }
        private sealed class InputBackend : IInputBackend
        {
            public bool IsFocused => true;
            public float ReadControl(string control) => 0;
        }
        private sealed class Rig : IDisposable
        {
            public readonly GameHost Host = new GameHost();
            public readonly ResourceStore Resources;
            public readonly AudioBackend Backend = new AudioBackend();
            public readonly AudioService Audio;
            public readonly InputService Input;
            public readonly InputContext Controls;
            public readonly LocalizationService Localization;
            public readonly UserSettingsService Settings;
            public Rig(IUserSettingsStore store, UserSettingsData defaults = null, bool initialize = true)
            {
                Resources = new ResourceStore(new Loader()); Audio = new AudioService(Resources, Backend);
                Input = new InputService(Host.Events); Controls = Input.CreateContext("game");
                Controls.Bind("jump", "key/space"); Controls.Bind("fire", "key/f");
                Localization = SettingsHostTest.Localization(Host);
                Settings = new UserSettingsService(Host.Context, store, defaults ?? new UserSettingsData(), Localization, Audio, Input);
                if (initialize) SettingsHostTest.Wait(Settings.InitializeAsync(), Host);
            }
            public void Modules()
            {
                // Intentionally register in reverse dependency order.
                Host.AddModule(new UserSettingsModule(Settings));
                Host.AddModule(new InputModule(Input, new InputBackend()));
                Host.AddModule(new AudioModule(Audio));
                Host.AddModule(new LocalizationModule(Localization));
                Host.AddModule(new ResourceModule(Resources));
            }
            public AudioHandle Play(string bus = "sfx")
            { var task = Audio.PlayAsync(new AudioRequest("clip", bus)); SettingsHostTest.Wait(task, Host); return task.Result; }
            public void Dispose()
            {
                SettingsHostTest.Wait(Settings.StopAsync(), Host);
                Input.Dispose(); Localization.Dispose(); SettingsHostTest.Wait(Audio.DisposeAsync().AsTask(), Host);
                SettingsHostTest.Wait(Resources.DisposeAsync().AsTask(), Host); SettingsHostTest.Wait(Host.StopAsync(), Host);
            }
        }

        [Test]
        public void DefaultsAndSavedChangesApplyAcrossLocalizationAudioInputAndNextHost()
        {
            var store = new MemoryStore();
            using (var rig = new Rig(store))
            {
                Assert.That(rig.Settings.IsDirty, Is.True);
                rig.Settings.SetLanguage("fr-CA"); rig.Settings.SetMasterVolume(.5f); rig.Settings.SetBusVolume("sfx", .4f);
                rig.Settings.Rebind("game", "jump", "key/j");
                var handle = rig.Play();
                Assert.That(rig.Backend.Last.Volume, Is.EqualTo(.2f).Within(.0001f));
                rig.Settings.SetBusMuted("sfx", true); Assert.That(rig.Backend.Last.Volume, Is.Zero);
                rig.Settings.SetBusMuted("sfx", false); rig.Settings.SetMasterMuted(true);
                Assert.That(rig.Backend.Last.Volume, Is.Zero);
                rig.Settings.SetMasterMuted(false);
                Assert.That(rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/j"] = 1 }).Events[0].Action, Is.EqualTo("jump"));
                Assert.That(rig.Localization.GetText("greeting"), Is.EqualTo("Bonjour"));
                SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host);
                Assert.That(rig.Settings.IsDirty, Is.False); SettingsHostTest.Wait(handle.StopAsync(), rig.Host);
            }
            using (var restored = new Rig(store))
            {
                Assert.That(restored.Settings.IsDirty, Is.False);
                Assert.That(restored.Localization.CurrentLanguage, Is.EqualTo("fr-CA"));
                Assert.That(restored.Input.CaptureBindings()[0].Control, Is.EqualTo("key/j"));
                restored.Play(); Assert.That(restored.Backend.Last.Volume, Is.EqualTo(.2f).Within(.0001f));
            }
            Assert.That(store.Saves, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotsDefaultsAndHostEventsDoNotShareMutableState()
        {
            var defaults = new UserSettingsData { MasterVolume = .8f };
            using var first = new Rig(new MemoryStore(), defaults);
            using var second = new Rig(new MemoryStore());
            defaults.MasterVolume = .1f;
            var snapshot = first.Settings.Snapshot; snapshot.BusVolumes["sfx"] = .1f; snapshot.InputBindings[0].Control = "external";
            int one = 0, two = 0;
            using var a = first.Host.Events.Subscribe<UserSettingsChanged>(message => { one++; message.Settings.BusVolumes.Clear(); });
            using var b = second.Host.Events.Subscribe<UserSettingsChanged>(_ => two++);
            first.Settings.SetMasterVolume(.5f);
            Assert.That(first.Settings.Snapshot.BusVolumes["sfx"], Is.EqualTo(1));
            Assert.That(first.Settings.Snapshot.InputBindings[0].Control, Is.EqualTo("key/space"));
            Assert.That(one, Is.EqualTo(1)); Assert.That(two, Is.Zero);
            first.Settings.ResetToDefaults(); Assert.That(first.Settings.Snapshot.MasterVolume, Is.EqualTo(.8f));
            Assert.That(second.Settings.Snapshot.MasterVolume, Is.EqualTo(1));
        }

        [Test]
        public void ValidationIsAtomicAndBatchRebindingSwapsControlsWithoutLosingDynamicActions()
        {
            using var rig = new Rig(new MemoryStore());
            long revision = rig.Settings.Revision;
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Settings.SetMasterVolume(float.NaN));
            Assert.Throws<ArgumentException>(() => rig.Settings.SetLanguage("de"));
            Assert.Throws<ArgumentException>(() => rig.Settings.SetBusVolume("unknown", .1f));
            Assert.Throws<ArgumentException>(() => rig.Settings.Rebind("game", "jump", "key/f"));
            Assert.That(rig.Settings.Revision, Is.EqualTo(revision));
            rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/space"] = 1 });
            rig.Settings.SetMasterVolume(.5f);
            Assert.That(rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/space"] = 1 }).Events[0].Phase, Is.EqualTo(InputActionPhase.Held));
            rig.Controls.Bind("dynamic", "key/d");
            var next = rig.Settings.Snapshot;
            next.InputBindings[0].Control = "key/f"; next.InputBindings[1].Control = "key/space";
            rig.Settings.Update(next);
            var bindings = rig.Input.CaptureBindings();
            Assert.That(bindings.Count, Is.EqualTo(3)); Assert.That(bindings[2].Control, Is.EqualTo("key/d"));
            Assert.That(rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/f"] = 1 }).Events[0].Action, Is.EqualTo("jump"));
            Assert.Throws<ArgumentException>(() => rig.Settings.Rebind("game", "jump", "key/d"));
        }

        [Test]
        public void InFlightAndQueuedSavesCannotAcknowledgeOrOverwriteNewerChanges()
        {
            var store = new MemoryStore(); var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = new List<float>();
            store.Saving = data => { lock (writes) writes.Add(data.MasterVolume); return writes.Count == 1 ? pending.Task : Task.CompletedTask; };
            using var rig = new Rig(store);
            rig.Settings.SetMasterVolume(.3f); var first = rig.Settings.SaveAsync();
            rig.Settings.SetMasterVolume(.7f); var second = rig.Settings.SaveAsync();
            Assert.That(store.Saves, Is.EqualTo(1));
            pending.SetResult(true); SettingsHostTest.Wait(first, rig.Host);
            SettingsHostTest.Wait(second, rig.Host);
            Assert.That(writes, Is.EqualTo(new[] { .3f, .7f }));
            Assert.That(store.Data.MasterVolume, Is.EqualTo(.7f)); Assert.That(rig.Settings.IsDirty, Is.False);
            var last = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Saving = _ => last.Task;
            rig.Settings.SetMasterVolume(.2f); var old = rig.Settings.SaveAsync(); rig.Settings.SetMasterVolume(.9f);
            last.SetResult(true); SettingsHostTest.Wait(old, rig.Host);
            Assert.That(rig.Settings.IsDirty, Is.True);
            store.Saving = null;
        }

        [Test]
        public void SaveAndShutdownFailuresKeepDirtyChangesAndAllowExplicitRetry()
        {
            var failure = new IOException("disk unavailable"); var store = new MemoryStore { SaveError = failure };
            using var rig = new Rig(store);
            Assert.Throws<IOException>(() => SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host));
            Assert.That(rig.Settings.IsDirty, Is.True); Assert.That(rig.Settings.LastSaveError, Is.SameAs(failure));
            Assert.Throws<IOException>(() => SettingsHostTest.Wait(rig.Settings.StopAsync(), rig.Host));
            Assert.Throws<ObjectDisposedException>(() => rig.Settings.SetLanguage("fr"));
            store.SaveError = null; SettingsHostTest.Wait(rig.Settings.StopAsync(), rig.Host);
            Assert.That(rig.Settings.IsDirty, Is.False); Assert.That(rig.Settings.LastSaveError, Is.Null);
        }

        [Test]
        public void StopJoinsEarlierWriteThenPersistsLatestRevision()
        {
            var store = new MemoryStore(); var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Saving = _ => store.Saves == 1 ? pending.Task : Task.CompletedTask;
            using var rig = new Rig(store);
            rig.Settings.SetMasterVolume(.2f); var first = rig.Settings.SaveAsync(); rig.Settings.SetMasterVolume(.8f);
            var stop = rig.Settings.StopAsync(); Assert.That(stop.IsCompleted, Is.False);
            Assert.Throws<ObjectDisposedException>(() => rig.Settings.SetMasterVolume(.1f));
            pending.SetResult(true); SettingsHostTest.Wait(first, rig.Host); SettingsHostTest.Wait(stop, rig.Host);
            Assert.That(store.Data.MasterVolume, Is.EqualTo(.8f)); Assert.That(store.Saves, Is.EqualTo(2));
        }

        [Test]
        public void NewlyIntroducedDefaultsMergeAndRemainDirtyUntilSaved()
        {
            var store = new MemoryStore { Data = new UserSettingsData { Language = "fr", MasterVolume = .4f } };
            using var rig = new Rig(store);
            Assert.That(rig.Settings.Snapshot.BusVolumes.Count, Is.EqualTo(2));
            Assert.That(rig.Settings.Snapshot.InputBindings.Count, Is.EqualTo(2)); Assert.That(rig.Settings.IsDirty, Is.True);
            SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host);
            Assert.That(store.Data.InputBindings.Count, Is.EqualTo(2)); Assert.That(rig.Settings.IsDirty, Is.False);
        }

        [Test]
        public void NormalizedRestoreRequiresSaveButEquivalentReorderedUpdateHasNoEffects()
        {
            var store = new MemoryStore();
            using (var seed = new Rig(store)) SettingsHostTest.Wait(seed.Settings.SaveAsync(), seed.Host);
            store.Data.Language = "FR-ca";
            using (var rig = new Rig(store))
            {
                Assert.That(rig.Settings.Snapshot.Language, Is.EqualTo("fr-CA"));
                Assert.That(store.Data.Language, Is.EqualTo("FR-ca"));
                Assert.That(rig.Settings.IsDirty, Is.True);
                long revision = rig.Settings.Revision;
                int changes = 0;
                using var subscription = rig.Host.Events.Subscribe<UserSettingsChanged>(_ => changes++);
                rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/space"] = 1 });
                var same = rig.Settings.Snapshot;
                same.InputBindings.Reverse();
                rig.Settings.Update(same);
                Assert.That(rig.Settings.Revision, Is.EqualTo(revision));
                Assert.That(changes, Is.Zero);
                Assert.That(rig.Input.ProcessFrame(new Dictionary<string, float> { ["key/space"] = 1 }).Events[0].Phase,
                    Is.EqualTo(InputActionPhase.Held));
                SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host);
                Assert.That(store.Data.Language, Is.EqualTo("fr-CA"));
            }
            using var restored = new Rig(store);
            Assert.That(restored.Settings.IsDirty, Is.False);
        }

        [Test]
        public void LoadValidatesAgainstRuntimeBindingsCapturedAfterStorageCompletes()
        {
            var pending = new TaskCompletionSource<UserSettingsData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new MemoryStore { Loading = () => pending.Task };
            using var rig = new Rig(store, initialize: false);
            var saved = rig.Settings.Snapshot;
            saved.InputBindings[0].Control = "key/d";
            var load = rig.Settings.InitializeAsync();
            rig.Controls.Bind("dynamic", "key/d");
            pending.SetResult(saved);

            Assert.Throws<ArgumentException>(() => SettingsHostTest.Wait(load, rig.Host));
            Assert.That(rig.Settings.IsInitialized, Is.False);
            Assert.That(rig.Settings.Revision, Is.Zero);
            Assert.That(rig.Input.CaptureBindings()[0].Control, Is.EqualTo("key/space"));
            Assert.That(rig.Input.CaptureBindings()[2].Control, Is.EqualTo("key/d"));
            Assert.That(store.Saves, Is.Zero);
        }

        [Test]
        public void ListenerFailureCommitsDesiredSettingsAndReapplyCanRecoverRuntime()
        {
            using var rig = new Rig(new MemoryStore());
            using var subscription = rig.Host.Events.Subscribe<LanguageChanged>(_ => throw new FormatException("view"));
            Assert.Throws<AggregateException>(() => rig.Settings.SetLanguage("fr"));
            Assert.That(rig.Settings.Snapshot.Language, Is.EqualTo("fr")); Assert.That(rig.Settings.IsDirty, Is.True);
            Assert.That(rig.Localization.CurrentLanguage, Is.EqualTo("fr"));
            subscription.Dispose(); rig.Settings.Reapply();
            Assert.That(rig.Localization.GetText("greeting"), Is.EqualTo("Bonjour"));
        }

        [Test]
        public void DisposedLocalizationRejectsChangesBeforeSettingsOrAudioCommit()
        {
            using var rig = new Rig(new MemoryStore());
            rig.Play();
            long revision = rig.Settings.Revision;
            int changes = 0;
            using var subscription = rig.Host.Events.Subscribe<UserSettingsChanged>(_ => changes++);
            rig.Localization.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rig.Settings.SetMasterVolume(.2f));
            Assert.Throws<ObjectDisposedException>(() => rig.Settings.SetLanguage("fr"));
            Assert.That(rig.Settings.Revision, Is.EqualTo(revision));
            Assert.That(rig.Settings.Snapshot.MasterVolume, Is.EqualTo(1));
            Assert.That(rig.Settings.Snapshot.Language, Is.EqualTo("en"));
            Assert.That(rig.Backend.Last.Volume, Is.EqualTo(1));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void ModulesRestoreBeforeDependentsAndFlushBeforeAudioInputLocalizationStop()
        {
            var store = new MemoryStore(); using var rig = new Rig(store, initialize: false);
            rig.Modules(); SettingsHostTest.Wait(rig.Host.StartAsync(), rig.Host);
            Assert.That(rig.Host.Services.Get<UserSettingsService>(), Is.SameAs(rig.Settings));
            rig.Settings.SetLanguage("fr"); rig.Settings.Rebind("game", "jump", "key/j");
            bool dependenciesAlive = false;
            store.Saving = _ => {
                dependenciesAlive = rig.Localization.GetText("greeting") == "Bonjour" && rig.Audio.HasBus("sfx") && rig.Input.CaptureBindings().Count == 2;
                return Task.CompletedTask;
            };
            SettingsHostTest.Wait(rig.Host.StopAsync(), rig.Host);
            Assert.That(dependenciesAlive, Is.True); Assert.That(store.Data.Language, Is.EqualTo("fr"));
            Assert.That(store.Data.InputBindings[0].Control, Is.EqualTo("key/j"));
        }

        [Test]
        public void CancelledLoadCannotApplyOrOverwriteLateResults()
        {
            var pending = new TaskCompletionSource<UserSettingsData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new MemoryStore { Loading = () => pending.Task };
            using var rig = new Rig(store, initialize: false); using var cancellation = new CancellationTokenSource();
            var load = rig.Settings.InitializeAsync(cancellation.Token); cancellation.Cancel();
            pending.SetResult(new UserSettingsData { Language = "fr" });
            Assert.Catch<OperationCanceledException>(() => SettingsHostTest.Wait(load, rig.Host));
            Assert.That(rig.Settings.IsInitialized, Is.False); Assert.That(rig.Localization.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(store.Saves, Is.Zero);
        }

        public sealed class SettingsV1 { public string Locale { get; set; } public float Volume { get; set; } }

        [Test]
        public void VersionedSettingsMigrateOldDtoAndRecoverChecksumProtectedBackup()
        {
            string directory = Path.Combine(Path.GetTempPath(), "awith-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var json = new JsonSerializer(); var old = new VersionedSaveService<SettingsV1>(directory, json, 1);
                old.SaveAsync(0, new SettingsV1 { Locale = "fr", Volume = .4f }).GetAwaiter().GetResult();
                var migration = new SaveMigration(1, 2, async (bytes, token) => {
                    var data = await json.DeserializeAsync<SettingsV1>(bytes);
                    return await json.SerializeAsync(new UserSettingsData { Language = data.Locale, MasterVolume = data.Volume });
                });
                var saves = new VersionedSaveService<UserSettingsData>(directory, json, 2, new[] { migration });
                var store = new VersionedUserSettingsStore(saves);
                using (var rig = new Rig(store))
                {
                    Assert.That(store.WasMigrated, Is.True); Assert.That(rig.Localization.CurrentLanguage, Is.EqualTo("fr"));
                    rig.Play(); Assert.That(rig.Backend.Last.Volume, Is.EqualTo(.4f));
                    SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host);
                    rig.Settings.SetMasterVolume(.8f); SettingsHostTest.Wait(rig.Settings.SaveAsync(), rig.Host);
                }
                File.WriteAllBytes(saves.GetFilePath(0), new byte[] { 1, 2, 3 });
                var recoveredStore = new VersionedUserSettingsStore(saves);
                using (var restored = new Rig(recoveredStore))
                {
                    Assert.That(recoveredStore.LastLoadStatus, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
                    Assert.That(restored.Settings.Snapshot.MasterVolume, Is.EqualTo(.4f));
                    Assert.That(restored.Settings.IsDirty, Is.False);
                }
                Assert.That(saves.LoadAsync(0).GetAwaiter().GetResult().Metadata.SchemaVersion, Is.EqualTo(2));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CorruptAndFutureSettingsFailStartupWithoutOverwritingSavedData(bool future)
        {
            string directory = Path.Combine(Path.GetTempPath(), "awith-settings-invalid-" + Guid.NewGuid().ToString("N"));
            try
            {
                var json = new JsonSerializer(); var current = new VersionedSaveService<UserSettingsData>(directory, json, 1);
                if (future) new VersionedSaveService<UserSettingsData>(directory, json, 2).SaveAsync(0, new UserSettingsData()).GetAwaiter().GetResult();
                else { Directory.CreateDirectory(directory); File.WriteAllBytes(current.GetFilePath(0), new byte[] { 4, 5, 6 }); }
                byte[] before = File.ReadAllBytes(current.GetFilePath(0));
                using var rig = new Rig(new VersionedUserSettingsStore(current), initialize: false);
                Exception failure = Assert.Catch(() => SettingsHostTest.Wait(rig.Settings.InitializeAsync(), rig.Host));
                Assert.That(failure, future ? Is.InstanceOf<SaveVersionException>() : Is.InstanceOf<SaveCorruptedException>());
                Assert.That(rig.Settings.IsInitialized, Is.False);
                Assert.That(File.ReadAllBytes(current.GetFilePath(0)), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
