using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ����Mono����
/// �����Է�װһ����ʱ��
/// </summary>
public class MonoManager : BaseManager<MonoManager>
{
    private MonoControl controller;

    public MonoManager()
    {
        // ������֤��MonoControl�����Ψһ��
        GameObject go = new GameObject("MonoController");
        controller = go.AddComponent<MonoControl>();
    }

    /// <summary>
    /// ���ⲿ�ṩ ���֡�����¼�
    /// </summary>
    /// <param name="function"></param>
    public void AddUpdateListener(UnityAction function)
    {
        controller.AddUpdateListener(function);
    }

    /// <summary>
    /// ���ⲿ�ṩ �Ƴ�֡�����¼�
    /// </summary>
    /// <param name="function"></param>
    public void RemoveUpdateListener(UnityAction function)
    {
        controller.RemoveUpdateListener(function);
    }

    #region Э�����
    public Coroutine StartCoroutine(string methodName)
    {
        return controller.StartCoroutine(methodName);
    }

    public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
    {
        return controller.StartCoroutine(methodName, value);
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    {
        return controller.StartCoroutine(routine);
    }

    public void StopCoroutine(IEnumerator routine)
    {
        controller.StopCoroutine(routine);
    }

    public void StopCoroutine(Coroutine routine)
    {
        controller.StopCoroutine(routine);
    }
    #endregion
}
