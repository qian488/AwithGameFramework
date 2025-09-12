using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Foundation.Pool;

namespace AwithGameFrame.Foundation
{
    public class ResourcesManager : BaseManager<ResourcesManager>
    {
        private IPoolManager poolManager => PoolManagerAPI.GetInstance();

        // 同步加载资源
        public T Load<T>(string name) where T : Object
        {
            LoggingAPI.Info(LogCategory.Resource, $"同步加载资源: {name}");
            
            T res = Resources.Load<T>(name);
            if (res is GameObject)
            {
                var instantiated = GameObject.Instantiate(res);
                LoggingAPI.Info(LogCategory.Resource, $"GameObject实例化完成: {name}");
                return instantiated;
            }
            else
            {
                LoggingAPI.Info(LogCategory.Resource, $"资源加载完成: {name}");
                return res;
            }
        }

        // 异步加载资源
        public void LoadAsync<T>(string name, UnityAction<T> callback, bool usePool = true) where T : Object
        {
            LoggingAPI.Info(LogCategory.Resource, $"异步加载资源: {name}, 使用对象池: {usePool}");
            
            // 如果是GameObject类型且启用对象池，尝试从对象池获取
            if (typeof(T) == typeof(GameObject) && usePool && poolManager != null)
            {
                if (poolManager.CheckGameObjectInPool(name))
                {
                    LoggingAPI.Info(LogCategory.Resource, $"从对象池获取GameObject: {name}");
                    poolManager.GetGameObject(name, (go) =>
                    {
                        callback(go as T);
                    });
                    return;
                }
            }

            // 如果对象池中没有，则从Resources加载
            LoggingAPI.Info(LogCategory.Resource, $"从Resources加载资源: {name}");
            MonoManager.GetInstance().StartCoroutine(ReallyLoadAsync(name, callback));
        }

        private IEnumerator ReallyLoadAsync<T>(string name, UnityAction<T> callback) where T : Object
        {
            ResourceRequest request = Resources.LoadAsync<T>(name);
            yield return request;

            if(request.asset is GameObject)
            {
                var instantiated = GameObject.Instantiate(request.asset) as T;
                LoggingAPI.Info(LogCategory.Resource, $"异步加载GameObject完成: {name}");
                callback(instantiated);
            }
            else
            {
                LoggingAPI.Info(LogCategory.Resource, $"异步加载资源完成: {name}");
                callback(request.asset as T);
            }
        }

        // 添加资源回收方法
        public void Recycle<T>(string name, T obj) where T : Object
        {
            LoggingAPI.Info(LogCategory.Resource, $"回收资源: {name}");
            
            if (obj is GameObject go)
            {
                poolManager.PushGameObject(name, go);
                LoggingAPI.Info(LogCategory.Resource, $"GameObject回收到对象池: {name}");
            }
            else
            {
                Object.Destroy(obj);
                LoggingAPI.Info(LogCategory.Resource, $"资源销毁: {name}");
            }
        }
    }
}
