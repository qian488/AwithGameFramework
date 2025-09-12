using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Foundation.Pool
{
    /// <summary>
    /// 对象池初始化器
    /// 在基础包中初始化对象池系统
    /// </summary>
    public static class PoolInitializer
    {
        /// <summary>
        /// 初始化对象池系统
        /// </summary>
        public static void Initialize()
        {
            // 创建对象池管理器实例
            var poolManager = PoolManager.GetInstance();
            
            // 初始化对象池管理器
            poolManager.Initialize();
            
            // 注册到API门面
            PoolManagerAPI.SetInstance(poolManager);
            
            LoggingAPI.Info(LogCategory.Pool, "PoolInitializer: 对象池系统已初始化");
        }

        /// <summary>
        /// 检查对象池系统是否已初始化
        /// </summary>
        /// <returns>是否已初始化</returns>
        public static bool IsInitialized()
        {
            return PoolManagerAPI.IsInitialized();
        }
    }
}
