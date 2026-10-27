using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using AwithGameFrame.Core.DI;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 场景切换管理器
    /// </summary>
    public class ScenesManager : BaseManager<ScenesManager>, ISceneManager
    {
        public override int Priority => (int)ModulePriority.Infrastructure;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<ISceneManager>(this);
        }

        public void LoadScene(string sceneName, UnityAction callback = null)
        {
            SceneManager.LoadScene(sceneName);
            callback?.Invoke();
        }

        public void LoadSceneAsync(string sceneName, UnityAction callback = null)
        {
            MonoManager.GetInstance().StartCoroutine(ReallyLoadSceneAsync(sceneName, callback));
        }

        private IEnumerator ReallyLoadSceneAsync(string sceneName, UnityAction callback)
        {
            AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName);
            while (!ao.isDone)
            {
                EventCenter.GetInstance().EventTrigger("Loading", ao.progress);
                yield return null;
            }
            callback?.Invoke();
        }
    }
}
