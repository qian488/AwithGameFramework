using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;

namespace AwithGameFrame.Gameplay.Audio
{
    public enum AudioPlaybackState { Playing, Paused, Stopped }

    public sealed class AudioRequest
    {
        public AudioRequest(string key, string bus = "sfx", float volume = 1, bool loop = false, bool pauseWithHost = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Audio key cannot be empty.", nameof(key));
            if (string.IsNullOrWhiteSpace(bus)) throw new ArgumentException("Audio bus cannot be empty.", nameof(bus));
            AudioService.ValidateVolume(volume, nameof(volume));
            Key = key; Bus = bus; Volume = volume; Loop = loop; PauseWithHost = pauseWithHost;
        }
        public string Key { get; }
        public string Bus { get; }
        public float Volume { get; }
        public bool Loop { get; }
        public bool PauseWithHost { get; }
    }

    public interface IAudioVoice : IDisposable
    {
        bool IsPlaying { get; }
        void SetVolume(float volume);
        void Play();
        void Pause();
        void Resume();
        void Stop();
    }

    public interface IAudioBackend
    {
        /// <summary>Load assets through scope. Clean up partial native objects before throwing.</summary>
        Task<IAudioVoice> CreateAsync(string key, ResourceScope scope, bool loop, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Owns native voices and per-play ResourceScopes. Calls and await continuations belong to the
    /// creating thread; an engine or headless synchronization context must keep asynchronous work there.
    /// </summary>
    public sealed class AudioService : IAsyncDisposable
    {
        private sealed class Bus
        {
            internal float Volume = 1;
            internal bool Muted;
            internal int Capacity;
        }

        internal sealed class Playback
        {
            internal AudioRequest Request;
            internal ResourceScope Scope;
            internal CancellationTokenSource Cancellation;
            internal Task<IAudioVoice> Creation;
            internal IAudioVoice Voice;
            internal AudioHandle Handle;
            internal Task Cleanup;
            internal float Volume;
            internal bool UserPaused, HostPaused, ReportBackgroundFailure;
            internal bool IsPaused => UserPaused || HostPaused;
        }

        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly ResourceStore _resources;
        private readonly IAudioBackend _backend;
        private readonly int _capacity;
        private readonly Dictionary<string, Bus> _buses = new Dictionary<string, Bus>(StringComparer.Ordinal);
        private readonly HashSet<Playback> _playbacks = new HashSet<Playback>();
        private readonly List<Exception> _backgroundErrors = new List<Exception>();
        private float _masterVolume = 1;
        private bool _masterMuted, _hostPaused, _closing, _inBackend;
        private Task _disposeTask;

        public AudioService(ResourceStore resources, IAudioBackend backend, int maxConcurrent = 32)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            if (maxConcurrent <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrent));
            _capacity = maxConcurrent;
            _buses.Add("sfx", new Bus { Capacity = maxConcurrent });
            _buses.Add("music", new Bus { Capacity = 1 });
        }

        public int ActiveCount { get { VerifyThread(); return _playbacks.Count; } }
        public bool IsPaused { get { VerifyThread(); return _hostPaused; } }

        public bool HasBus(string name)
        {
            VerifyOpen();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bus name cannot be empty.", nameof(name));
            return _buses.ContainsKey(name);
        }

        public void ConfigureBus(string name, int maxConcurrent, float volume = 1, bool muted = false)
        {
            VerifyOpen();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bus name cannot be empty.", nameof(name));
            if (maxConcurrent <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrent));
            ValidateVolume(volume, nameof(volume));
            if (!_buses.TryGetValue(name, out var bus)) _buses.Add(name, bus = new Bus());
            if (CountBus(name) > maxConcurrent) throw new InvalidOperationException("Existing playbacks exceed the new bus capacity.");
            bus.Capacity = maxConcurrent; bus.Volume = volume; bus.Muted = muted;
            ApplyVolumes(name);
        }

        public void SetMasterVolume(float volume)
        { VerifyOpen(); ValidateVolume(volume, nameof(volume)); _masterVolume = volume; ApplyVolumes(null); }
        public void SetMasterMuted(bool muted)
        { VerifyOpen(); _masterMuted = muted; ApplyVolumes(null); }
        public void SetBusVolume(string name, float volume)
        { VerifyOpen(); ValidateVolume(volume, nameof(volume)); GetBus(name).Volume = volume; ApplyVolumes(name); }
        public void SetBusMuted(string name, bool muted)
        { VerifyOpen(); GetBus(name).Muted = muted; ApplyVolumes(name); }

