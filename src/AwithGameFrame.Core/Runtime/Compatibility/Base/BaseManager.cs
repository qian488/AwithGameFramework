namespace AwithGameFrame.Core
{
    /// <summary>
    /// 单例管理器基类
    /// 创建、获取和重置共用一把锁。
    /// </summary>
    /// <typeparam name="T">继承此基类的类型</typeparam>
    public class BaseManager<T> : IModule where T : BaseManager<T>, new()
    {
        private static readonly object _lock = new object();
        private static T instance;
        private ModuleState _state = ModuleState.None;

        /// <summary>
        /// 获取已完成构造及初始状态设置的单例实例。
        /// 返回后，调用方仍须协调实例使用与 ResetInstance 的生命周期。
        /// </summary>
        public static T GetInstance()
        {
            lock (_lock)
            {
                if (instance == null)
                {
                    var created = new T();
                    created._state = ModuleState.Initialized;
                    instance = created;
                }
                return instance;
            }
        }

        /// <summary>
        /// 重置单例（仅用于测试或模块替换）。
        /// 等待正在进行的获取，但无法保证调用方此前取得的实例仍有效。
        /// </summary>
        public static void ResetInstance()
        {
            lock (_lock)
            {
                if (instance != null)
                {
                    instance.Shutdown();
                }
                instance = null;
            }
        }

        #region IModule 默认实现

        public string ModuleName => typeof(T).Name;
        public virtual int Priority => (int)ModulePriority.UserDefined;
        public ModuleState State => _state;

        public virtual void Initialize()
        {
            _state = ModuleState.Initialized;
        }

        public virtual void PostInitialize()
        {
            _state = ModuleState.Running;
        }

        public virtual void Shutdown()
        {
            _state = ModuleState.Shutdown;
        }

        #endregion
    }
}
