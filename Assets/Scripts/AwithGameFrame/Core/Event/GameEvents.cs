using System;

namespace AwithGameFrame.Core.Event
{
    /// <summary>
    /// 类型安全的事件ID
    /// 可隐式转换为string以兼容旧的字符串事件API
    /// </summary>
    public readonly struct EventId : IEquatable<EventId>
    {
        public string Value { get; }

        public EventId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static implicit operator string(EventId id) => id.Value;

        public bool Equals(EventId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value;

        public static bool operator ==(EventId a, EventId b) => a.Equals(b);
        public static bool operator !=(EventId a, EventId b) => !a.Equals(b);
    }

    /// <summary>
    /// 框架预定义事件ID
    /// 新代码使用这些常量获得编译时检查
    /// 旧代码的字符串字面量仍然可用
    /// </summary>
    public static class FrameworkEvents
    {
        public static readonly EventId ConfigChanged = new EventId("ConfigChanged");
        public static readonly EventId ConfigReloaded = new EventId("ConfigReloaded");
        public static readonly EventId SceneLoading = new EventId("Loading");
        public static readonly EventId SceneLoaded = new EventId("SceneLoaded");
        public static readonly EventId KeyDown = new EventId("KeyDown");
        public static readonly EventId KeyUp = new EventId("KeyUp");
        public static readonly EventId FrameworkInitialized = new EventId("FrameworkInitialized");
        public static readonly EventId FrameworkShutdown = new EventId("FrameworkShutdown");
        public static readonly EventId ApplicationPause = new EventId("ApplicationPause");
        public static readonly EventId ApplicationResume = new EventId("ApplicationResume");
        public static readonly EventId ModuleInitialized = new EventId("ModuleInitialized");
        public static readonly EventId ModuleShutdown = new EventId("ModuleShutdown");
    }
}
