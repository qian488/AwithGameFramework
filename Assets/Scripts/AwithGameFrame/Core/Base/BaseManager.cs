namespace AwithGameFrame.Core
{
    /// <summary>
    /// 单例管理器基类
    /// 提供线程安全的单例模式实现
    /// </summary>
    /// <typeparam name="T">继承此基类的类型</typeparam>
    public class BaseManager<T> where T : new()
    {
        private static readonly object _lock = new object();
        private static T instance;
        
        /// <summary>
        /// 获取单例实例
        /// 使用双重检查锁定模式确保线程安全
        /// </summary>
        /// <returns>单例实例</returns>
        public static T GetInstance()
        {
            if (instance == null)
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new T();
                    }
                }
            }
            return instance; 
        }
    }
}
