using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Logging;
using UnityEngine.Events;

namespace AwithGameFrame.Foundation.Pool
{
    /// <summary>
    /// 对象池管理器实现
    /// 基础包提供具体实现，支持配置系统
    /// </summary>
    public class PoolManager : BaseManager<PoolManager>, IPoolManager
    {
        #region 字段
        /// <summary>
        /// 对象池字典，使用字符串作为键，支持动态创建
        /// </summary>
        private Dictionary<string, PoolData> poolDictionary = new Dictionary<string, PoolData>();

        /// <summary>
        /// 对象池根节点
        /// </summary>
        private GameObject poolGO;

        /// <summary>
        /// 自动清理协程
        /// </summary>
        private Coroutine autoCleanupCoroutine;

        /// <summary>
        /// 是否启用自动清理
        /// </summary>
        private bool isAutoCleanupEnabled = false;
        
        /// <summary>
        /// Mono管理器引用
        /// </summary>
        private MonoManager monoManager;
        #endregion

        #region 配置管理
        /// <summary>
        /// 默认配置值
        /// </summary>
        private const string DEFAULT_POOL_ROOT_NAME = "Pool";
        private const int DEFAULT_MAX_SIZE = 100;
        private const bool DEFAULT_SHOW_DEBUG_INFO = false;
        private const bool DEFAULT_ENABLE_AUTO_CLEANUP = true;
        private const float DEFAULT_AUTO_CLEANUP_INTERVAL = 30f;
        private const int DEFAULT_CLEANUP_THRESHOLD = 10;
        private const bool DEFAULT_USE_UNITASK = true; // 默认使用UniTask，性能更好
        
        /// <summary>
        /// 预热对象池时的批处理大小
        /// </summary>
        private const int WARMUP_BATCH_SIZE = 10;
        
        /// <summary>
        /// 自动清理时移除对象池的倍数阈值
        /// </summary>
        private const int CLEANUP_MULTIPLIER = 2;
        
        /// <summary>
        /// 清理时移除对象数量的除数
        /// </summary>
        private const int CLEANUP_DIVISOR = 2;
        
        /// <summary>
        /// 每个GameObject的估算内存大小（字节）
        /// </summary>
        private const int ESTIMATED_OBJECT_SIZE = 1024;

        /// <summary>
        /// 当前配置值
        /// </summary>
        private string poolRootName = DEFAULT_POOL_ROOT_NAME;
        private int defaultMaxSize = DEFAULT_MAX_SIZE;
        private bool showDebugInfo = DEFAULT_SHOW_DEBUG_INFO;
        private bool enableAutoCleanup = DEFAULT_ENABLE_AUTO_CLEANUP;
        private float autoCleanupInterval = DEFAULT_AUTO_CLEANUP_INTERVAL;
        private int cleanupThreshold = DEFAULT_CLEANUP_THRESHOLD;
        private bool useUniTask = DEFAULT_USE_UNITASK;

        /// <summary>
        /// 获取对象池根节点名称
        /// </summary>
        /// <returns>根节点名称</returns>
        private string GetPoolRootName()
        {
            return poolRootName;
        }

        /// <summary>
        /// 获取默认最大容量
        /// </summary>
        /// <returns>默认最大容量</returns>
        private int GetDefaultMaxSize()
        {
            return defaultMaxSize;
        }

        /// <summary>
        /// 是否显示调试信息
        /// </summary>
        /// <returns>是否显示调试信息</returns>
        private bool ShouldShowDebugInfo()
        {
            return showDebugInfo;
        }

        /// <summary>
        /// 是否启用自动清理
        /// </summary>
        /// <returns>是否启用自动清理</returns>
        private bool IsAutoCleanupEnabled()
        {
            return enableAutoCleanup;
        }

        /// <summary>
        /// 获取自动清理间隔
        /// </summary>
        /// <returns>自动清理间隔（秒）</returns>
        private float GetAutoCleanupInterval()
        {
            return autoCleanupInterval;
        }

        /// <summary>
        /// 获取清理阈值
        /// </summary>
        /// <returns>清理阈值</returns>
        private int GetCleanupThreshold()
        {
            return cleanupThreshold;
        }

