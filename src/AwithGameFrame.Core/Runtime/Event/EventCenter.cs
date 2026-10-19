using System.Collections.Generic;
using System;
using AwithGameFrame.Core.Event;
using AwithGameFrame.Core.Logging;


namespace AwithGameFrame.Core
{
    #region 事件信息封装 基类装子类
    public interface IEventInfo { }
    
    // 传递一个泛型参数
    public class EventInfo<T> : IEventInfo
    {
        public Action<T> actions;

        public EventInfo(Action<T> action)
        {
            actions += action;
        }
    }
    
    public class EventInfo<T0,T1> : IEventInfo
    {
        public Action<T0,T1> actions;

        public EventInfo(Action<T0,T1> action)
        {
            actions += action;
        }
    }
    
    // 传递三个泛型参数
    public class EventInfo<T0,T1,T2> : IEventInfo
    {
        public Action<T0,T1,T2> actions;

        public EventInfo(Action<T0,T1,T2> action)
        {
            actions += action;
        }
    }
    
    // 传递四个泛型参数
    public class EventInfo<T0,T1,T2,T3> : IEventInfo
    {
        public Action<T0,T1,T2,T3> actions;

        public EventInfo(Action<T0,T1,T2,T3> action)
        {
            actions += action;
        }
    }
    
    // 不传递参数
    public class EventInfo : IEventInfo
    {
        public Action actions;

        public EventInfo(Action action)
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

        private TEventInfo GetEventInfo<TEventInfo>(string eventName) where TEventInfo : class, IEventInfo
        {
            var registered = evenDictionary[eventName];
            if (registered is TEventInfo matching) return matching;

            throw new InvalidOperationException(
                $"Event '{eventName}' was registered with signature {registered.GetType()}, " +
                $"but the operation requested {typeof(TEventInfo)}. Use one signature per event ID.");
        }
        
        #region 事件监听
        /// <summary>
        /// 添加事件监听
        /// </summary>
        /// <param name="eventName">事件名字</param>
        /// <param name="action">处理事件的委托函数</param>
        public void AddEventListener<T>(string eventName, Action<T> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T>>(eventName).actions += action;
            }
            else
            {
                evenDictionary.Add(eventName, new EventInfo<T>(action));
            }
            
            LoggingAPI.Info(LogCategory.Event, $"添加事件监听: {eventName}");
        }

        public void AddEventListener<T0,T1>(string eventName, Action<T0,T1> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1>>(eventName).actions += action;
            }
            else
            {
                evenDictionary.Add(eventName, new EventInfo<T0,T1>(action));
            }
            
