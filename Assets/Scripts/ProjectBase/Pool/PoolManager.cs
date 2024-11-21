using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 缓存池模块
/// </summary>
public class PoolManager : BaseManager<PoolManager>
{
    public Dictionary<string,PoolData> poolDictionary = new Dictionary<string, PoolData>();

    private GameObject poolGO;
    /// <summary>
    /// 获取池中对象
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public void GetGameObject(string name,UnityAction<GameObject> callback)
    {
        if (CheckGameObjectInPool(name))
        {
            callback(poolDictionary[name].GetGameObject());
        }
        else
        {
            // 如果池中没有，让ResourcesManager去加载
            ResourcesManager.GetInstance().LoadAsync<GameObject>(name, (go) =>
            {
                go.name = name;
                callback(go);
            }, false);  // usePool设为false 池中没有，故等价于不使用对象池
        }
    }

    /// <summary>
    /// 将对象压入池中
    /// </summary>
    /// <param name="name"></param>
    /// <param name="go"></param>
    public void PushGameObject(string name,GameObject go)
    {
        if(poolGO == null) poolGO = new GameObject("Pool");

        if (poolDictionary.ContainsKey(name))
        {
            poolDictionary[name].PushGameObject(go);
        }
        else
        {
            poolDictionary.Add(name, new PoolData(go,poolGO));
        }
    }

    /// <summary>
    /// 清空对象池
    /// </summary>
    /// <param name="destroyPoolGO">是否同时销毁对象池根节点</param>
    public void Clear(bool destroyPoolGO = true)
    {
        // 遍历字典中的所有PoolData
        foreach (var poolData in poolDictionary.Values)
        {
            // 销毁池中的所有游戏对象
            foreach (var go in poolData.poolList)
            {
                if(go != null)
                    GameObject.Destroy(go);
            }
            // 销毁父节点游戏对象
            if(poolData.fatherGameObject != null)
                GameObject.Destroy(poolData.fatherGameObject);
        }

        // 清空字典
        poolDictionary.Clear();

        // 根据参数决定是否销毁对象池的根节点
        if (destroyPoolGO && poolGO != null)
        {
            GameObject.Destroy(poolGO);
            poolGO = null;
        }
    }

    /// <summary>
    /// 清除指定名称的对象池
    /// </summary>
    /// <param name="name">对象池名称</param>
    public void ClearPool(string name)
    {
        if (poolDictionary.ContainsKey(name))
        {
            // 销毁池中的所有游戏对象
            foreach (var go in poolDictionary[name].poolList)
            {
                if(go != null)
                    GameObject.Destroy(go);
            }
            // 销毁父节点游戏对象
            if(poolDictionary[name].fatherGameObject != null)
                GameObject.Destroy(poolDictionary[name].fatherGameObject);
            
            // 从字典中移除
            poolDictionary.Remove(name);
        }
    }

    public bool CheckGameObjectInPool(string name)
    {
        return poolDictionary.ContainsKey(name) && poolDictionary[name].poolList.Count > 0;
    }
}
