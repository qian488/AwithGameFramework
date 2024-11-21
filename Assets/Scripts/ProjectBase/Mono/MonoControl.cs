using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MonoControl : MonoBehaviour
{
    private event UnityAction updateEvent;

    void Start()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    void Update()
    {
        if (updateEvent != null)
        {
            updateEvent();
        }
    }

    /// <summary>
    /// 给外部提供 添加帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void AddUpdateListener(UnityAction function)
    {
        updateEvent += function;
    }

    /// <summary>
    /// 给外部提供 移除帧更新事件
    /// </summary>
    /// <param name="function"></param>
    public void RemoveUpdateListener(UnityAction function) 
    { 
        updateEvent -= function; 
    }
}
