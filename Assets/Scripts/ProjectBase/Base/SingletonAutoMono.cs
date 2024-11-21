using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 继承了mono的单例模式对象 但自动添加脚本
public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T GetInstance()
    {
        if (instance == null)
        {
            GameObject go = new GameObject();
            go.name = typeof(T).ToString();
            DontDestroyOnLoad(go);
            instance = go.AddComponent<T>();
        }
        return instance;
    }

}