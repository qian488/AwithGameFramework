using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景切换模块
/// </summary>
public class MyScenesManager : BaseManager<MyScenesManager>
{
    // 同步加载场景
    public void LoadScene(string sceneName,UnityAction function)
    {
        SceneManager.LoadScene(sceneName);
        function();
    }

    // 异步加载场景
    public void LoadSceneAsync(string sceneName, UnityAction function)
    {
        MonoManager.GetInstance().StartCoroutine(ReallyLoadSceneAsync(sceneName, function));
    }

    private IEnumerator ReallyLoadSceneAsync(string sceneName,UnityAction function)
    {
        AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName);
        while(ao.isDone)
        {
            EventCenter.GetInstance().EventTrigger("Loading",ao.progress);
            yield return ao.progress;
        }
        function();
    }
}
