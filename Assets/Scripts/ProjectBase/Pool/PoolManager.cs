using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// �����ģ��
/// </summary>
public class PoolManager : BaseManager<PoolManager>
{
    public Dictionary<string,PoolData> poolDictionary = new Dictionary<string, PoolData>();

    private GameObject poolGO;
    /// <summary>
    /// ��ȡ���ж���
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public void GetGameObject(string name,UnityAction<GameObject> callback)
    {
        if (poolDictionary.ContainsKey(name) && poolDictionary[name].poolList.Count > 0)
        {
            callback(poolDictionary[name].GetGameObject());
        }
        else
        {
            ResourcesManager.GetInstance().LoadAsync<GameObject>(name, (go) =>
            {
                go.name = name;
                callback(go);
            });
        }
    }
    /// <summary>
    /// ������ѹ�����
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

    public void Clear()
    {
        poolDictionary.Clear();
        poolGO = null;
    }
}
