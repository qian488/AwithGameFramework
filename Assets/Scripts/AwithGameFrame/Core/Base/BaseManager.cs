namespace AwithGameFrame.Core
{
    public class BaseManager<T> where T : new()
    {
        private static readonly object _lock = new object();
        private static T instance;
        
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
