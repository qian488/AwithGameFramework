using UnityEngine;
using UnityEngine.Events;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 音频管理器接口
    /// 负责BGM、SFX、Voice的播放和管理
    /// </summary>
    public interface IAudioManager
    {
        #region BGM
        void PlayBGM(string name);
        void PauseBGM();
        void StopBGM();
        void ChangeBGMVolume(float value);
        #endregion

        #region SFX
        void PlaySFX(string name, bool loop = false, UnityAction<AudioSource> callback = null);
        void StopSFX(AudioSource source);
        void ChangeSFXVolume(float value);
        #endregion

        #region Voice
        void PlayVoice(string name, bool loop = false, UnityAction<AudioSource> callback = null);
        void StopVoice(AudioSource source);
        void ChangeVoiceVolume(float value);
        #endregion
    }
}
