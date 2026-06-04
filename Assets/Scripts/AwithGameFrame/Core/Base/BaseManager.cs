namespace AwithGameFrame.Core
{
    /// <summary>
    /// 单例管理器基类
    /// 线程安全的双重检查锁定单例模式
    /// </summary>
    /// <typeparam name="T">继承此基类的类型</typeparam>
    public class BaseManager<T> : IModule where T : BaseManager<T>, new()
    {
        private static readonly object _lock = new object();
        private static T instance;
        private ModuleState _state = ModuleState.None;

        /// <summary>
        /// 获取单例实例（双重检查锁定）
        /// </summary>
        public static T GetInstance()
        {
            if (instance == null)
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new T();
                        instance._state = ModuleState.Initialized;
                    }
                }
            }
            return instance;
        }

        /// <summary>
        /// 重置单例（仅用于测试或模块替换）
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
