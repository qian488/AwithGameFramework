using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;

namespace AwithGameFrame.Gameplay.Settings
{
    /// <summary>Host-thread mutations, explicit runtime application and revision-aware serialized persistence.</summary>
    public sealed class UserSettingsService : IAsyncDisposable
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim saveGate = new SemaphoreSlim(1, 1);
        private readonly GameContext context;
        private readonly IUserSettingsStore store;
        private readonly LocalizationService localization;
        private readonly AudioService audio;
        private readonly InputService input;
        private readonly UserSettingsData defaults;
        private UserSettingsData current;
        private long revision, savedRevision;
        private bool initialized, initializing, applying, closing, stopped;
        private Task stopTask;
        private Exception lastSaveError;

        public UserSettingsService(GameContext context, IUserSettingsStore store, UserSettingsData defaults,
            LocalizationService localization, AudioService audio = null, InputService input = null)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
            if (localization.Context != context) throw new ArgumentException("Localization belongs to a different host.", nameof(localization));
            if (!context.Dispatcher.CheckAccess) throw new InvalidOperationException("Create user settings on the host thread.");
            this.audio = audio;
            this.input = input;
            this.defaults = Copy(defaults ?? throw new ArgumentNullException(nameof(defaults)));
            if (this.defaults.BusVolumes.Count == 0)
            {
                this.defaults.BusVolumes.Add("sfx", 1);
                this.defaults.BusVolumes.Add("music", 1);
            }
            foreach (string bus in this.defaults.BusVolumes.Keys)
                if (!this.defaults.BusMuted.ContainsKey(bus)) this.defaults.BusMuted.Add(bus, false);
            foreach (string bus in this.defaults.BusMuted.Keys)
                if (!this.defaults.BusVolumes.ContainsKey(bus)) throw new ArgumentException("A muted settings bus must have a configured volume.", nameof(defaults));
            if (input != null && this.defaults.InputBindings.Count == 0)
                foreach (var binding in input.CaptureBindings()) this.defaults.InputBindings.Add(FromBinding(binding));
            ValidateShape(this.defaults);
            current = Copy(this.defaults);
        }

        internal GameContext Context => context;
        internal bool UsesAudio => audio != null;
        internal bool UsesInput => input != null;
        public bool IsInitialized { get { lock (sync) return initialized; } }
        public bool IsDirty { get { lock (sync) return revision != savedRevision; } }
        public long Revision { get { lock (sync) return revision; } }
        public Exception LastSaveError { get { lock (sync) return lastSaveError; } }
        public UserSettingsData Snapshot { get { lock (sync) return Copy(current); } }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (closing) throw new ObjectDisposedException(nameof(UserSettingsService));
                if (initialized) return;
                if (initializing) throw new InvalidOperationException("User settings are already loading.");
                initializing = true;
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.LifetimeToken))
            {
                try
                {
                    var saved = await store.LoadAsync(linked.Token).ConfigureAwait(false);
                    var restored = saved == null ? Copy(defaults) : Merge(saved);
                    await OnHostAsync(() =>
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        lock (sync) if (closing) throw new ObjectDisposedException(nameof(UserSettingsService));
                        ValidateRuntime(restored);
                        lock (sync)
                        {
                            current = restored;
                            revision = saved == null || !Equal(saved, restored) ? 1 : 0;
                            savedRevision = 0;
                        }
                        ApplyRuntime(restored, null, true);
                        lock (sync) initialized = true;
                        PublishChanged();
                    }, linked.Token).ConfigureAwait(false);
                }
                finally { lock (sync) initializing = false; }
            }
        }

        public void SetLanguage(string language) { var next = Editable(); next.Language = language; Update(next); }
        public void SetMasterVolume(float volume) { var next = Editable(); next.MasterVolume = volume; Update(next); }
        public void SetMasterMuted(bool muted) { var next = Editable(); next.MasterMuted = muted; Update(next); }
        public void SetBusVolume(string bus, float volume)
        {
            var next = Editable();
            if (bus == null || !next.BusVolumes.ContainsKey(bus)) throw new ArgumentException("The settings bus is not defined.", nameof(bus));
            next.BusVolumes[bus] = volume; Update(next);
        }
        public void SetBusMuted(string bus, bool muted)
        {
            var next = Editable();
            if (bus == null || !next.BusMuted.ContainsKey(bus)) throw new ArgumentException("The settings bus is not defined.", nameof(bus));
            next.BusMuted[bus] = muted; Update(next);
        }
        public void Rebind(string contextId, string action, string control, float scale = 1, float deadZone = 0.1f)
        {
            var next = Editable();
            var binding = next.InputBindings.Find(item => item.ContextId == contextId && item.Action == action);
            if (binding == null) throw new ArgumentException("The settings action is not defined.", nameof(action));
            binding.Control = control; binding.Scale = scale; binding.DeadZone = deadZone; Update(next);
        }

        /// <summary>Validate everything before commit. Runtime/listener errors leave the committed desired settings dirty.</summary>
        public void Update(UserSettingsData settings)
        {
            VerifyMutation();
            var next = Copy(settings ?? throw new ArgumentNullException(nameof(settings)));
            ValidateRuntime(next);
            UserSettingsData previous;
            lock (sync)
            {
                previous = current;
                if (Equal(previous, next)) return;
                current = next;
                revision++;
                applying = true;
            }
            var errors = new List<Exception>();
            try
            {
                Try(() => ApplyRuntime(next, previous, false), errors);
                Try(PublishChanged, errors);
            }
            finally { lock (sync) applying = false; }
            if (errors.Count != 0) throw new AggregateException("Settings committed, but runtime application or listeners failed. Reapply after fixing the cause.", errors);
        }

        public void ResetToDefaults() => Update(defaults);
        public void Reapply()
        {
            VerifyMutation();
            var next = Snapshot;
            ValidateRuntime(next);
            lock (sync) applying = true;
            try { ApplyRuntime(next, null, true); }
            finally { lock (sync) applying = false; }
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => SaveCoreAsync(false, cancellationToken);

        private async Task SaveCoreAsync(bool duringStop, CancellationToken cancellationToken)
        {
            await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                UserSettingsData snapshot;
                long writingRevision;
                lock (sync)
                {
                    if (!initialized) throw new InvalidOperationException("Load settings before saving.");
                    if (stopped || (closing && !duringStop)) throw new ObjectDisposedException(nameof(UserSettingsService));
                    if (revision == savedRevision) return;
                    snapshot = Copy(current);
                    writingRevision = revision;
                }
                try
                {
                    await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    lock (sync) { savedRevision = writingRevision; lastSaveError = null; }
                }
                catch (Exception error) { lock (sync) lastSaveError = error; throw; }
            }
            finally { saveGate.Release(); }
        }

        /// <summary>Reject changes, join earlier saves, then persist the latest revision. A failed stop remains retryable.</summary>
        public Task StopAsync()
        {
            lock (sync)
            {
                if (stopped) return Task.CompletedTask;
                if (stopTask != null && !stopTask.IsCompleted) return stopTask;
                closing = true;
                stopTask = CompleteStopAsync();
                return stopTask;
            }
        }
        private async Task CompleteStopAsync()
        {
            bool ready;
            lock (sync) ready = initialized;
            if (ready) await SaveCoreAsync(true, CancellationToken.None).ConfigureAwait(false);
            lock (sync) stopped = true;
        }
        public ValueTask DisposeAsync() => new ValueTask(StopAsync());

        private UserSettingsData Editable() { VerifyMutation(); return Snapshot; }
        private void VerifyMutation()
        {
            if (!context.Dispatcher.CheckAccess) throw new InvalidOperationException("Change settings on the host thread.");
            lock (sync)
            {
                if (closing) throw new ObjectDisposedException(nameof(UserSettingsService));
                if (!initialized || initializing) throw new InvalidOperationException("Settings are not ready.");
                if (applying) throw new InvalidOperationException("Settings callbacks cannot reenter mutations; dispatch for a later frame.");
                context.LifetimeToken.ThrowIfCancellationRequested();
            }
        }

        private UserSettingsData Merge(UserSettingsData saved)
        {
            ValidateShape(saved);
            var merged = Copy(defaults);
            merged.Language = saved.Language;
            merged.MasterVolume = saved.MasterVolume;
            merged.MasterMuted = saved.MasterMuted;
            foreach (var pair in saved.BusVolumes)
            {
                if (!merged.BusVolumes.ContainsKey(pair.Key)) throw new ArgumentException("Saved settings contain an unknown audio bus: " + pair.Key);
                merged.BusVolumes[pair.Key] = pair.Value;
            }
            foreach (var pair in saved.BusMuted)
            {
                if (!merged.BusMuted.ContainsKey(pair.Key)) throw new ArgumentException("Saved settings contain an unknown audio bus: " + pair.Key);
                merged.BusMuted[pair.Key] = pair.Value;
            }
            foreach (var binding in saved.InputBindings)
            {
                int index = merged.InputBindings.FindIndex(item => item.ContextId == binding.ContextId && item.Action == binding.Action);
                if (index < 0) throw new ArgumentException("Saved settings contain an unknown input action.");
                merged.InputBindings[index] = CopyBinding(binding);
            }
            return merged;
        }

        private void ValidateRuntime(UserSettingsData settings)
        {
            ValidateShape(settings);
            if (!localization.IsLanguageSupported(settings.Language)) throw new ArgumentException("The settings language has no localization dataset.");
            settings.Language = LocalizationService.NormalizeLanguage(settings.Language);
            if (settings.BusVolumes.Count != defaults.BusVolumes.Count || settings.BusMuted.Count != defaults.BusMuted.Count)
                throw new ArgumentException("Settings must include every configured audio bus.");
            foreach (string bus in settings.BusVolumes.Keys)
                if (!defaults.BusVolumes.ContainsKey(bus) || !settings.BusMuted.ContainsKey(bus) || (audio != null && !audio.HasBus(bus)))
                    throw new ArgumentException("The settings audio bus is not configured: " + bus);
            if (settings.InputBindings.Count != defaults.InputBindings.Count) throw new ArgumentException("Settings must include every configurable input action.");
            foreach (var binding in settings.InputBindings)
                if (!defaults.InputBindings.Exists(item => item.ContextId == binding.ContextId && item.Action == binding.Action))
                    throw new ArgumentException("The settings input action is not configured.");
            if (input != null) BuildInputSnapshot(settings);
        }

        private IReadOnlyList<InputBindingSnapshot> BuildInputSnapshot(UserSettingsData settings)
        {
            var captured = input.CaptureBindings();
            var result = new List<InputBindingSnapshot>(captured);
            foreach (var binding in settings.InputBindings)
            {
                int index = result.FindIndex(item => item.ContextId == binding.ContextId && item.Action == binding.Action);
                if (index < 0) throw new ArgumentException("A settings input context/action has not been created.");
                result[index] = new InputBindingSnapshot(binding.ContextId, binding.Action, binding.Control, binding.Scale, binding.DeadZone);
            }
            var controls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in result)
                if (!controls.Add(binding.ContextId + "\0" + binding.Control)) throw new ArgumentException("Settings create a control conflict in one input context.");
            return result.AsReadOnly();
        }

        private void ApplyRuntime(UserSettingsData next, UserSettingsData previous, bool force)
        {
            var errors = new List<Exception>();
            if (input != null && (force || !BindingsEqual(next.InputBindings, previous.InputBindings)))
                Try(() => input.ApplyBindings(BuildInputSnapshot(next)), errors);
            if (audio != null)
            {
                if (force || next.MasterVolume != previous.MasterVolume) Try(() => audio.SetMasterVolume(next.MasterVolume), errors);
                if (force || next.MasterMuted != previous.MasterMuted) Try(() => audio.SetMasterMuted(next.MasterMuted), errors);
                foreach (var bus in next.BusVolumes)
                {
                    if (force || bus.Value != previous.BusVolumes[bus.Key]) Try(() => audio.SetBusVolume(bus.Key, bus.Value), errors);
                    if (force || next.BusMuted[bus.Key] != previous.BusMuted[bus.Key]) Try(() => audio.SetBusMuted(bus.Key, next.BusMuted[bus.Key]), errors);
                }
            }
            if (force || next.Language != previous.Language) Try(() => localization.SwitchLanguage(next.Language), errors);
            if (errors.Count != 0) throw new AggregateException("Runtime settings application failed.", errors);
        }

        private void PublishChanged() => context.Events.Publish(new UserSettingsChanged(this, Revision, Snapshot)).ThrowIfFailed();
        private Task OnHostAsync(Action action, CancellationToken token)
        {
            if (context.Dispatcher.CheckAccess) { token.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
            return context.Dispatcher.InvokeAsync(action, token);
        }
        private static void Try(Action action, List<Exception> errors) { try { action(); } catch (Exception error) { errors.Add(error); } }

        private static void ValidateShape(UserSettingsData settings)
        {
            LocalizationService.NormalizeLanguage(settings.Language);
            Volume(settings.MasterVolume);
            if (settings.BusVolumes == null || settings.BusMuted == null || settings.InputBindings == null) throw new ArgumentException("Settings collections cannot be null.");
            foreach (var pair in settings.BusVolumes) { Name(pair.Key); Volume(pair.Value); }
            foreach (var pair in settings.BusMuted) Name(pair.Key);
            var actions = new HashSet<string>(StringComparer.Ordinal);
            var controls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in settings.InputBindings)
            {
                if (binding == null) throw new ArgumentException("Input settings cannot contain null.");
                Name(binding.ContextId); Name(binding.Action); Name(binding.Control);
                if (float.IsNaN(binding.Scale) || float.IsInfinity(binding.Scale) || binding.Scale == 0 || Math.Abs(binding.Scale) > 1 ||
                    float.IsNaN(binding.DeadZone) || binding.DeadZone < 0 || binding.DeadZone >= 1)
                    throw new ArgumentException("Input scale and dead zone are invalid.");
                if (!actions.Add(binding.ContextId + "\0" + binding.Action) || !controls.Add(binding.ContextId + "\0" + binding.Control))
                    throw new ArgumentException("Input actions and controls must be unique within their context.");
            }
        }
        private static void Name(string value)
        { if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0) throw new ArgumentException("Settings names cannot be empty or contain NUL."); }
        private static void Volume(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value), "Volume must be finite in [0, 1]."); }
        private static UserInputBinding FromBinding(InputBindingSnapshot item) => new UserInputBinding
        { ContextId = item.ContextId, Action = item.Action, Control = item.Control, Scale = item.Scale, DeadZone = item.DeadZone };
        private static UserInputBinding CopyBinding(UserInputBinding item)
        {
            if (item == null) throw new ArgumentException("Input settings cannot contain null.");
            return new UserInputBinding { ContextId = item.ContextId, Action = item.Action, Control = item.Control, Scale = item.Scale, DeadZone = item.DeadZone };
        }
        internal static UserSettingsData Copy(UserSettingsData source)
        {
            if (source.BusVolumes == null || source.BusMuted == null || source.InputBindings == null) throw new ArgumentException("Settings collections cannot be null.");
            var copy = new UserSettingsData { Language = source.Language, MasterVolume = source.MasterVolume, MasterMuted = source.MasterMuted,
                BusVolumes = new Dictionary<string, float>(source.BusVolumes, StringComparer.Ordinal),
                BusMuted = new Dictionary<string, bool>(source.BusMuted, StringComparer.Ordinal) };
            foreach (var binding in source.InputBindings) copy.InputBindings.Add(CopyBinding(binding));
            return copy;
        }
        private static bool BindingsEqual(List<UserInputBinding> a, List<UserInputBinding> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var left in a)
            {
                var right = b.Find(item => item.ContextId == left.ContextId && item.Action == left.Action);
                if (right == null || right.Control != left.Control || right.Scale != left.Scale || right.DeadZone != left.DeadZone) return false;
            }
            return true;
        }
        private static bool Equal(UserSettingsData a, UserSettingsData b)
        {
            if (a.Language != b.Language || a.MasterVolume != b.MasterVolume || a.MasterMuted != b.MasterMuted ||
                a.BusVolumes.Count != b.BusVolumes.Count || a.BusMuted.Count != b.BusMuted.Count || !BindingsEqual(a.InputBindings, b.InputBindings)) return false;
            foreach (var pair in a.BusVolumes) if (!b.BusVolumes.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
            foreach (var pair in a.BusMuted) if (!b.BusMuted.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
            return true;
        }
    }

    public sealed class UserSettingsModule : GameModule
    {
        private readonly UserSettingsService service;
        public UserSettingsModule(UserSettingsService service, IReadOnlyCollection<string> dependencies = null, string id = "settings")
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A module ID is required.", nameof(id));
            Id = id;
            var required = new List<string>(dependencies ?? new[] { "localization" });
            if (dependencies == null)
            {
                if (service.UsesAudio) required.Add("audio");
                if (service.UsesInput) required.Add("input");
            }
            Dependencies = required.AsReadOnly();
        }
        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override async Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            if (context != service.Context) throw new InvalidOperationException("Settings belong to a different host.");
            await service.InitializeAsync(cancellationToken).ConfigureAwait(false);
            context.Services.Register(service);
        }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => service.StopAsync();
    }
}
