using UnityEngine;
using UnityEngine.Events;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 资源管理器接口
    /// 负责同步/异步资源加载和回收
    /// </summary>
    public interface IResourceManager
    {
        /// <summary>
        /// 同步加载资源
        /// </summary>
        T Load<T>(string path) where T : Object;

        /// <summary>
        /// 异步加载资源
        /// </summary>
        void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object;

        /// <summary>
        /// 回收资源（GameObject回池，其他Destroy）
        /// </summary>
        void Recycle<T>(string path, T obj) where T : Object;
    }
}
