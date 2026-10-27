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
    /// <summary>Captures runtime facts, commits pure rule results and coordinates effects and revision-aware persistence.</summary>
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
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            this.defaults = UserSettingsRules.CreateDefaults(defaults,
                input != null && defaults.InputBindings?.Count == 0 ? input.CaptureBindings() : null);
            current = UserSettingsRules.Copy(this.defaults);
        }

        internal GameContext Context => context;
        internal bool UsesAudio => audio != null;
        internal bool UsesInput => input != null;
        public bool IsInitialized { get { lock (sync) return initialized; } }
        public bool IsDirty { get { lock (sync) return revision != savedRevision; } }
        public long Revision { get { lock (sync) return revision; } }
        public Exception LastSaveError { get { lock (sync) return lastSaveError; } }
        public UserSettingsData Snapshot { get { lock (sync) return UserSettingsRules.Copy(current); } }

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
                    await OnHostAsync(() =>
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        lock (sync) if (closing) throw new ObjectDisposedException(nameof(UserSettingsService));
                        var restored = UserSettingsRules.Restore(defaults, saved, localization.AvailableLanguages,
                            CaptureAvailableAudioBuses(), input?.CaptureBindings());
                        lock (sync)
                        {
                            current = restored;
                            revision = saved == null || !UserSettingsRules.AreEquivalent(saved, restored) ? 1 : 0;
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
            var next = PrepareCandidate(settings ?? throw new ArgumentNullException(nameof(settings)));
            UserSettingsData previous;
            lock (sync)
            {
                previous = current;
                if (UserSettingsRules.AreEquivalent(previous, next)) return;
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
            var next = PrepareCandidate(Snapshot);
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
                    snapshot = UserSettingsRules.Copy(current);
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

        private UserSettingsData PrepareCandidate(UserSettingsData candidate) =>
            UserSettingsRules.Normalize(candidate, defaults, localization.AvailableLanguages,
                CaptureAvailableAudioBuses(), input?.CaptureBindings());

        // Runtime queries stay here; the rules receive values and never call services.
        private IReadOnlyCollection<string> CaptureAvailableAudioBuses()
        {
            if (audio == null) return null;
            var available = new List<string>();
            foreach (string bus in defaults.BusVolumes.Keys)
                if (audio.HasBus(bus)) available.Add(bus);
            return available.AsReadOnly();
        }

        private void ApplyRuntime(UserSettingsData next, UserSettingsData previous, bool force)
        {
            var errors = new List<Exception>();
            if (input != null && (force || !UserSettingsRules.HaveSameBindings(next.InputBindings, previous.InputBindings)))
                // Preserve actions outside user settings using the latest runtime snapshot.
                Try(() => input.ApplyBindings(UserSettingsRules.BuildInputBindings(next, input.CaptureBindings())), errors);
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
