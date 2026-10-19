using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;

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

        public override int Priority => (int)ModulePriority.Features;

        public MusicManager()
        {
            MonoManager.GetInstance().AddUpdateListener(Update);
        }

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<IAudioManager>(this);
        }

        private void Update()
        {
            CleanFinishedSources(_sfxList);
            CleanFinishedSources(_voiceList);
        }

        private void CleanFinishedSources(List<AudioSource> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i].isPlaying)
                {
                    RecycleAudioSource(list[i]);
                    list.RemoveAt(i);
                }
            }
        }

        private AudioSource GetAudioSource(GameObject parent)
        {
            if (_audioSourcePool.Count > 0)
            {
                var source = _audioSourcePool.Dequeue();
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
            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
            _audioSourcePool.Enqueue(source);
        }

        #region BGM

        public void PlayBGM(string name)
        {
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
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(path, (clip) =>
            {
                _clipCache[path] = clip;
                _bgmSource.clip = clip;
                _bgmSource.volume = _bgmVolume;
                _bgmSource.loop = true;
                _bgmSource.Play();
            });
        }

        public void PauseBGM()
        {
            _bgmSource?.Pause();
        }

        public void StopBGM()
        {
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
            EnsureAudioRoot(ref _sfxRoot, "SFX");

            var path = GameConstants.MUSIC_SFX_PATH + name;
            LoadClipAndPlay(path, _sfxRoot, _sfxVolume, loop, _sfxList, callback);
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
            EnsureAudioRoot(ref _voiceRoot, "Voice");

            var path = GameConstants.MUSIC_VOICE_PATH + name;
            LoadClipAndPlay(path, _voiceRoot, _voiceVolume, loop, _voiceList, callback);
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

        private void LoadClipAndPlay(string path, GameObject parent, float volume, bool loop, List<AudioSource> list, UnityAction<AudioSource> callback)
        {
            if (_clipCache.TryGetValue(path, out var cachedClip))
            {
                var src = GetAudioSource(parent);
                src.clip = cachedClip;
                src.volume = volume;
                src.loop = loop;
                src.Play();
                list.Add(src);
                callback?.Invoke(src);
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(path, (clip) =>
            {
                _clipCache[path] = clip;
                var src = GetAudioSource(parent);
                src.clip = clip;
                src.volume = volume;
                src.loop = loop;
                src.Play();
                list.Add(src);
                callback?.Invoke(src);
            });
        }

        #endregion
    }
}