        public Task<AudioHandle> PlayAsync(AudioRequest request, CancellationToken cancellationToken = default)
        {
            VerifyOpen();
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var bus = GetBus(request.Bus);
            if (_playbacks.Count >= _capacity || CountBus(request.Bus) >= bus.Capacity)
                throw new InvalidOperationException("Audio concurrency limit reached, including pending loads and cleanup.");
            var playback = new Playback
            {
                Request = request, Volume = request.Volume,
                Scope = new ResourceScope(_resources),
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            };
            _playbacks.Add(playback); // Reserve before invoking a backend, including a synchronous backend.
            try
            {
                _inBackend = true;
                playback.Creation = _backend.CreateAsync(request.Key, playback.Scope, request.Loop, playback.Cancellation.Token)
                    ?? Task.FromException<IAudioVoice>(new InvalidOperationException("Audio backend returned a null Task."));
            }
            catch (Exception error) { playback.Creation = Task.FromException<IAudioVoice>(error); }
            finally { _inBackend = false; }
            return CompletePlayAsync(playback);
        }

        private async Task<AudioHandle> CompletePlayAsync(Playback playback)
        {
            try
            {
                playback.Voice = await WaitForCallerAsync(playback.Creation, playback.Cancellation.Token);
                VerifyThread();
                if (playback.Voice == null) throw new InvalidOperationException("Audio backend returned a null voice.");
                playback.Cancellation.Token.ThrowIfCancellationRequested();
                if (_closing) throw new ObjectDisposedException(nameof(AudioService));
                Invoke(() => playback.Voice.SetVolume(EffectiveVolume(playback)));
                Invoke(playback.Voice.Play);
                playback.HostPaused = _hostPaused && playback.Request.PauseWithHost;
                if (playback.IsPaused) Invoke(playback.Voice.Pause);
                playback.Cancellation.Dispose();
                playback.Cancellation = null; // Caller cancellation controls startup only.
                playback.Handle = new AudioHandle(this, playback, playback.Request);
                return playback.Handle;
            }
            catch (Exception failure)
            {
                bool lateCreation = !playback.Creation.IsCompleted;
                var cleanup = StartCleanup(playback, lateCreation);
                // Cancellation remains prompt even when the backend ignores it. The reserved slot and
                // service still own the late voice until its observed cleanup finishes.
                if (lateCreation)
                {
                    _ = ObserveBackgroundAsync(cleanup);
                    throw;
                }
                try { await cleanup; }
                catch (Exception cleanupFailure)
                { throw new AggregateException("Audio startup failed and cleanup reported errors.", failure, cleanupFailure); }
                throw;
            }
        }

        public void Pause()
        {
            VerifyOpen();
            _hostPaused = true;
            ChangeHostPause(true);
        }

        public void Resume()
        {
            VerifyOpen();
            _hostPaused = false;
            ChangeHostPause(false);
        }

        /// <summary>Releases naturally completed voices and reports completed background cleanup failures.</summary>
        public void Tick()
        {
            VerifyOpen();
            var errors = DrainErrors();
            foreach (var playback in new List<Playback>(_playbacks))
            {
                if (playback.Voice == null || playback.Cleanup != null || playback.IsPaused) continue;
                try
                {
                    bool playing = false;
                    Invoke(() => playing = playback.Voice.IsPlaying);
                    if (!playing) _ = ObserveBackgroundAsync(StartCleanup(playback, true));
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    _ = ObserveBackgroundAsync(StartCleanup(playback, true));
                }
            }
            foreach (var error in DrainErrors()) if (!errors.Contains(error)) errors.Add(error);
            if (errors.Count != 0) throw new AggregateException("Audio background operations reported errors.", errors);
        }

        public IReadOnlyList<Exception> TakeErrors()
        { VerifyThread(); return DrainErrors().AsReadOnly(); }

        public ValueTask DisposeAsync()
        {
            VerifyThread();
            if (_inBackend) throw new InvalidOperationException("Audio backend callbacks cannot reenter the service.");
            if (_disposeTask != null) return new ValueTask(_disposeTask);
            _closing = true;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return new ValueTask(_disposeTask);
        }

