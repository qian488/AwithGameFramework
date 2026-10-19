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
        private IPoolManager poolManager => PoolManagerAPI.GetInstance();

        public override int Priority => (int)ModulePriority.CoreSystems;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<IResourceManager>(this);
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

            if (typeof(T) == typeof(GameObject) && poolManager != null && poolManager.CheckGameObjectInPool(path))
            {
                poolManager.GetGameObject(path, (go) => callback(go as T));
                return;
            }

            MonoManager.GetInstance().StartCoroutine(ReallyLoadAsync(path, callback));
        }

        private IEnumerator ReallyLoadAsync<T>(string path, UnityAction<T> callback) where T : Object
        {
            ResourceRequest request = Resources.LoadAsync<T>(path);
            yield return request;

            if (request.asset is GameObject)
                callback(GameObject.Instantiate(request.asset) as T);
            else
                callback(request.asset as T);
        }

        public void Recycle<T>(string path, T obj) where T : Object
        {
            if (obj is GameObject go)
            {
                poolManager.PushGameObject(path, go);
            }
            else
            {
                Object.Destroy(obj);
            }
        }
    }
}
