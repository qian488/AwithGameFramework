using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Foundation.Pool;

namespace AwithGameFrame.Foundation
{
    public class ResourcesManager : BaseManager<ResourcesManager>, IResourceManager
    {
        private IPoolManager poolManager => PoolManagerAPI.IsInitialized() ? PoolManagerAPI.GetInstance() : null;
        private int loadGeneration;

        public override int Priority => (int)ModulePriority.CoreSystems;

        public override void Initialize()
        {
            base.Initialize();
            loadGeneration++;
            ServiceLocator.Register<IResourceManager>(this);
        }

        public override void Shutdown()
        {
            loadGeneration++;
            ServiceLocator.Unregister<IResourceManager>();
            base.Shutdown();
        }

        public T Load<T>(string path) where T : Object
        {
            LoggingAPI.Info(LogCategory.Resource, $"同步加载资源: {path}");
            T res = Resources.Load<T>(path);
            if (res is GameObject)
                return GameObject.Instantiate(res);
            return res;
        }

        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object
        {
            LoggingAPI.Info(LogCategory.Resource, $"异步加载资源: {path}");

            if (typeof(T) == typeof(GameObject) && poolManager != null)
            {
                poolManager.GetGameObject(path, (go) => callback?.Invoke(go as T));
                return;
            }

            MonoManager.GetInstance().StartCoroutine(ReallyLoadAsync(path, callback, loadGeneration));
        }

        private IEnumerator ReallyLoadAsync<T>(string path, UnityAction<T> callback, int generation) where T : Object
        {
            ResourceRequest request = Resources.LoadAsync<T>(path);
            yield return request;
            if (generation != loadGeneration) { callback?.Invoke(null); yield break; }

            if (request.asset is GameObject)
                callback?.Invoke(GameObject.Instantiate(request.asset) as T);
            else
                callback?.Invoke(request.asset as T);
        }

        public void Recycle<T>(string path, T obj) where T : Object
        {
            if (obj is GameObject go)
            {
                if (poolManager != null) poolManager.PushGameObject(path, go);
                else Object.Destroy(go);
            }
            else
            {
                Object.Destroy(obj);
            }
        }
    }
}