            LoggingAPI.Info(LogCategory.Event, $"添加事件监听: {eventName}");
        }

        public void AddEventListener<T0,T1,T2>(string eventName, Action<T0,T1,T2> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2>>(eventName).actions += action;
            }
            else
            {
                evenDictionary.Add(eventName, new EventInfo<T0,T1,T2>(action));
            }
            
            LoggingAPI.Info(LogCategory.Event, $"添加事件监听: {eventName}");
        }

        public void AddEventListener<T0,T1,T2,T3>(string eventName, Action<T0,T1,T2,T3> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2,T3>>(eventName).actions += action;
            }
            else
            {
                evenDictionary.Add(eventName, new EventInfo<T0,T1,T2,T3>(action));
            }
            
            LoggingAPI.Info(LogCategory.Event, $"添加事件监听: {eventName}");
        }

        public void AddEventListener(string eventName, Action action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo>(eventName).actions += action;
            }
            else
            {
                evenDictionary.Add(eventName, new EventInfo(action));
            }
            
            LoggingAPI.Info(LogCategory.Event, $"添加事件监听: {eventName}");
        }
        #endregion

        #region 移除事件监听
        /// <summary>
        /// 移除事件监听
        /// </summary>
        /// <param name="eventName">事件名字</param>
        /// <param name="action">对应之前添加的委托函数</param>
        public void RemoveEventListener<T>(string eventName, Action<T> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T>>(eventName).actions -= action;
                LoggingAPI.Info(LogCategory.Event, $"移除事件监听: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试移除不存在的事件监听: {eventName}");
            }
        }
        
        public void RemoveEventListener<T0,T1>(string eventName, Action<T0,T1> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1>>(eventName).actions -= action;
                LoggingAPI.Info(LogCategory.Event, $"移除事件监听: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试移除不存在的事件监听: {eventName}");
            }
        }
        
        public void RemoveEventListener<T0,T1,T2>(string eventName, Action<T0,T1,T2> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2>>(eventName).actions -= action;
                LoggingAPI.Info(LogCategory.Event, $"移除事件监听: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试移除不存在的事件监听: {eventName}");
            }
        }

        public void RemoveEventListener<T0,T1,T2,T3>(string eventName, Action<T0,T1,T2,T3> action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2,T3>>(eventName).actions -= action;
                LoggingAPI.Info(LogCategory.Event, $"移除事件监听: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试移除不存在的事件监听: {eventName}");
            }
        }
        
        public void RemoveEventListener(string eventName, Action action)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo>(eventName).actions -= action;
                LoggingAPI.Info(LogCategory.Event, $"移除事件监听: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试移除不存在的事件监听: {eventName}");
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
                GetEventInfo<EventInfo<T>>(eventName).actions?.Invoke(info);
                LoggingAPI.Info(LogCategory.Event, $"触发事件: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试触发不存在的事件: {eventName}");
            }
        }
        
        public void EventTrigger<T0,T1>(string eventName,T0 info0,T1 info1)
        {
            if(evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1>>(eventName).actions?.Invoke(info0,info1);
                LoggingAPI.Info(LogCategory.Event, $"触发事件: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试触发不存在的事件: {eventName}");
            }
        }
        
        public void EventTrigger<T0,T1,T2>(string eventName,T0 info0,T1 info1,T2 info2)
        {
            if(evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2>>(eventName).actions?.Invoke(info0,info1,info2);
                LoggingAPI.Info(LogCategory.Event, $"触发事件: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试触发不存在的事件: {eventName}");
            }
        }
        
        public void EventTrigger<T0,T1,T2,T3>(string eventName,T0 info0,T1 info1,T2 info2,T3 info3)
        {
            if(evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo<T0,T1,T2,T3>>(eventName).actions?.Invoke(info0,info1,info2,info3);
                LoggingAPI.Info(LogCategory.Event, $"触发事件: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试触发不存在的事件: {eventName}");
            }
        }
        
        public void EventTrigger(string eventName)
        {
            if (evenDictionary.ContainsKey(eventName))
            {
                GetEventInfo<EventInfo>(eventName).actions?.Invoke();
                LoggingAPI.Info(LogCategory.Event, $"触发事件: {eventName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试触发不存在的事件: {eventName}");
            }
        }
        #endregion

        public void Clear()
        {
            evenDictionary.Clear();
            LoggingAPI.Info(LogCategory.Event, "清空所有事件监听");
        }

        #region EventId 类型安全重载

        public void AddEventListener<T>(EventId eventId, Action<T> action) => AddEventListener<T>((string)eventId, action);
        public void AddEventListener<T0, T1>(EventId eventId, Action<T0, T1> action) => AddEventListener<T0, T1>((string)eventId, action);
        public void AddEventListener(EventId eventId, Action action) => AddEventListener((string)eventId, action);

        public void RemoveEventListener<T>(EventId eventId, Action<T> action) => RemoveEventListener<T>((string)eventId, action);
        public void RemoveEventListener(EventId eventId, Action action) => RemoveEventListener((string)eventId, action);

        public void EventTrigger<T>(EventId eventId, T info) => EventTrigger<T>((string)eventId, info);
        public void EventTrigger(EventId eventId) => EventTrigger((string)eventId);

        #endregion
    }
}
