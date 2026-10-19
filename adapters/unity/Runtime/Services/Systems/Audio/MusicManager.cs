using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Core.Config;

namespace AwithGameFrame.Foundation.Systems.Audio
{
    /// <summary>
    /// 音频管理器
    /// 负责BGM、SFX、Voice的播放和管理
    /// </summary>
    public class MusicManager : BaseManager<MusicManager>, IAudioManager
    {
        private AudioSource _bgmSource;
        private float _bgmVolume = 1f;

        private GameObject _sfxRoot;
        private readonly List<AudioSource> _sfxList = new List<AudioSource>();
        private float _sfxVolume = 1f;

        private GameObject _voiceRoot;
        private readonly List<AudioSource> _voiceList = new List<AudioSource>();
        private float _voiceVolume = 1f;

        private readonly Queue<AudioSource> _audioSourcePool = new Queue<AudioSource>();
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();
        private readonly HashSet<AudioSource> _pausedSources = new HashSet<AudioSource>();
        private bool _initialized;
        private bool _applicationPaused;
        private int _generation;
        private int _bgmRequest;
        private int _pendingSfx;
        private int _maxConcurrentSfx = 10;

        public override int Priority => (int)ModulePriority.Features;

        public override void Initialize()
        {
            if (_initialized) return;
            base.Initialize();
            _initialized = true;
            _generation++;
            MonoManager.GetInstance().AddUpdateListener(Update);
            EventCenter.GetInstance().AddEventListener<ConfigAPI.ConfigChangedEventData>("ConfigChanged", OnConfigChanged);
            ApplyConfiguration();
            ServiceLocator.Register<IAudioManager>(this);
        }

        public override void Shutdown()
        {
            if (_initialized)
            {
                MonoManager.GetInstance().RemoveUpdateListener(Update);
                EventCenter.GetInstance().RemoveEventListener<ConfigAPI.ConfigChangedEventData>("ConfigChanged", OnConfigChanged);
            }
            _initialized = false;
            _generation++;
            _bgmRequest++;
            if (_bgmSource != null) Object.Destroy(_bgmSource.gameObject);
            if (_sfxRoot != null) Object.Destroy(_sfxRoot);
            if (_voiceRoot != null) Object.Destroy(_voiceRoot);
            _bgmSource = null;
            _sfxRoot = _voiceRoot = null;
            _sfxList.Clear();
            _voiceList.Clear();
            _audioSourcePool.Clear();
            _clipCache.Clear();
            _pausedSources.Clear();
            _pendingSfx = 0;
            _applicationPaused = false;
            ServiceLocator.Unregister<IAudioManager>();
            base.Shutdown();
        }

        private void OnConfigChanged(ConfigAPI.ConfigChangedEventData change)
        {
            if (change.key == "FrameworkConfig") ApplyConfiguration();
        }

        private void ApplyConfiguration()
        {
            var config = ConfigManager.GetInstance().FrameworkConfig;
            ChangeBGMVolume(Mathf.Clamp01(config.defaultVolume));
            ChangeSFXVolume(Mathf.Clamp01(config.defaultVolume));
            ChangeVoiceVolume(Mathf.Clamp01(config.defaultVolume));
            _maxConcurrentSfx = Mathf.Max(1, config.maxConcurrentSFX);
        }

        public void SetApplicationPaused(bool paused)
        {
            if (_applicationPaused == paused) return;
            _applicationPaused = paused;
            if (paused)
            {
                PauseForApplication(_bgmSource);
                foreach (var source in _sfxList) PauseForApplication(source);
                foreach (var source in _voiceList) PauseForApplication(source);
            }
            else
            {
                foreach (var source in _pausedSources) if (source != null) source.UnPause();
                _pausedSources.Clear();
            }
        }

        private void PauseForApplication(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Pause();
                _pausedSources.Add(source);
            }
        }

        private void Update()
        {
            if (_applicationPaused) return;
            CleanFinishedSources(_sfxList);
            CleanFinishedSources(_voiceList);
        }

