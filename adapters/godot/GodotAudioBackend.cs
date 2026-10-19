using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Gameplay.Audio;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>
    /// Owns one player and private loop-configured stream per voice. AudioService awaits
    /// each voice's IAsyncDisposable until the mixer releases playback ownership, before
    /// releasing its resource scope. Keep the SceneTree pumping until shutdown completes;
    /// mixer cleanup exceeding five seconds fails explicitly after releasing our references.
    /// </summary>
    public sealed class GodotAudioBackend : IAudioBackend
    {
        private readonly Node _parent;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        public GodotAudioBackend(Node parent) { _parent = parent ?? throw new ArgumentNullException(nameof(parent)); }

        public async Task<IAudioVoice> CreateAsync(string key, ResourceScope scope, bool loop, CancellationToken cancellationToken)
        {
            VerifyThread();
            var lease = await scope.LoadAsync<AudioStream>(key, cancellationToken);
            VerifyThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (!GodotObject.IsInstanceValid(_parent) || !_parent.IsInsideTree()) throw new InvalidOperationException("Audio parent must be alive inside the SceneTree.");
            AudioStreamPlayer player = null;
            AudioStream stream = null;
            try
            {
                // Loop flags live on AudioStream resources. Duplicate so per-play settings cannot
                // mutate the ResourceStore's shared asset or another voice using the same key.
                stream = (AudioStream)lease.Value.Duplicate();
                if (stream is AudioStreamWav wav)
                {
                    wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                    if (loop && wav.LoopEnd <= wav.LoopBegin)
                    {
                        long samples = (long)Math.Round(wav.GetLength() * wav.MixRate);
                        if (samples <= 0 || samples > int.MaxValue) throw new NotSupportedException("WAV loop requires a finite, nonempty sample range.");
                        wav.LoopBegin = 0; wav.LoopEnd = (int)samples;
                    }
                }
                else if (stream is AudioStreamOggVorbis ogg) ogg.Loop = loop;
                else if (stream is AudioStreamMP3 mp3) mp3.Loop = loop;
                else if (loop) throw new NotSupportedException("Looping requires WAV, Ogg Vorbis or MP3 streams.");
                player = new AudioStreamPlayer { Name = "AwithAudio", Stream = stream, ProcessMode = Node.ProcessModeEnum.Always };
                _parent.AddChild(player);
                return new Voice(player, stream, _thread);
            }
            catch
            {
                if (GodotObject.IsInstanceValid(player)) { player.Stream = null; player.Free(); }
                stream?.Dispose();
                throw;
            }
        }

        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Godot audio must remain on the engine thread."); }

        private sealed class Voice : IAudioVoice, IAsyncDisposable
        {
            private AudioStreamPlayer _player;
            private AudioStream _stream;
            private readonly SceneTree _tree;
            private readonly int _thread;
            private Task _disposing;
            internal Voice(AudioStreamPlayer player, AudioStream stream, int thread)
            { _player = player; _stream = stream; _thread = thread; _tree = player.GetTree(); }
            public bool IsPlaying { get { Check(); return _player.Playing; } }
            public void SetVolume(float volume) { Check(); _player.VolumeLinear = volume; }
            public void Play() { Check(); _player.Play(); }
            public void Pause() { Check(); _player.StreamPaused = true; }
            public void Resume() { Check(); _player.StreamPaused = false; }
            public void Stop() { Check(); _player.Stop(); }
            public void Dispose()
            {
                CheckThread();
                StopAndFreePlayer();
                ReleaseStream();
            }
            public ValueTask DisposeAsync()
            {
                CheckThread();
                if (_disposing == null) _disposing = DisposeCoreAsync();
                return new ValueTask(_disposing);
            }
            private async Task DisposeCoreAsync()
            {
                StopAndFreePlayer();
                var timeout = Stopwatch.StartNew();
                try
                {
                    // Stop marks the server playback for a fade-out. Its mixer and
                    // subsequent main-thread update still own our private stream until
                    // deletion completes. Wait for that actual ownership to end before
                    // AudioService releases the voice's resource scope or quits the tree.
                    while (GodotObject.IsInstanceValid(_stream) && _stream.GetReferenceCount() > 1)
                    {
                        if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Godot audio mixer did not release the stopped playback within five seconds.");
                        if (!GodotObject.IsInstanceValid(_tree)) throw new InvalidOperationException("Await audio shutdown before freeing the SceneTree.");
                        await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
                        CheckThread();
                    }
                }
                finally { ReleaseStream(); }
            }
            private void StopAndFreePlayer()
            {
                if (_player == null) return;
                if (GodotObject.IsInstanceValid(_player))
                {
                    _player.Stop(); _player.Stream = null;
                    _player.GetParent()?.RemoveChild(_player);
                    // IAudioVoice.Dispose completes synchronously. Destroy the detached
                    // player now so it cannot outlive AudioService's resource release.
                    _player.Free();
                }
                _player = null;
            }
            private void ReleaseStream() { _stream?.Dispose(); _stream = null; }
            private void Check() { CheckThread(); if (!GodotObject.IsInstanceValid(_player)) throw new ObjectDisposedException(nameof(Voice)); }
            private void CheckThread()
            { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Godot audio must remain on the engine thread."); }
        }
    }
}