        private async Task CompleteDisposeAsync(TaskCompletionSource<bool> completion)
        {
            var errors = DrainErrors();
            var pending = new List<Task>();
            foreach (var playback in new List<Playback>(_playbacks))
            {
                try { playback.Cancellation?.Cancel(); }
                catch (Exception error) { errors.Add(error); }
                pending.Add(StartCleanup(playback));
            }
            foreach (var task in pending)
            {
                try { await task; }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var error in DrainErrors()) if (!errors.Contains(error)) errors.Add(error);
            if (errors.Count == 0) completion.TrySetResult(true);
            else completion.TrySetException(new AggregateException("Audio shutdown reported errors.", errors));
        }

        internal AudioPlaybackState GetState(Playback playback)
        {
            VerifyThread();
            if (playback.Cleanup != null || !_playbacks.Contains(playback)) return AudioPlaybackState.Stopped;
            return playback.IsPaused ? AudioPlaybackState.Paused : AudioPlaybackState.Playing;
        }

        internal void SetTrackVolume(Playback playback, float volume)
        {
            VerifyPlayback(playback);
            ValidateVolume(volume, nameof(volume));
            playback.Volume = volume;
            Invoke(() => playback.Voice.SetVolume(EffectiveVolume(playback)));
        }

        internal void SetUserPause(Playback playback, bool paused)
        {
            VerifyPlayback(playback);
            bool wasPaused = playback.IsPaused;
            playback.UserPaused = paused;
            ApplyPause(playback, wasPaused);
        }

        internal Task StopAsync(Playback playback)
        { VerifyThread(); if (_inBackend) throw new InvalidOperationException("Audio backend callbacks cannot reenter the service."); return StartCleanup(playback); }

        private Task StartCleanup(Playback playback, bool reportBackgroundFailure = false)
        {
            if (reportBackgroundFailure) playback.ReportBackgroundFailure = true;
            if (playback.Cleanup != null) return playback.Cleanup;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            playback.Cleanup = completion.Task;
            playback.Handle?.Detach(completion.Task);
            _ = CleanupAsync(playback, completion);
            return completion.Task;
        }