        private void CleanFinishedSources(List<AudioSource> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null || !list[i].isPlaying)
                {
                    RecycleAudioSource(list[i]);
                    list.RemoveAt(i);
                }
            }
        }

        private AudioSource GetAudioSource(GameObject parent)
        {
            while (_audioSourcePool.Count > 0)
            {
                var source = _audioSourcePool.Dequeue();
                if (source == null) continue;
                source.gameObject.SetActive(true);
                source.transform.SetParent(parent.transform);
                return source;
            }
            var newSource = new GameObject("AudioSource").AddComponent<AudioSource>();
            newSource.transform.SetParent(parent.transform);
            return newSource;
        }

        private void RecycleAudioSource(AudioSource source)
        {
            if (source == null) return;
            _pausedSources.Remove(source);
            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
            _audioSourcePool.Enqueue(source);
        }

        #region BGM

        public void PlayBGM(string name)
        {
            if (!_initialized) return;
            var generation = _generation;
            var request = ++_bgmRequest;
            if (_bgmSource == null)
            {
                var go = new GameObject("BGM");
                Object.DontDestroyOnLoad(go);
                _bgmSource = go.AddComponent<AudioSource>();
            }

            var path = GameConstants.MUSIC_BGM_PATH + name;
            if (_clipCache.TryGetValue(path, out var cachedClip))
            {
                _bgmSource.clip = cachedClip;
                _bgmSource.volume = _bgmVolume;
                _bgmSource.loop = true;
                _bgmSource.Play();
                if (_applicationPaused) PauseForApplication(_bgmSource);
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(path, (clip) =>
            {
                if (!_initialized || generation != _generation || request != _bgmRequest || clip == null) return;
                _clipCache[path] = clip;
                _bgmSource.clip = clip;
                _bgmSource.volume = _bgmVolume;
                _bgmSource.loop = true;
                _bgmSource.Play();
                if (_applicationPaused) PauseForApplication(_bgmSource);
            });
        }

        public void PauseBGM()
        {
            _pausedSources.Remove(_bgmSource);
            _bgmSource?.Pause();
        }

        public void StopBGM()
        {
            _bgmRequest++;
            _pausedSources.Remove(_bgmSource);
            _bgmSource?.Stop();
        }

        public void ChangeBGMVolume(float value)
        {
            _bgmVolume = value;
            if (_bgmSource != null) _bgmSource.volume = value;
        }

        #endregion

        #region SFX

        public void PlaySFX(string name, bool loop = false, UnityAction<AudioSource> callback = null)
        {
            if (!_initialized || _sfxList.Count + _pendingSfx >= _maxConcurrentSfx) { callback?.Invoke(null); return; }
            EnsureAudioRoot(ref _sfxRoot, "SFX");

            var path = GameConstants.MUSIC_SFX_PATH + name;
            var generation = _generation;
            _pendingSfx++;
            LoadClipAndPlay(path, _sfxRoot, loop, _sfxList, source =>
            {
                if (generation == _generation) _pendingSfx--;
                callback?.Invoke(source);
            });
        }

        public void StopSFX(AudioSource source)
        {
            if (source != null && _sfxList.Remove(source))
                RecycleAudioSource(source);
        }

        public void ChangeSFXVolume(float value)
        {
            _sfxVolume = value;
            foreach (var sfx in _sfxList) sfx.volume = value;
        }

        #endregion

        #region Voice

        public void PlayVoice(string name, bool loop = false, UnityAction<AudioSource> callback = null)
        {
            if (!_initialized) { callback?.Invoke(null); return; }
            EnsureAudioRoot(ref _voiceRoot, "Voice");

            var path = GameConstants.MUSIC_VOICE_PATH + name;
            LoadClipAndPlay(path, _voiceRoot, loop, _voiceList, callback);
        }

        public void StopVoice(AudioSource source)
        {
            if (source != null && _voiceList.Remove(source))
                RecycleAudioSource(source);
        }

        public void ChangeVoiceVolume(float value)
        {
            _voiceVolume = value;
            foreach (var voice in _voiceList) voice.volume = value;
        }

        #endregion

        #region Helpers

        private void EnsureAudioRoot(ref GameObject root, string name)
        {
            if (root == null)
            {
                root = new GameObject(name);
                Object.DontDestroyOnLoad(root);
            }
        }

        private void LoadClipAndPlay(string path, GameObject parent, bool loop, List<AudioSource> list, UnityAction<AudioSource> callback)
        {
            var generation = _generation;
            if (_clipCache.TryGetValue(path, out var cachedClip))
            {
                var src = GetAudioSource(parent);
                src.clip = cachedClip;
                src.volume = list == _sfxList ? _sfxVolume : _voiceVolume;
                src.loop = loop;
                src.Play();
                list.Add(src);
                if (_applicationPaused) PauseForApplication(src);
                callback?.Invoke(src);
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(path, (clip) =>
            {
                if (!_initialized || generation != _generation || parent == null || clip == null) { callback?.Invoke(null); return; }
                _clipCache[path] = clip;
                var src = GetAudioSource(parent);
                src.clip = clip;
                src.volume = list == _sfxList ? _sfxVolume : _voiceVolume;
                src.loop = loop;
                src.Play();
                list.Add(src);
                if (_applicationPaused) PauseForApplication(src);
                callback?.Invoke(src);
            });
        }

        #endregion
    }
}
