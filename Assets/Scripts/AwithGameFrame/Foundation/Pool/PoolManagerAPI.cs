using System;
using UnityEngine;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Logging;


namespace AwithGameFrame.Foundation.Pool
{
    /// <summary>
    /// 对象池管理器API
    /// 基础包提供静态门面接口
    /// </summary>
    public static class PoolManagerAPI
    {
        /// <summary>
        /// 对象池管理器实例
        /// </summary>
        private static IPoolManager instance;

        /// <summary>
        /// 设置对象池管理器实例
        /// </summary>
        /// <param name="poolManager">对象池管理器实例</param>
        public static void SetInstance(IPoolManager poolManager)
        {
            instance = poolManager;
        }

        /// <summary>
        /// 获取对象池管理器实例
        /// </summary>
        /// <returns>对象池管理器实例</returns>
        public static IPoolManager GetInstance()
        {
            if (instance == null)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManagerAPI: 对象池管理器未初始化，请先调用SetInstance()");
                return null;
            }
            return instance;
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        /// <returns>是否已初始化</returns>
        public static bool IsInitialized()
        {
            return instance != null;
        }

        /// <summary>
        /// 预定义的对象池类型常量
        /// </summary>
        public static class PoolTypes
        {
            public const string UI = "UI";
            public const string Effect = "Effect";
            public const string Bullet = "Bullet";
            public const string Enemy = "Enemy";
            public const string Item = "Item";
            public const string Projectile = "Projectile";
            public const string Particle = "Particle";
            public const string Decoration = "Decoration";
        }

        #region 公共方法（委托给实例）
        /// <summary>
        /// 获取池中对象（异步回调方式）
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="callback">获取到对象后的回调</param>
        public static void GetGameObject(string name, UnityAction<GameObject> callback)
        {
            var poolManager = GetInstance();
            poolManager?.GetGameObject(name, callback);
        }

