using System.Collections.Generic;
using UnityEngine;

namespace AwithGameFrame.Foundation.Pool
{
    /// <summary>
    /// 对象池数据
    /// 基础包提供具体实现
    /// </summary>
    [System.Serializable]
    public class PoolData
    {
        #region 字段
        /// <summary>
        /// 对象池栈，使用Stack提高性能
        /// </summary>
        public Stack<GameObject> poolStack = new Stack<GameObject>();

        /// <summary>
        /// 最大容量
        /// </summary>
        public int maxSize;

        /// <summary>
        /// 总创建数量
        /// </summary>
        public int totalCreated;

        /// <summary>
        /// 总复用数量
        /// </summary>
        public int totalReused;

        /// <summary>
        /// 当前活跃数量
        /// </summary>
        public int currentActive;

        /// <summary>
        /// 最后使用时间
        /// </summary>
        public float lastUsedTime;

        /// <summary>
        /// 对象池根节点
        /// </summary>
        private GameObject poolRoot;
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="prefab">预制体</param>
        /// <param name="root">对象池根节点</param>
        /// <param name="maxSize">最大容量</param>
        public PoolData(GameObject prefab, GameObject root, int maxSize)
        {
            this.maxSize = maxSize;
            this.poolRoot = root;
            this.lastUsedTime = Time.time;
            
            // 将预制体添加到池中
            if (prefab != null)
            {
                prefab.SetActive(false);
                prefab.transform.SetParent(root.transform);
                poolStack.Push(prefab);
                totalCreated++;
            }
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 获取对象
        /// </summary>
        /// <returns>对象</returns>
        public GameObject GetGameObject()
        {
            if (poolStack.Count > 0)
            {
                var go = poolStack.Pop();
                currentActive++;
                totalReused++;
                lastUsedTime = Time.time;
                return go;
            }
            return null;
        }

        /// <summary>
        /// 压入对象
        /// </summary>
        /// <param name="go">对象</param>
        /// <returns>是否成功压入</returns>
        public bool PushGameObject(GameObject go)
        {
            if (go == null) return false;

            // 检查是否超过最大容量
            if (maxSize > 0 && poolStack.Count >= maxSize)
            {
                // 如果超过最大容量，销毁最老的对象
                if (poolStack.Count > 0)
                {
                    var oldGo = poolStack.Pop();
                    if (oldGo != null)
                    {
                        Object.DestroyImmediate(oldGo);
                    }
                }
            }

            go.SetActive(false);
            go.transform.SetParent(poolRoot.transform);
            poolStack.Push(go);
            currentActive--;
            lastUsedTime = Time.time;
            return true;
        }

        /// <summary>
        /// 检查是否有可用对象
        /// </summary>
        /// <returns>是否有可用对象</returns>
        public bool HasAvailableObject()
        {
            return poolStack.Count > 0;
        }

        /// <summary>
        /// 获取池中对象数量
        /// </summary>
        /// <returns>对象数量</returns>
        public int GetPoolCount()
        {
            return poolStack.Count;
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        /// <returns>统计信息字符串</returns>
        public string GetStatistics()
        {
            return $"池中:{poolStack.Count}, 活跃:{currentActive}, 总创建:{totalCreated}, 总复用:{totalReused}";
        }

        /// <summary>
        /// 检查对象是否在池中
        /// </summary>
        /// <param name="go">要检查的对象</param>
        /// <returns>是否在池中</returns>
        public bool CheckGameObjectInPool(GameObject go)
        {
            if (go == null) return false;
            
            foreach (var pooledGo in poolStack)
            {
                if (pooledGo == go)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 清空对象池
        /// </summary>
        public void ClearPool()
        {
            while (poolStack.Count > 0)
            {
                var go = poolStack.Pop();
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            poolStack.Clear();
            totalCreated = 0;
            totalReused = 0;
            currentActive = 0;
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            totalCreated = 0;
            totalReused = 0;
            currentActive = 0;
            lastUsedTime = Time.time;
        }
        #endregion
    }
}
