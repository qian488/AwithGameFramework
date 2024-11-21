using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : BaseManager<ResourcesManager>
{
    private PoolManager poolManager => PoolManager.GetInstance();

    // 同步加载资源
    public T Load<T>(string name) where T : Object
    {
        T res = Resources.Load<T>(name);
        if (res is GameObject)
        {
            return GameObject.Instantiate(res);
        }
        else
        {
            return res;
        }
    }

    // 异步加载资源
    public void LoadAsync<T>(string name, UnityAction<T> callback, bool usePool = true) where T : Object
    {
        // 如果是GameObject类型且启用对象池，尝试从对象池获取
        if (typeof(T) == typeof(GameObject) && usePool)
        {
            if (poolManager.CheckGameObjectInPool(name))
            {
                poolManager.GetGameObject(name, (go) =>
                {
                    callback(go as T);
                });
                return;
            }
        }

        // 如果对象池中没有，则从Resources加载
        MonoManager.GetInstance().StartCoroutine(ReallyLoadAsync(name, callback));
    }

    private IEnumerator ReallyLoadAsync<T>(string name, UnityAction<T> callback) where T : Object
    {
        ResourceRequest request = Resources.LoadAsync<T>(name);
        yield return request;

        if(request.asset is GameObject)
        {
            callback(GameObject.Instantiate(request.asset) as T);
        }
        else
        {
            callback(request.asset as T);
        }
    }

    // 添加资源回收方法
    public void Recycle<T>(string name, T obj) where T : Object
    {
        if (obj is GameObject go)
        {
            poolManager.PushGameObject(name, go);
        }
        else
        {
            Object.Destroy(obj);
        }
    }
}
