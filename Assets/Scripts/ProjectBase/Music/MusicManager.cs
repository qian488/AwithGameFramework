using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MusicManager : BaseManager<MusicManager>
{
    private AudioSource BGM = null;
    private float BGMValue = 1f;

    private GameObject SFXGO = null;
    private List<AudioSource> SFXList = new List<AudioSource>();
    private float SFXValue = 1f;

    private GameObject VoiceGO = null;
    private List<AudioSource> VoiceList = new List<AudioSource>();
    private float VoiceValue = 1f;

    // 添加音频源对象池
    private Queue<AudioSource> audioSourcePool = new Queue<AudioSource>();
    
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
        MonoManager.GetInstance().AddUpdateListener(Update);
    }

    private void Update()
    {
        for(int i = SFXList.Count - 1; i >= 0; i--)
        {
            if (!SFXList[i].isPlaying)
            {
                GameObject.Destroy(SFXList[i]);
                SFXList.RemoveAt(i);
            }
        }

        for (int i = VoiceList.Count - 1; i >= 0; i--)
        {
            if (!VoiceList[i].isPlaying)
            {
                GameObject.Destroy(VoiceList[i]);
                VoiceList.RemoveAt(i);
            }
        }
    }

    #region BGM -- 背景音乐
    public void PlayBGM(string name)
    {
        if (BGM == null)
        {
            GameObject go = new GameObject();
            go.name = "BGM";
            BGM = go.AddComponent<AudioSource>();
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/BGM/" + name, (clip) =>
        {
            BGM.clip = clip;
            BGM.volume = BGMValue;
            BGM.loop = true;
            BGM.Play();
        });
    }

    public void PauseBGM()
    {
        if (BGM == null) return;
        BGM.Pause();
    }

    public void StopBGM() 
    {
        if (BGM == null) return;
        BGM.Stop();
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
            SFXGO.name = name;
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/SFX/" + name, (clip) =>
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
            SFX.Stop();
            GameObject.Destroy(SFX);
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
            VoiceGO.name = name;
        }

        ResourcesManager.GetInstance().LoadAsync<AudioClip>("Music/Voice/" + name, (clip) =>
        {
            AudioSource voice = VoiceGO.AddComponent<AudioSource>();
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
            voice.Stop();
            GameObject.Destroy(voice);
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

}