        /// <summary>
        /// 是否使用UniTask
        /// </summary>
        /// <returns>是否使用UniTask</returns>
        private bool ShouldUseUniTask()
        {
            return useUniTask;
        }

        #endregion
        
        #region 初始化
        /// <summary>
        /// 初始化对象池管理器
        /// </summary>
        public void Initialize()
        {
            // 初始化MonoManager
            monoManager = MonoManager.GetInstance();
            
            // 创建对象池根节点
            if (poolGO == null)
            {
                poolGO = new GameObject(GetPoolRootName());
                UnityEngine.Object.DontDestroyOnLoad(poolGO);
            }
        }
        #endregion
        
        #region IPoolManager 实现
        /// <summary>
        /// 获取池中对象（异步回调方式）
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="callback">获取到对象后的回调</param>
        public void GetGameObject(string name, UnityAction<GameObject> callback)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                callback?.Invoke(null);
                return;
            }

            if (poolDictionary.ContainsKey(name))
            {
                var poolData = poolDictionary[name];
                if (poolData.HasAvailableObject())
                {
                    var go = poolData.GetGameObject();
                    if (go != null)
                    {
                        go.SetActive(true);
                        callback?.Invoke(go);
                        return;
                    }
                }
            }

            // 如果池中没有可用对象，异步加载新对象
            LoadGameObjectAsync(name, callback);
        }

        /// <summary>
        /// 获取池中的对象（同步方式）
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体（如果池为空则创建新对象）</param>
        /// <returns>获取的对象</returns>
        public GameObject GetGameObject(string name, GameObject prefab = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return null;
            }

            if (poolDictionary.ContainsKey(name))
            {
                var poolData = poolDictionary[name];
                if (poolData.HasAvailableObject())
                {
                    var go = poolData.GetGameObject();
                    if (go != null)
                    {
                        go.SetActive(true);
                        return go;
                    }
                }
            }

            // 如果池中没有可用对象且提供了预制体，创建新对象
            if (prefab != null)
            {
                var go = GameObject.Instantiate(prefab);
                go.name = name;
                go.SetActive(true);
                return go;
            }

            LoggingAPI.Warn($"PoolManager: 池 '{name}' 中没有可用对象，且未提供预制体");
            return null;
        }

        /// <summary>
        /// 将对象放回池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="go">要放回的对象</param>
        public void PushGameObject(string name, GameObject go)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return;
            }

            if (go == null)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 尝试压入空对象");
                return;
            }

            if (poolGO == null) 
                poolGO = new GameObject(GetPoolRootName());

            if (poolDictionary.ContainsKey(name))
            {
                poolDictionary[name].PushGameObject(go);
            }
            else
            {
                // 使用配置中的默认最大容量
                int actualMaxSize = GetDefaultMaxSize();
                
                var poolData = new PoolData(go, poolGO, actualMaxSize);
                poolDictionary.Add(name, poolData);
                
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 创建新对象池 - {name}, maxSize: {actualMaxSize}");
                }
            }
        }

        /// <summary>
        /// 预加载对象到池中
        /// </summary>
        /// <param name="name">池名称</param>
        /// <param name="prefab">预制体</param>
        /// <param name="count">预加载数量</param>
        public void WarmupPool(string name, GameObject prefab, int count)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return;
            }

            if (prefab == null)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 预制体不能为空");
                return;
            }

            if (count <= 0)
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 预加载数量必须大于0");
                return;
            }

            if (poolGO == null)
                poolGO = new GameObject(GetPoolRootName());

            // 确保池存在
            if (!poolDictionary.ContainsKey(name))
            {
                var poolData = new PoolData(prefab, poolGO, GetDefaultMaxSize());
                poolDictionary.Add(name, poolData);
            }

            // 预加载指定数量的对象
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.Instantiate(prefab);
                go.name = name;
                go.SetActive(false);
                poolDictionary[name].PushGameObject(go);
            }

            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 预加载完成 - {name}, 数量: {count}");
            }
        }

        /// <summary>
        /// 检查池中是否有可用对象
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>是否有可用对象</returns>
        public bool CheckGameObjectInPool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return poolDictionary.ContainsKey(name) && poolDictionary[name].HasAvailableObject();
        }

        /// <summary>
        /// 检查池是否存在
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池是否存在</returns>
        public bool HasPool(string name)
        {
            return !string.IsNullOrEmpty(name) && poolDictionary.ContainsKey(name);
        }

        /// <summary>
        /// 获取池中对象数量
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>池中对象数量</returns>
        public int GetPoolCount(string name)
        {
            if (string.IsNullOrEmpty(name) || !poolDictionary.ContainsKey(name))
                return 0;

            return poolDictionary[name].GetPoolCount();
        }

        /// <summary>
        /// 获取所有池名称
        /// </summary>
        /// <returns>池名称数组</returns>
        public string[] GetPoolNames()
        {
            var names = new string[poolDictionary.Count];
            int index = 0;
            foreach (var kvp in poolDictionary)
            {
                names[index++] = kvp.Key;
            }
            return names;
        }

        /// <summary>
        /// 清空指定名称的对象池
        /// </summary>
        /// <param name="name">对象池名称</param>
        public void ClearPool(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return;
            }

            if (poolDictionary.ContainsKey(name))
            {
                poolDictionary[name].ClearPool();
                poolDictionary.Remove(name);
                
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 已清空对象池 - {name}");
                }
            }
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public void ClearPool()
        {
            foreach (var kvp in poolDictionary)
            {
                kvp.Value.ClearPool();
            }
            poolDictionary.Clear();
            
            if (poolGO != null)
            {
                UnityEngine.Object.DestroyImmediate(poolGO);
                poolGO = null;
            }
            
            LoggingAPI.Info(LogCategory.Pool, "PoolManager: 所有对象池已清空");
        }

        /// <summary>
        /// 清空对象池
        /// </summary>
        /// <param name="destroyPoolGO">是否同时销毁对象池根节点</param>
        public void Clear(bool destroyPoolGO = true)
        {
            foreach (var kvp in poolDictionary)
            {
                kvp.Value.ClearPool();
            }
            poolDictionary.Clear();
            
            if (destroyPoolGO && poolGO != null)
            {
                UnityEngine.Object.DestroyImmediate(poolGO);
                poolGO = null;
            }
            
            LoggingAPI.Info(LogCategory.Pool, "PoolManager: 所有对象池已清空");
        }

        /// <summary>
        /// 获取对象池统计信息
        /// </summary>
        /// <returns>统计信息字符串</returns>
        public string GetPoolStatistics()
        {
            if (poolDictionary.Count == 0)
                return "没有对象池";

            var stats = new List<string>();
            stats.Add($"对象池统计信息 (共{poolDictionary.Count}个池):");
            
            foreach (var kvp in poolDictionary)
            {
                var poolData = kvp.Value;
                stats.Add($"- {kvp.Key}: {poolData.GetStatistics()}");
            }
            
            return StringHelper.Join("\n", stats.ToArray());
        }

        /// <summary>
        /// 获取指定池的统计信息
        /// </summary>
        /// <param name="name">池名称</param>
        /// <returns>统计信息字符串</returns>
        public string GetPoolStatistics(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "池名称不能为空";

            if (!poolDictionary.ContainsKey(name))
                return $"池 '{name}' 不存在";

            var poolData = poolDictionary[name];
            return $"池 '{name}': {poolData.GetStatistics()}";
        }

        #endregion

        #region 配置方法
        /// <summary>
        /// 设置对象池根节点名称
        /// </summary>
        /// <param name="name">根节点名称</param>
        public void SetPoolRootName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 根节点名称不能为空，使用默认名称");
                return;
            }
            poolRootName = name;
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 对象池根节点名称已设置为 '{name}'");
            }
        }

        /// <summary>
        /// 设置默认最大容量
        /// </summary>
        /// <param name="maxSize">最大容量</param>
        public void SetDefaultMaxSize(int maxSize)
        {
            if (maxSize <= 0)
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 最大容量必须大于0，使用默认值");
                return;
            }
            defaultMaxSize = maxSize;
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 默认最大容量已设置为 {maxSize}");
            }
        }

        /// <summary>
        /// 设置是否显示调试信息
        /// </summary>
        /// <param name="show">是否显示</param>
        public void SetDebugInfo(bool show)
        {
            showDebugInfo = show;
            LoggingAPI.Info($"PoolManager: 调试信息显示已{(show ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 设置是否启用自动清理
        /// </summary>
        /// <param name="enable">是否启用</param>
        public void SetAutoCleanup(bool enable)
        {
            enableAutoCleanup = enable;
            
            if (enable)
            {
                StartAutoCleanup();
            }
            else
            {
                StopAutoCleanup();
            }
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 自动清理已{(enable ? "启用" : "禁用")}");
            }
        }

        /// <summary>
        /// 设置自动清理间隔
        /// </summary>
        /// <param name="interval">清理间隔（秒）</param>
        public void SetAutoCleanupInterval(float interval)
        {
            if (interval <= 0)
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 清理间隔必须大于0，使用默认值");
                return;
            }
            autoCleanupInterval = interval;
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 自动清理间隔已设置为 {interval} 秒");
            }
        }

        /// <summary>
        /// 设置清理阈值
        /// </summary>
        /// <param name="threshold">清理阈值</param>
        public void SetCleanupThreshold(int threshold)
        {
            if (threshold < 0)
            {
                LoggingAPI.Warn(LogCategory.Pool, "PoolManager: 清理阈值不能小于0，使用默认值");
                return;
            }
            cleanupThreshold = threshold;
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 清理阈值已设置为 {threshold}");
            }
        }

        /// <summary>
        /// 设置是否使用UniTask
        /// </summary>
        /// <param name="use">是否使用</param>
        public void SetUseUniTask(bool use)
        {
            useUniTask = use;
            
            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info($"PoolManager: 异步实现已切换为{(use ? "UniTask" : "协程")}");
            }
        }

        /// <summary>
        /// 获取当前配置信息
        /// </summary>
        /// <returns>配置信息字符串</returns>
        public string GetConfigInfo()
        {
            var config = new List<string>();
            config.Add("=== 对象池配置信息 ===");
            config.Add($"根节点名称: {poolRootName}");
            config.Add($"默认最大容量: {defaultMaxSize}");
            config.Add($"显示调试信息: {showDebugInfo}");
            config.Add($"启用自动清理: {enableAutoCleanup}");
            config.Add($"清理间隔: {autoCleanupInterval}秒");
            config.Add($"清理阈值: {cleanupThreshold}");
            config.Add($"使用UniTask: {useUniTask}");
            
            return StringHelper.Join("\n", config.ToArray());
        }
        #endregion

        #region 新增功能方法
        /// <summary>
        /// 同步获取对象
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <returns>对象，如果没有则返回null</returns>
        public GameObject GetGameObjectSync(string poolName)
        {
            if (string.IsNullOrEmpty(poolName))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return null;
            }

            if (poolDictionary.ContainsKey(poolName))
            {
                var poolData = poolDictionary[poolName];
                if (poolData.HasAvailableObject())
                {
                    var go = poolData.GetGameObject();
                    if (go != null)
                    {
                        go.SetActive(true);
                        return go;
                    }
                }
            }

            return null;
        }
        #endregion

        #region 协程实现
        /// <summary>
        /// 预热对象池（智能降级：优先UniTask，降级到协程）
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <param name="callback">预热完成回调</param>
        public void WarmupPool(string poolName, int count, UnityAction callback = null)
        {
            if (string.IsNullOrEmpty(poolName) || count <= 0)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 预热参数无效");
                callback?.Invoke();
                return;
            }

            // 优先尝试使用UniTask
            if (ShouldUseUniTask())
            {
                try
                {
                    // 使用UniTask异步预热
                    WarmupPoolAsyncInternal(poolName, count).ContinueWith(task => {
                        if (task.IsFaulted)
                        {
                            LoggingAPI.Warn($"PoolManager: UniTask预热失败，降级到协程 - {task.Exception?.GetBaseException()?.Message}");
                            // 降级到协程
                            WarmupPoolCoroutine(poolName, count, callback);
                        }
                        else
                        {
                            callback?.Invoke();
                        }
                    });
                    return;
                }
                catch (System.Exception ex)
                {
                    LoggingAPI.Warn($"PoolManager: UniTask不可用，降级到协程 - {ex.Message}");
                }
            }

            // 降级到协程
            // 确保MonoManager已初始化
            if (monoManager == null)
            {
                monoManager = MonoManager.GetInstance();
            }

            monoManager.StartCoroutine(WarmupPoolCoroutine(poolName, count, callback));
        }

        /// <summary>
        /// 预热对象池协程
        /// </summary>
        public IEnumerator WarmupPoolCoroutine(string poolName, int count, UnityAction callback)
        {
            int createdCount = 0;
            var request = Resources.LoadAsync<GameObject>(poolName);
            yield return request;

            if (request.asset != null)
            {
                var prefab = request.asset as GameObject;
                if (prefab != null)
                {
                    // 分批创建对象，每10个对象等待一帧
                    for (int i = 0; i < count; i++)
                    {
                        var go = GameObject.Instantiate(prefab);
                        go.name = prefab.name;
                        go.SetActive(false);
                        PushGameObject(poolName, go);
                        createdCount++;

                        if (i % WARMUP_BATCH_SIZE == 0) // 每批对象等待一帧
                        {
                            yield return null;
                        }
                    }

                    if (ShouldShowDebugInfo())
                    {
                        LoggingAPI.Info($"PoolManager: 预热完成 - {poolName}, 创建了 {createdCount} 个对象");
                    }
                }
                else
                {
                    LoggingAPI.Error($"PoolManager: 资源 {poolName} 不是GameObject");
                }
            }
            else
            {
                LoggingAPI.Error($"PoolManager: 无法加载资源 {poolName}");
            }

            callback?.Invoke();
        }
        #endregion

        #region UniTask实现
        /// <summary>
        /// 预热对象池（UniTask版本）
        /// 需要UniTask依赖
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <returns>预热完成Task</returns>
        public async System.Threading.Tasks.Task WarmupPoolAsync(string poolName, int count)
        {
            if (string.IsNullOrEmpty(poolName) || count <= 0)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 预热参数无效");
                return;
            }

            await WarmupPoolAsyncInternal(poolName, count);
        }

        /// <summary>
        /// 智能预热对象池（优先使用UniTask，降级到协程）
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="count">预热数量</param>
        /// <param name="callback">预热完成回调</param>
        public void WarmupPoolSmart(string poolName, int count, UnityAction callback = null)
        {
            // 直接调用WarmupPool，它已经实现了智能降级逻辑
            WarmupPool(poolName, count, callback);
        }

        /// <summary>
        /// 预热对象池异步实现（UniTask版本）
        /// </summary>
        private async System.Threading.Tasks.Task WarmupPoolAsyncInternal(string poolName, int count)
        {
            int createdCount = 0;
            
            // 异步加载资源
            var request = Resources.LoadAsync<GameObject>(poolName);
            while (!request.isDone)
            {
                await System.Threading.Tasks.Task.Yield();
            }

            if (request.asset != null)
            {
                var prefab = request.asset as GameObject;
                if (prefab != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var go = GameObject.Instantiate(prefab);
                        go.name = prefab.name;
                        go.SetActive(false);
                        PushGameObject(poolName, go);
                        createdCount++;

                        // 每10个对象等待一帧
                        if (i % WARMUP_BATCH_SIZE == 0)
                        {
                            await System.Threading.Tasks.Task.Yield();
                        }
                    }

                    if (ShouldShowDebugInfo())
                    {
                        LoggingAPI.Info($"PoolManager: 预热完成 - {poolName}, 创建了 {createdCount} 个对象");
                    }
                }
                else
                {
                    LoggingAPI.Error($"PoolManager: 资源 {poolName} 不是GameObject");
                }
            }
            else
            {
                LoggingAPI.Error($"PoolManager: 无法加载资源 {poolName}");
            }
        }
        #endregion

        #region 自动清理功能
        /// <summary>
        /// 启动自动清理
        /// </summary>
        public void StartAutoCleanup()
        {
            if (isAutoCleanupEnabled) return;

            isAutoCleanupEnabled = IsAutoCleanupEnabled();
            if (isAutoCleanupEnabled)
            {
                // 确保MonoManager已初始化
                if (monoManager == null)
                {
                    monoManager = MonoManager.GetInstance();
                }

                autoCleanupCoroutine = monoManager.StartCoroutine(AutoCleanupCoroutine());
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info(LogCategory.Pool, "PoolManager: 自动清理已启动");
                }
            }
        }

        /// <summary>
        /// 停止自动清理
        /// </summary>
        public void StopAutoCleanup()
        {
            if (!isAutoCleanupEnabled) return;

            isAutoCleanupEnabled = false;
            if (autoCleanupCoroutine != null)
            {
                // 确保MonoManager已初始化
                if (monoManager == null)
                {
                    monoManager = MonoManager.GetInstance();
                }

                monoManager.StopCoroutine(autoCleanupCoroutine);
                autoCleanupCoroutine = null;
            }

            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info(LogCategory.Pool, "PoolManager: 自动清理已停止");
            }
        }

        /// <summary>
        /// 自动清理协程
        /// </summary>
        private IEnumerator AutoCleanupCoroutine()
        {
            while (isAutoCleanupEnabled)
            {
                yield return new WaitForSeconds(GetAutoCleanupInterval());
                
                if (isAutoCleanupEnabled)
                {
                    CleanupUnusedPools();
                }
            }
        }

        /// <summary>
        /// 清理未使用的对象池
        /// </summary>
        private void CleanupUnusedPools()
        {
            var threshold = GetCleanupThreshold();
            var currentTime = Time.time;
            var poolsToRemove = new List<string>();

            foreach (var kvp in poolDictionary)
            {
                var poolData = kvp.Value;
                var timeSinceLastUsed = currentTime - poolData.lastUsedTime;
                
                // 如果对象池长时间未使用且对象数量超过阈值，则清理
                if (timeSinceLastUsed > GetAutoCleanupInterval() && poolData.GetPoolCount() > threshold)
                {
                    // 清理一半的对象
                    int objectsToRemove = poolData.GetPoolCount() / CLEANUP_DIVISOR;
                    for (int i = 0; i < objectsToRemove; i++)
                    {
                        if (poolData.HasAvailableObject())
                        {
                            var go = poolData.GetGameObject();
                            if (go != null)
                            {
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        }
                    }

                    if (ShouldShowDebugInfo())
                    {
                        LoggingAPI.Info($"PoolManager: 清理对象池 {kvp.Key}，移除了 {objectsToRemove} 个对象");
                    }
                }

                // 如果对象池完全为空且长时间未使用，则移除整个池
                if (poolData.GetPoolCount() == 0 && timeSinceLastUsed > GetAutoCleanupInterval() * CLEANUP_MULTIPLIER)
                {
                    poolsToRemove.Add(kvp.Key);
                }
            }

            // 移除空的对象池
            foreach (var poolName in poolsToRemove)
            {
                poolDictionary.Remove(poolName);
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 移除空对象池 {poolName}");
                }
            }
        }
        #endregion

        #region 统计信息
        /// <summary>
        /// 获取详细统计信息
        /// </summary>
        /// <returns>详细统计信息</returns>
        public string GetDetailedStatistics()
        {
            if (poolDictionary.Count == 0)
                return "没有对象池";

            var stats = new List<string>();
            stats.Add("=== 对象池详细统计信息 ===");
            stats.Add($"总池数: {poolDictionary.Count}");
            stats.Add($"自动清理: {(isAutoCleanupEnabled ? "启用" : "禁用")}");
            stats.Add($"清理间隔: {GetAutoCleanupInterval()}秒");
            stats.Add($"清理阈值: {GetCleanupThreshold()}");
            stats.Add("");

            int totalPooled = 0;
            int totalActive = 0;
            int totalCreated = 0;
            int totalReused = 0;

            foreach (var kvp in poolDictionary)
            {
                var poolData = kvp.Value;
                stats.Add($"池: {kvp.Key}");
                stats.Add($"  - 池中对象: {poolData.GetPoolCount()}");
                stats.Add($"  - 活跃对象: {poolData.currentActive}");
                stats.Add($"  - 总创建数: {poolData.totalCreated}");
                stats.Add($"  - 总复用数: {poolData.totalReused}");
                stats.Add($"  - 最后使用: {Time.time - poolData.lastUsedTime:F1}秒前");
                stats.Add("");

                totalPooled += poolData.GetPoolCount();
                totalActive += poolData.currentActive;
                totalCreated += poolData.totalCreated;
                totalReused += poolData.totalReused;
            }

            stats.Add("=== 总计 ===");
            stats.Add($"池中对象总数: {totalPooled}");
            stats.Add($"活跃对象总数: {totalActive}");
            stats.Add($"总创建数: {totalCreated}");
            stats.Add($"总复用数: {totalReused}");
            stats.Add($"复用率: {(totalCreated > 0 ? (float)totalReused / totalCreated * 100 : 0):F1}%");

            return StringHelper.Join("\n", stats.ToArray());
        }

        /// <summary>
        /// 重置对象池统计信息
        /// </summary>
        public void ResetStatistics()
        {
            foreach (var kvp in poolDictionary)
            {
                kvp.Value.ResetStatistics();
            }

            if (ShouldShowDebugInfo())
            {
                LoggingAPI.Info(LogCategory.Pool, "PoolManager: 统计信息已重置");
            }
        }

        /// <summary>
        /// 清空指定对象池
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <returns>是否成功清空</returns>
        public bool ClearSpecificPool(string poolName)
        {
            if (string.IsNullOrEmpty(poolName))
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 对象池名称不能为空");
                return false;
            }

            if (poolDictionary.ContainsKey(poolName))
            {
                poolDictionary[poolName].ClearPool();
                poolDictionary.Remove(poolName);
                
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 已清空对象池 {poolName}");
                }
                return true;
            }

            LoggingAPI.Warn($"PoolManager: 对象池 {poolName} 不存在");
            return false;
        }

        /// <summary>
        /// 获取所有对象池名称
        /// </summary>
        /// <returns>对象池名称列表</returns>
        public string[] GetAllPoolNames()
        {
            var names = new string[poolDictionary.Count];
            int index = 0;
            foreach (var kvp in poolDictionary)
            {
                names[index++] = kvp.Key;
            }
            return names;
        }

        /// <summary>
        /// 设置对象池最大容量
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="maxSize">最大容量</param>
        /// <returns>是否成功设置</returns>
        public bool SetPoolMaxSize(string poolName, int maxSize)
        {
            if (string.IsNullOrEmpty(poolName) || maxSize < 0)
            {
                LoggingAPI.Error(LogCategory.Pool, "PoolManager: 参数无效");
                return false;
            }

            if (poolDictionary.ContainsKey(poolName))
            {
                poolDictionary[poolName].maxSize = maxSize;
                
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 设置对象池 {poolName} 最大容量为 {maxSize}");
                }
                return true;
            }

            LoggingAPI.Warn($"PoolManager: 对象池 {poolName} 不存在");
            return false;
        }

        /// <summary>
        /// 强制清理所有对象池（立即清理，不等待自动清理）
        /// </summary>
        public void ForceCleanupAllPools()
        {
            var currentTime = Time.time;
            var poolsToRemove = new List<string>();

            foreach (var kvp in poolDictionary)
            {
                var poolData = kvp.Value;
                var timeSinceLastUsed = currentTime - poolData.lastUsedTime;
                
                // 清理长时间未使用的对象池
                if (timeSinceLastUsed > GetAutoCleanupInterval())
                {
                    // 清理一半的对象
                    int objectsToRemove = poolData.GetPoolCount() / CLEANUP_DIVISOR;
                    for (int i = 0; i < objectsToRemove; i++)
                    {
                        if (poolData.HasAvailableObject())
                        {
                            var go = poolData.GetGameObject();
                            if (go != null)
                            {
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        }
                    }

                    if (ShouldShowDebugInfo())
                    {
                        LoggingAPI.Info($"PoolManager: 强制清理对象池 {kvp.Key}，移除了 {objectsToRemove} 个对象");
                    }
                }

                // 如果对象池完全为空，标记为移除
                if (poolData.GetPoolCount() == 0)
                {
                    poolsToRemove.Add(kvp.Key);
                }
            }

            // 移除空的对象池
            foreach (var poolName in poolsToRemove)
            {
                poolDictionary.Remove(poolName);
                if (ShouldShowDebugInfo())
                {
                    LoggingAPI.Info($"PoolManager: 移除空对象池 {poolName}");
                }
            }
        }

        /// <summary>
        /// 获取对象池内存使用情况
        /// </summary>
        /// <returns>内存使用信息</returns>
        public string GetMemoryUsage()
        {
            var stats = new List<string>();
            stats.Add("=== 对象池内存使用情况 ===");
            
            int totalObjects = 0;
            int totalActive = 0;
            long estimatedMemory = 0;

            foreach (var kvp in poolDictionary)
            {
                var poolData = kvp.Value;
                int poolCount = poolData.GetPoolCount();
                int activeCount = poolData.currentActive;
                
                totalObjects += poolCount;
                totalActive += activeCount;
                
                // 粗略估算内存使用（每个GameObject约1KB）
                estimatedMemory += (poolCount + activeCount) * ESTIMATED_OBJECT_SIZE;
                
                stats.Add($"池: {kvp.Key}");
                stats.Add($"  - 池中对象: {poolCount}");
                stats.Add($"  - 活跃对象: {activeCount}");
                stats.Add($"  - 估算内存: {(poolCount + activeCount) * ESTIMATED_OBJECT_SIZE / 1024.0f:F2} KB");
            }

            stats.Add("");
            stats.Add("总计:");
            stats.Add($"  - 总对象数: {totalObjects + totalActive}");
            stats.Add($"  - 池中对象: {totalObjects}");
            stats.Add($"  - 活跃对象: {totalActive}");
            stats.Add($"  - 估算总内存: {estimatedMemory / 1024.0f:F2} KB");

            return StringHelper.Join("\n", stats.ToArray());
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 异步加载GameObject
        /// </summary>
        /// <param name="poolName">对象池名称</param>
        /// <param name="callback">加载完成回调</param>
        private void LoadGameObjectAsync(string poolName, UnityAction<GameObject> callback)
        {
            // 尝试从Resources加载
            var request = Resources.LoadAsync<GameObject>(poolName);
            if (request != null)
            {
                monoManager.StartCoroutine(LoadGameObjectCoroutine(request, poolName, callback));
            }
            else
            {
                LoggingAPI.Error($"PoolManager: 无法加载资源 {poolName}");
                callback?.Invoke(null);
            }
        }

        /// <summary>
        /// 加载GameObject协程
        /// </summary>
        /// <param name="request">资源加载请求</param>
        /// <param name="poolName">对象池名称</param>
        /// <param name="callback">加载完成回调</param>
        private IEnumerator LoadGameObjectCoroutine(ResourceRequest request, string poolName, UnityAction<GameObject> callback)
        {
            yield return request;
            
            if (request.asset != null)
            {
                var prefab = request.asset as GameObject;
                if (prefab != null)
                {
                    var go = GameObject.Instantiate(prefab);
                    go.name = prefab.name;
                    go.SetActive(false);
                    
                    // 将新对象添加到池中
                    PushGameObject(poolName, go);
                    
                    // 获取对象并激活
                    go.SetActive(true);
                    callback?.Invoke(go);
                }
                else
                {
                    LoggingAPI.Error($"PoolManager: 资源 {poolName} 不是GameObject");
                    callback?.Invoke(null);
                }
            }
            else
            {
                LoggingAPI.Error($"PoolManager: 加载资源失败 {poolName}");
                callback?.Invoke(null);
            }
        }
        #endregion
    }
}
