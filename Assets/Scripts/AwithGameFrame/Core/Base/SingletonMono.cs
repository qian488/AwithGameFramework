using UnityEngine;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 继承MonoBehaviour的单例模式基类
    /// 需要手动保证唯一性，不能多次挂载到同一对象上
    /// </summary>
    /// <typeparam name="T">继承此基类的MonoBehaviour类型</typeparam>
    public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        
        /// <summary>
        /// 获取单例实例
        /// 注意：继承了MonoBehaviour的脚本不能直接new，需要通过拖拽或AddComponent添加
        /// </summary>
        /// <returns>单例实例</returns>
        public static T GetInstance()
        {
            return instance; 
        }

        /// <summary>
        /// 子类需要重写此方法
        /// 在Awake中设置instance = this as T
        /// </summary>
        protected virtual void Awake()
        {
            instance = this as T;
        }
    }
}