        private async Task CleanupAsync(Playback playback, TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                if (playback.Voice == null)
                {
                    try { playback.Voice = await playback.Creation; }
                    catch { /* The PlayAsync task reports the creation failure. */ }
                }
                VerifyThread();
                if (playback.Voice != null)
                {
                    try { Invoke(playback.Voice.Stop); }
                    catch (Exception error) { errors.Add(error); }
                    try
                    {
                        if (playback.Voice is IAsyncDisposable asyncVoice)
                        {
                            Task disposal = null;
                            Invoke(() => disposal = asyncVoice.DisposeAsync().AsTask());
                            await disposal;
                            VerifyThread();
                        }
                        else Invoke(playback.Voice.Dispose);
                    }
                    catch (Exception error) { errors.Add(error); }
                    playback.Voice = null;
                }
                try { await playback.Scope.DisposeAsync(); }
                catch (Exception error) { errors.Add(error); }
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                playback.Cancellation?.Dispose();
                playback.Cancellation = null;
                _playbacks.Remove(playback);
                if (errors.Count == 0) completion.TrySetResult(true);
                else
                {
                    var failure = new AggregateException("Audio playback cleanup reported errors.", errors);
                    // Record before completing the Task/removing ownership is externally visible;
                    // shutdown cannot race a deferred observer and lose this cleanup failure.
                    if (playback.ReportBackgroundFailure) _backgroundErrors.Add(failure);
                    completion.TrySetException(failure);
                }
            }
        }

        private async Task ObserveBackgroundAsync(Task task)
        {
            try { await task; }
            catch { /* Cleanup records background failures before its Task completes. */ }
        }

        private void ChangeHostPause(bool paused)
        {
            var errors = new List<Exception>();
            foreach (var playback in _playbacks)
            {
                if (playback.Voice == null || playback.Cleanup != null) continue;
                bool wasPaused = playback.IsPaused;
                playback.HostPaused = paused && playback.Request.PauseWithHost;
                try { ApplyPause(playback, wasPaused); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Audio pause operations reported errors.", errors);
        }

        private void ApplyPause(Playback playback, bool wasPaused)
        {
            if (wasPaused == playback.IsPaused) return;
            if (playback.IsPaused) Invoke(playback.Voice.Pause);
            else Invoke(playback.Voice.Resume);
        }

        private void ApplyVolumes(string bus)
        {
            var errors = new List<Exception>();
            foreach (var playback in _playbacks)
            {
                if (playback.Voice == null || playback.Cleanup != null || (bus != null && playback.Request.Bus != bus)) continue;
                try { Invoke(() => playback.Voice.SetVolume(EffectiveVolume(playback))); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Audio volume operations reported errors.", errors);
        }

        private float EffectiveVolume(Playback playback)
        {
            var bus = _buses[playback.Request.Bus];
            return _masterMuted || bus.Muted ? 0 : _masterVolume * bus.Volume * playback.Volume;
        }
        private int CountBus(string bus)
        { int count = 0; foreach (var playback in _playbacks) if (playback.Request.Bus == bus) count++; return count; }
        private Bus GetBus(string name)
        { if (name == null || !_buses.TryGetValue(name, out var bus)) throw new ArgumentException("Unknown audio bus: " + name, nameof(name)); return bus; }
        private List<Exception> DrainErrors()
        { var errors = new List<Exception>(_backgroundErrors); _backgroundErrors.Clear(); return errors; }
        private void Invoke(Action action)
        { _inBackend = true; try { action(); } finally { _inBackend = false; } }
        private void VerifyPlayback(Playback playback)
        { VerifyOpen(); if (playback.Cleanup != null || !_playbacks.Contains(playback)) throw new ObjectDisposedException(nameof(AudioHandle)); }
        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Audio calls and continuations must remain on the creating thread."); }
        private void VerifyOpen()
        {
            VerifyThread();
            if (_inBackend) throw new InvalidOperationException("Audio backend callbacks cannot reenter the service.");
            if (_closing) throw new ObjectDisposedException(nameof(AudioService));
        }
        internal static void ValidateVolume(float volume, string parameter)
        { if (float.IsNaN(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(parameter); }

        private static async Task<T> WaitForCallerAsync<T>(Task<T> task, CancellationToken token)
        {
            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancellation.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancellation.Task) != task) throw new OperationCanceledException(token);
                token.ThrowIfCancellationRequested();
                return await task;
            }
        }
    }

    public sealed class AudioHandle : IAsyncDisposable
    {
        private AudioService _service;
        private AudioService.Playback _playback;
        private Task _stopTask;
        internal AudioHandle(AudioService service, AudioService.Playback playback, AudioRequest request)
        { _service = service; _playback = playback; Key = request.Key; Bus = request.Bus; }
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Key { get; }
        public string Bus { get; }
        public AudioPlaybackState State => _service == null ? AudioPlaybackState.Stopped : _service.GetState(_playback);
        public void SetVolume(float volume) { Service.SetTrackVolume(_playback, volume); }
        public void Pause() { Service.SetUserPause(_playback, true); }
        public void Resume() { Service.SetUserPause(_playback, false); }
        public Task StopAsync() => _stopTask ?? Service.StopAsync(_playback);
        public ValueTask DisposeAsync() => new ValueTask(StopAsync());
        private AudioService Service => _service ?? throw new ObjectDisposedException(nameof(AudioHandle));
        internal void Detach(Task stopTask) { _stopTask = stopTask; _service = null; _playback = null; }
    }

    public sealed class AudioModule : GameModule, IGameMaintenanceModule
    {
        private readonly AudioService _audio;
        private readonly IReadOnlyCollection<string> _dependencies;
        public AudioModule(AudioService audio) : this(audio, new[] { "resources" }) { }
        /// <summary>Pass an explicit empty array for a ResourceStore owned outside this host.</summary>
        public AudioModule(AudioService audio, params string[] dependencies)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _dependencies = new List<string>(dependencies ?? Array.Empty<string>()).AsReadOnly();
        }
        public override string Id => "audio";
        public override IReadOnlyCollection<string> Dependencies => _dependencies;
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        { context.Services.Register(_audio); return Task.CompletedTask; }
        public override void Tick(GameContext context, double deltaSeconds) { _audio.Tick(); }
        public void Maintain(GameContext context) { _audio.Tick(); }
        public override Task PauseAsync(GameContext context, CancellationToken cancellationToken)
        { _audio.Pause(); return Task.CompletedTask; }
        public override Task ResumeAsync(GameContext context, CancellationToken cancellationToken)
        { _audio.Resume(); return Task.CompletedTask; }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => _audio.DisposeAsync().AsTask();
    }
}
