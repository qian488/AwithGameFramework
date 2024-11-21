using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#region 事件信息封装 基类装子类
public interface IEventInfo { }
// 传递一个泛型参数
public class EventInfo<T> : IEventInfo
{
    public UnityAction<T> actions;

    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }

}
public class EventInfo<T0,T1> : IEventInfo
{
    public UnityAction<T0,T1> actions;

    public EventInfo(UnityAction<T0,T1> action)
    {
        actions += action;
    }

}
// 不传递参数
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
/// 事件中心 单例模式对象
/// 观察者设计模式
/// </summary>
public class EventCenter : BaseManager<EventCenter>
{
   // key 事件名字
    // value 监听事件的委托函数
    // 用基类装子类 想要使用泛型 开一个空接口 封装一个泛型 原因：避免原来的object类型拆装箱
    private Dictionary<string, IEventInfo> evenDictionary = new Dictionary<string, IEventInfo>();
    #region 事件监听
    /// <summary>
    /// 添加事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">处理事件的委托函数</param>
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

    public void AddEventListener<T0,T1>(string eventName, UnityAction<T0,T1> action){
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T0,T1>).actions += action;
        }
        else
        {
            evenDictionary.Add(eventName, new EventInfo<T0,T1>(action));
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

    #region 移除事件监听
    /// <summary>
    /// 移除事件监听
    /// </summary>
    /// <param name="eventName">事件名字</param>
    /// <param name="action">对应之前添加的委托函数</param>
    public void RemoveEventListener<T>(string eventName, UnityAction<T> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions -= action;
        }
    }
    public void RemoveEventListener<T0,T1>(string eventName, UnityAction<T0,T1> action)
    {
        if (evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T0,T1>).actions -= action;
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

    #region 事件触发
    /// <summary>
    /// 事件触发
    /// </summary>
    /// <param name="eventName">哪个名字的事件触发</param>
    /// <param name="info">委托函数附带的信息</param>
    public void EventTrigger<T>(string eventName,T info)
    {
        if(evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T>).actions?.Invoke(info);
        }

    }
    public void EventTrigger<T0,T1>(string eventName,T0 info0,T1 info1)
    {
        if(evenDictionary.ContainsKey(eventName))
        {
            (evenDictionary[eventName] as EventInfo<T0,T1>).actions?.Invoke(info0,info1);
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
