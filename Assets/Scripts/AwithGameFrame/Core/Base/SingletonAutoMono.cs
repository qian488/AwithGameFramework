using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 继承MonoBehaviour的自动单例模式基类
    /// 自动创建GameObject并添加组件，确保唯一性
    /// </summary>
    /// <typeparam name="T">继承此基类的MonoBehaviour类型</typeparam>
    public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        
        /// <summary>
        /// 获取单例实例
        /// 如果实例不存在，会自动创建GameObject并添加组件
        /// </summary>
        /// <returns>单例实例</returns>
        public static T GetInstance()
        {
            if (instance == null)
            {
                GameObject go = new GameObject();
                go.name = typeof(T).ToString();
                DontDestroyOnLoad(go);
                instance = go.AddComponent<T>();
            }
            return instance;
        }
    }
}