        /// <summary>
        /// 获取池中的对象（同步方式）
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体（如果池为空则创建新对象）</param>
        /// <returns>获取的对象</returns>
        public static GameObject GetGameObject(string name, GameObject prefab = null)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetGameObject(name, prefab);
            }
            return null;
        }

        /// <summary>
        /// 将对象放回池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="go">要放回的对象</param>
        public static void PushGameObject(string name, GameObject go)
        {
            var poolManager = GetInstance();
            poolManager?.PushGameObject(name, go);
        }

        /// <summary>
        /// 预加载对象到池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体</param>
        /// <param name="count">预加载数量</param>
        public static void WarmupPool(string name, GameObject prefab, int count)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.WarmupPool(name, prefab, count);
            }
        }

        /// <summary>
        /// 检查池中是否有可用对象
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>是否有可用对象</returns>
        public static bool CheckGameObjectInPool(string name)
        {
            var poolManager = GetInstance();
            return poolManager?.CheckGameObjectInPool(name) ?? false;
        }

        /// <summary>
        /// 检查池是否存在
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池是否存在</returns>
        public static bool HasPool(string name)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.HasPool(name);
            }
            return false;
        }

        /// <summary>
        /// 获取池中对象数量
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池中对象数量</returns>
        public static int GetPoolCount(string name)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetPoolCount(name);
            }
            return 0;
        }

        /// <summary>
        /// 获取所有池名称
        /// </summary>
        /// <returns>池名称数组</returns>
        public static string[] GetPoolNames()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetPoolNames();
            }
            return new string[0];
        }

        /// <summary>
        /// 清空指定名称的对象池
        /// </summary>
        /// <param name="name">对象池名称</param>
        public static void ClearPool(string name)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.ClearPool(name);
            }
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public static void ClearPool()
        {
            var poolManager = GetInstance();
            poolManager?.Clear();
        }

        /// <summary>
        /// 清空对象池
        /// </summary>
        /// <param name="destroyPoolGO">是否同时销毁对象池根节点</param>
        public static void Clear(bool destroyPoolGO = true)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.Clear(destroyPoolGO);
            }
        }

        /// <summary>
        /// 获取对象池统计信息
        /// </summary>
        /// <returns>统计信息字符串</returns>
        public static string GetPoolStatistics()
        {
            var poolManager = GetInstance();
            return poolManager?.GetPoolStatistics() ?? "对象池管理器未初始化";
        }

        /// <summary>
        /// 获取指定池的统计信息
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>统计信息字符串</returns>
        public static string GetPoolStatistics(string name)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetPoolStatistics(name);
            }
            return "对象池管理器未初始化";
        }

        /// <summary>
        /// 获取详细统计信息
        /// </summary>
        /// <returns>详细统计信息</returns>
        public static string GetDetailedStatistics()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetDetailedStatistics();
            }
            return "对象池管理器未初始化";
        }

        /// <summary>
        /// 预热对象池（协程版本）
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <param name="callback">预热完成回调</param>
        public static void WarmupPool(string poolName, int count, UnityAction callback = null)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.WarmupPool(poolName, count, callback);
            }
        }

        /// <summary>
        /// 预热对象池（UniTask版本）
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <returns>预热完成Task</returns>
        public static async System.Threading.Tasks.Task WarmupPoolAsync(string poolName, int count)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                await poolManagerImpl.WarmupPoolAsync(poolName, count);
            }
        }

        /// <summary>
        /// 智能预热对象池（根据配置选择协程或UniTask）
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <param name="callback">预热完成回调（仅协程版本）</param>
        public static void WarmupPoolSmart(string poolName, int count, UnityAction callback = null)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.WarmupPoolSmart(poolName, count, callback);
            }
        }

        /// <summary>
        /// 启动自动清理
        /// </summary>
        public static void StartAutoCleanup()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.StartAutoCleanup();
            }
        }

        /// <summary>
        /// 停止自动清理
        /// </summary>
        public static void StopAutoCleanup()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.StopAutoCleanup();
            }
        }

        /// <summary>
        /// 重置对象池统计信息
        /// </summary>
        public static void ResetStatistics()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.ResetStatistics();
            }
        }

        /// <summary>
        /// 清空指定对象池
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <returns>是否成功清空</returns>
        public static bool ClearSpecificPool(string poolName)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.ClearSpecificPool(poolName);
            }
            return false;
        }

        /// <summary>
        /// 获取所有对象池名称
        /// </summary>
        /// <returns>对象池名称列表</returns>
        public static string[] GetAllPoolNames()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetAllPoolNames();
            }
            return new string[0];
        }

        /// <summary>
        /// 设置对象池最大容量
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="maxSize">最大容量</param>
        /// <returns>是否成功设置</returns>
        public static bool SetPoolMaxSize(string poolName, int maxSize)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.SetPoolMaxSize(poolName, maxSize);
            }
            return false;
        }

        /// <summary>
        /// 强制清理所有对象池
        /// </summary>
        public static void ForceCleanupAllPools()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.ForceCleanupAllPools();
            }
        }

        /// <summary>
        /// 获取对象池内存使用情况
        /// </summary>
        /// <returns>内存使用信息</returns>
        public static string GetMemoryUsage()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetMemoryUsage();
            }
            return "对象池管理器未初始化";
        }
        #endregion

        #region 配置方法
        /// <summary>
        /// 设置对象池根节点名称
        /// </summary>
        /// <param name="name">根节点名称</param>
        public static void SetPoolRootName(string name)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetPoolRootName(name);
            }
        }

        /// <summary>
        /// 设置默认最大容量
        /// </summary>
        /// <param name="maxSize">最大容量</param>
        public static void SetDefaultMaxSize(int maxSize)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetDefaultMaxSize(maxSize);
            }
        }

        /// <summary>
        /// 设置是否显示调试信息
        /// </summary>
        /// <param name="show">是否显示</param>
        public static void SetDebugInfo(bool show)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetDebugInfo(show);
            }
        }

        /// <summary>
        /// 设置是否启用自动清理
        /// </summary>
        /// <param name="enable">是否启用</param>
        public static void SetAutoCleanup(bool enable)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetAutoCleanup(enable);
            }
        }

        /// <summary>
        /// 设置自动清理间隔
        /// </summary>
        /// <param name="interval">清理间隔（秒）</param>
        public static void SetAutoCleanupInterval(float interval)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetAutoCleanupInterval(interval);
            }
        }

        /// <summary>
        /// 设置清理阈值
        /// </summary>
        /// <param name="threshold">清理阈值</param>
        public static void SetCleanupThreshold(int threshold)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetCleanupThreshold(threshold);
            }
        }

        /// <summary>
        /// 设置是否使用UniTask
        /// </summary>
        /// <param name="use">是否使用</param>
        public static void SetUseUniTask(bool use)
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                poolManagerImpl.SetUseUniTask(use);
            }
        }

        /// <summary>
        /// 获取当前配置信息
        /// </summary>
        /// <returns>配置信息字符串</returns>
        public static string GetConfigInfo()
        {
            var poolManager = GetInstance();
            if (poolManager is PoolManager poolManagerImpl)
            {
                return poolManagerImpl.GetConfigInfo();
            }
            return "对象池管理器未初始化";
        }
        #endregion
    }
}