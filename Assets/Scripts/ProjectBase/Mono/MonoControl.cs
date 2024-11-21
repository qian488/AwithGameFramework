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
    /// ���ⲿ�ṩ ���֡�����¼�
    /// </summary>
    /// <param name="function"></param>
    public void AddUpdateListener(UnityAction function)
    {
        updateEvent += function;
    }

    /// <summary>
    /// ���ⲿ�ṩ �Ƴ�֡�����¼�
    /// </summary>
    /// <param name="function"></param>
    public void RemoveUpdateListener(UnityAction function) 
    { 
        updateEvent -= function; 
    }
}
