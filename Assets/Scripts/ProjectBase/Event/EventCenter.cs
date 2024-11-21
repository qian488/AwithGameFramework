using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#region �¼���Ϣ��װ ����װ����
public interface IEventInfo { }
// ���ݷ��Ͳ���
public class EventInfo<T> : IEventInfo
{
    public UnityAction<T> actions;

    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }

}
// �����ݲ���
public class EventInfo : IEventInfo
{
    public UnityAction actions;

    public EventInfo(UnityAction action)
    {
        actions += action;
    }

}
#endregion

/// <summary>
/// �¼����� ����ģʽ����
/// �۲������ģʽ
/// </summary>
public class EventCenter : BaseManager<EventCenter>
{
    // key �¼�����
    // value �����¼���ί�к���
    // �û���װ���� ��Ҫʹ�÷��� ��һ���սӿ� ��װһ������ ԭ�򣺱���ԭ����object���Ͳ�װ��
    private Dictionary<string, IEventInfo> evenDictionary = new Dictionary<string, IEventInfo>();
    #region �¼�����
    /// <summary>
    /// ����¼�����
    /// </summary>
    /// <param name="eventName">�¼�����</param>
    /// <param name="action">�����¼���ί�к���</param>
    public void AddEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo<T>(action));
        }
    }

    public void AddEventListener(string eventName, UnityAction action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo(action));
        }
    }
    #endregion

    #region �Ƴ��¼�����
    /// <summary>
    /// �Ƴ��¼�����
    /// </summary>
    /// <param name="eventName">�¼�����</param>
    /// <param name="action">��Ӧ֮ǰ��ӵ�ί�к���</param>
    public void RemoveEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions -= action;
        }
    }
    public void RemoveEventListener(string eventName, UnityAction action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions -= action;
        }
    }
    #endregion

    #region �¼�����
    /// <summary>
    /// �¼�����
    /// </summary>
    /// <param name="eventName">�ĸ����ֵ��¼�����</param>
    /// <param name="info">ί�к�����������Ϣ</param>
    public void EventTrigger<T>(string eventName,T info)
    {
        if(evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions?.Invoke(info);
        }

    }
    public void EventTrigger(string eventName)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo).actions?.Invoke();
        }

    }
    #endregion

    public void Clear()
    {
        evenDictionary.Clear();
    }
}
