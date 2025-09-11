using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Foundation.Logging;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Foundation.Systems.Audio
{
    /// <summary>
    /// 音频管理器
    /// 负责BGM、SFX、Voice的播放和管理
    /// </summary>
    public class MusicManager : BaseManager<MusicManager>
    {
        #region 字段
        /// <summary>背景音乐音频源</summary>
        private AudioSource BGM = null;
        /// <summary>背景音乐音量</summary>
        private float BGMValue = 1f;

        /// <summary>音效父对象</summary>
        private GameObject SFXGO = null;
        /// <summary>音效音频源列表</summary>
        private List<AudioSource> SFXList = new List<AudioSource>();
        /// <summary>音效音量</summary>
        private float SFXValue = 1f;

        /// <summary>语音父对象</summary>
        private GameObject VoiceGO = null;
        /// <summary>语音音频源列表</summary>
        private List<AudioSource> VoiceList = new List<AudioSource>();
        /// <summary>语音音量</summary>
        private float VoiceValue = 1f;

        /// <summary>音频源对象池</summary>
        private Queue<AudioSource> audioSourcePool = new Queue<AudioSource>();
        #endregion
        
        private AudioSource GetAudioSource(GameObject parent)
        {
            AudioSource source;
            if (audioSourcePool.Count > 0)
            {
                source = audioSourcePool.Dequeue();
                source.gameObject.SetActive(true);
            }
            else
            {
                source = new GameObject("AudioSource").AddComponent<AudioSource>();
            }
            source.transform.SetParent(parent.transform);
            return source;
        }

        private void RecycleAudioSource(AudioSource source)
        {
            if (source != null)
            {
                source.Stop();
                source.clip = null;
                source.gameObject.SetActive(false);
                audioSourcePool.Enqueue(source);
            }
        }

        public MusicManager()
        {
            FrameworkLogger.LogAudio("MusicManager初始化开始");
            
            MonoManager.GetInstance().AddUpdateListener(Update);
            FrameworkLogger.LogAudio("MusicManager初始化完成");
        }

        private void Update()
        {
            for(int i = SFXList.Count - 1; i >= 0; i--)
            {
                if (!SFXList[i].isPlaying)
                {
                    RecycleAudioSource(SFXList[i]);
                    SFXList.RemoveAt(i);
                }
            }

            for (int i = VoiceList.Count - 1; i >= 0; i--)
            {
                if (!VoiceList[i].isPlaying)
                {
                    RecycleAudioSource(VoiceList[i]);
                    VoiceList.RemoveAt(i);
                }
            }
        }

        #region BGM -- 背景音乐
        public void PlayBGM(string name)
        {
            FrameworkLogger.LogAudio($"播放背景音乐: {name}");
            
            if (BGM == null)
            {
                GameObject go = new GameObject();
                go.name = "BGM";
                BGM = go.AddComponent<AudioSource>();
                FrameworkLogger.LogAudio("创建BGM AudioSource");
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(GameConstants.MUSIC_BGM_PATH + name, (clip) =>
            {
                BGM.clip = clip;
                BGM.volume = BGMValue;
                BGM.loop = true;
                BGM.Play();
                FrameworkLogger.LogAudio($"BGM播放开始: {name}");
            });
        }

        public void PauseBGM()
        {
            if (BGM == null) return;
            BGM.Pause();
            FrameworkLogger.LogAudio("BGM暂停");
        }

        public void StopBGM() 
        {
            if (BGM == null) return;
            BGM.Stop();
            FrameworkLogger.LogAudio("BGM停止");
        }

        public void ChangeBGMValue(float value)
        {
            BGMValue = value;
            if (BGM == null) return;
            BGM.volume = BGMValue;
        }
        #endregion

        #region SFX -- 音效
        public void PlaySFX(string name, bool isloop, UnityAction<AudioSource> callback = null)
        {
            if(SFXGO == null)
            {
                SFXGO = new GameObject();
                SFXGO.name = "SFX";
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(GameConstants.MUSIC_SFX_PATH + name, (clip) =>
            {
                AudioSource SFX = GetAudioSource(SFXGO);
                SFX.clip = clip;
                SFX.volume = SFXValue;
                SFX.loop = isloop;
                SFX.Play();
                SFXList.Add(SFX);

                if (callback != null)
                {
                    callback(SFX);
                }
            });
        }

        public void StopSFX(AudioSource SFX)
        {
            if (SFXList.Contains(SFX))
            {
                SFXList.Remove(SFX);
                RecycleAudioSource(SFX);
            }
        }

        public void ChangeSFXValue(float value)
        {
            SFXValue = value;
            for (int i = 0; i < SFXList.Count; i++)
            {
                SFXList[i].volume = SFXValue;
            }
        }
        #endregion

        #region Voice -- 角色音频
        public void PlayVoice(string name, bool isloop, UnityAction<AudioSource> callback = null)
        {
            if (VoiceGO == null)
            {
                VoiceGO = new GameObject();
                VoiceGO.name = "Voice";
            }

            ResourcesManager.GetInstance().LoadAsync<AudioClip>(GameConstants.MUSIC_VOICE_PATH + name, (clip) =>
            {
                AudioSource voice = GetAudioSource(VoiceGO);
                voice.clip = clip;
                voice.volume = VoiceValue;
                voice.loop = isloop;
                voice.Play();
                VoiceList.Add(voice);

                if (callback != null)
                {
                    callback(voice);
                }
            });
        }

        public void StopVoice(AudioSource voice)
        {
            if (VoiceList.Contains(voice))
            {
                VoiceList.Remove(voice);
                RecycleAudioSource(voice);
            }
        }

        public void ChangeVoiceValue(float value)
        {
            VoiceValue = value;
            for (int i = 0; i < VoiceList.Count; i++)
            {
                VoiceList[i].volume = VoiceValue;
            }
        }
        #endregion
        
        /// <summary>
        /// 获取设置键名
        /// </summary>
        protected string GetSettingsKey()
        {
            return "AudioSettings";
        }
        
        /// <summary>
        /// 获取存储类型
        /// </summary>
        protected StorageType GetStorageType()
        {
            return StorageType.PlayerPrefs;
        }
        
        /// <summary>
        /// 设置加载完成回调
        /// </summary>
        protected void OnSettingsLoaded()
        {
            // 应用设置到音频系统
            ApplySettingsToAudio();
            FrameworkLogger.LogAudio("音频设置已应用");
        }
        
        /// <summary>
        /// 设置变更回调
        /// </summary>
        protected void OnSettingsChangedInternal(AudioSettings settings)
        {
            // 设置变更时自动应用
            ApplySettingsToAudio();
            FrameworkLogger.LogAudio("音频设置已更新并应用");
        }
        
        /// <summary>
        /// 应用设置到音频系统
        /// </summary>
        private void ApplySettingsToAudio()
        {
            // 这里可以添加从数据持久化系统加载设置的逻辑
            // 暂时使用默认值，后续可以集成SettingsHelper
            
            FrameworkLogger.LogAudio($"音频设置已应用: BGM={BGMValue}, SFX={SFXValue}, Voice={VoiceValue}");
        }
        
        /// <summary>
        /// 更新音频设置
        /// </summary>
        /// <param name="updateAction">更新委托</param>
        public async System.Threading.Tasks.Task<bool> UpdateAudioSettingsAsync(System.Action<AudioSettings> updateAction)
        {
            try
            {
                // 使用SettingsHelper更新设置
                var settings = new AudioSettings();
                updateAction?.Invoke(settings);
                
                var result = await DataPersistenceAPI.SaveAsync<AudioSettings>(GetSettingsKey(), settings, GetStorageType());
                if (result == DataOperationResult.Success)
                {
                    OnSettingsChangedInternal(settings);
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                FrameworkLogger.Error($"更新音频设置失败: {ex.Message}", LogCategory.Core);
                return false;
            }
        }
        
        /// <summary>
        /// 重置音频设置
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ResetAudioSettingsAsync()
        {
            try
            {
                // 使用SettingsHelper重置设置
                var result = await DataPersistenceAPI.DeleteAsync(GetSettingsKey(), GetStorageType());
                if (result == DataOperationResult.Success)
                {
                    var defaultSettings = new AudioSettings();
                    OnSettingsChangedInternal(defaultSettings);
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                FrameworkLogger.Error($"重置音频设置失败: {ex.Message}", LogCategory.Core);
                return false;
            }
        }
    }
    
    /// <summary>
    /// 音频设置数据类
    /// </summary>
    [System.Serializable]
    public class AudioSettings
    {
        public float BGMVolume = 1.0f;
        public float SFXVolume = 1.0f;
        public float VoiceVolume = 1.0f;
        public bool MuteBGM = false;
        public bool MuteSFX = false;
        public bool MuteVoice = false;
        public bool MuteAll = false;
    }
}
