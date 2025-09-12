using UnityEngine;
using UnityEngine.Events;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 对象池管理器接口
    /// 核心包定义接口，具体实现在基础包中
    /// 提供完整的对象池功能
    /// </summary>
    public interface IPoolManager
    {
        #region 核心功能
        /// <summary>
        /// 获取池中的对象（异步回调方式）
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="callback">获取到对象后的回调</param>
        void GetGameObject(string name, UnityAction<GameObject> callback);

        /// <summary>
        /// 获取池中的对象（同步方式）
        /// 如果池中没有对象，会使用提供的预制体创建新对象
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体（如果池为空则创建新对象）</param>
        /// <returns>获取的对象</returns>
        GameObject GetGameObject(string name, GameObject prefab = null);

        /// <summary>
        /// 将对象放回池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="go">要放回的对象</param>
        void PushGameObject(string name, GameObject go);
        #endregion

        #region 池管理
        /// <summary>
        /// 预加载对象到池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体</param>
        /// <param name="count">预加载数量</param>
        void WarmupPool(string name, GameObject prefab, int count);

        /// <summary>
        /// 检查池中是否有可用对象
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>是否有可用对象</returns>
        bool CheckGameObjectInPool(string name);

        /// <summary>
        /// 检查池是否存在
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池是否存在</returns>
        bool HasPool(string name);

        /// <summary>
        /// 获取池中对象数量
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池中对象数量</returns>
        int GetPoolCount(string name);

        /// <summary>
        /// 获取所有池名称
        /// </summary>
        /// <returns>池名称数组</returns>
        string[] GetPoolNames();
        #endregion

        #region 清理功能
        /// <summary>
        /// 清空指定名称的对象池
        /// </summary>
        /// <param name="name">对象池名称</param>
        void ClearPool(string name);

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        /// <param name="destroyPoolGO">是否同时销毁对象池根节点</param>
        void Clear(bool destroyPoolGO = true);
        #endregion

        #region 统计信息
        /// <summary>
        /// 获取对象池统计信息
        /// </summary>
        /// <returns>统计信息字符串</returns>
        string GetPoolStatistics();

        /// <summary>
        /// 获取指定池的统计信息
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>统计信息字符串</returns>
        string GetPoolStatistics(string name);
        #endregion
    }
}
