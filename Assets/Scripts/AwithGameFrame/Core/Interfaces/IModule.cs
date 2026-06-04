namespace AwithGameFrame.Core
{
    /// <summary>
    /// 模块优先级 - 值越大越先初始化
    /// </summary>
    public enum ModulePriority
    {
        Critical = 100,       // 日志、配置
        Infrastructure = 80,  // MonoManager、Provider
        CoreSystems = 60,    // 对象池、资源
        Features = 40,       // 音频、输入、UI
        GameSpecific = 20,   // 游戏特定
        UserDefined = 0      // 用户自定义
    }

    /// <summary>
    /// 模块生命周期状态
    /// </summary>
    public enum ModuleState
    {
        None,
        Initializing,
        Initialized,
        Running,
        ShuttingDown,
        Shutdown
    }

    /// <summary>
    /// 框架模块生命周期契约
    /// 所有框架Manager如有需要可选择性实现此接口参与统一生命周期管理
    /// </summary>
    public interface IModule
    {
        string ModuleName { get; }
        int Priority { get; }
        ModuleState State { get; }

        /// <summary>
        /// 一次性初始化 - 设置内部状态、注册服务
        /// </summary>
        void Initialize();

        /// <summary>
        /// 所有模块Initialize完成后调用 - 跨模块关联
        /// </summary>
        void PostInitialize();

        /// <summary>
        /// 销毁清理
        /// </summary>
        void Shutdown();
    }
}
