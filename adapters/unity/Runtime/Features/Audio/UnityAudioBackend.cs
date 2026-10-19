using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Gameplay.Audio;
using UnityEngine;

namespace AwithGameFrame.Unity.Audio
{
    public sealed class UnityAudioBackend : IAudioBackend
    {
        private readonly Transform _parent;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        public UnityAudioBackend(Transform parent = null) { _parent = parent; }

        public async Task<IAudioVoice> CreateAsync(string key, ResourceScope scope, bool loop, CancellationToken cancellationToken)
        {
            VerifyThread();
            var clip = await scope.LoadAsync<AudioClip>(key, cancellationToken);
            VerifyThread();
            cancellationToken.ThrowIfCancellationRequested();
            GameObject instance = null;
            try
            {
                instance = new GameObject("Audio: " + key);
                if (_parent != null) instance.transform.SetParent(_parent, false);
                var source = instance.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                source.loop = loop;
                source.clip = clip.Value;
                return new Voice(instance, source, _thread);
            }
            catch
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                throw;
            }
        }

        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Unity audio must remain on the engine thread."); }

        private sealed class Voice : IAudioVoice
        {
            private GameObject _instance;
            private AudioSource _source;
            private readonly int _thread;
            internal Voice(GameObject instance, AudioSource source, int thread) { _instance = instance; _source = source; _thread = thread; }
            public bool IsPlaying { get { Check(); return _source.isPlaying; } }
            public void SetVolume(float volume) { Check(); _source.volume = volume; }
            public void Play() { Check(); _source.Play(); }
            public void Pause() { Check(); _source.Pause(); }
            public void Resume() { Check(); _source.UnPause(); }
            public void Stop() { Check(); _source.Stop(); }
            public void Dispose()
            {
                CheckThread();
                if (_instance == null) return;
                if (_source != null) { _source.Stop(); _source.clip = null; }
                UnityEngine.Object.Destroy(_instance);
                _instance = null; _source = null;
            }
            private void Check() { CheckThread(); if (_source == null) throw new ObjectDisposedException(nameof(Voice)); }
            private void CheckThread()
            { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Unity audio must remain on the engine thread."); }
        }
    }
}
