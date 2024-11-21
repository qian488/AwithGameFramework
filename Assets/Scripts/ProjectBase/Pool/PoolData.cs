using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// fatherGameObject 是这一类对象的容器 父节点
/// poolList 是池中的对象容器
/// </summary>
public class PoolData
{ 
    public GameObject fatherGameObject;
    public List<GameObject> poolList;

    public PoolData(GameObject go, GameObject poolGO)
    {
        fatherGameObject = new GameObject(go.name);
        fatherGameObject.transform.parent = poolGO.transform;
        poolList = new List<GameObject>() { };
        PushGameObject(go);
    }

    public void PushGameObject(GameObject go)
    {
        go.SetActive(false);
        poolList.Add(go);
        go.transform.parent = fatherGameObject.transform;
    }

    // 默认拿取第一个
    public GameObject GetGameObject()
    {
        GameObject go = poolList[0];
        poolList.RemoveAt(0);
        go.SetActive(true);
        go.transform.parent = null;
        return go;
    }
}
