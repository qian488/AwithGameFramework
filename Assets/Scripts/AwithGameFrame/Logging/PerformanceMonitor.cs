using System;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Logging
{
    /// <summary>
    /// 性能监控器 - 监控游戏性能指标
    /// </summary>
    public class PerformanceMonitor : BaseManager<PerformanceMonitor>
    {
        #region 字段
        private Dictionary<string, float> _timers = new Dictionary<string, float>();
        private Dictionary<string, int> _counters = new Dictionary<string, int>();
        private Dictionary<string, float> _averages = new Dictionary<string, float>();
        private Dictionary<string, int> _averageCounts = new Dictionary<string, int>();
        private bool _enableAutoLogging = true;
        private bool _enablePerformanceLogging = true;
        private float _lastFrameTime = 0f;
        private int _frameCount = 0;
        private float _fpsUpdateInterval = 1f;
        #endregion
        
        #region 属性
        /// <summary>
        /// 是否启用自动日志记录
        /// </summary>
        public bool EnableAutoLogging
        {
            get => _enableAutoLogging;
            set => _enableAutoLogging = value;
        }
        
        /// <summary>
        /// 是否启用性能日志记录
        /// </summary>
        public bool EnablePerformanceLogging
        {
            get => _enablePerformanceLogging;
            set => _enablePerformanceLogging = value;
        }
        
        /// <summary>
        /// FPS更新间隔（秒）
        /// </summary>
        public float FpsUpdateInterval
        {
            get => _fpsUpdateInterval;
            set => _fpsUpdateInterval = Mathf.Max(0.1f, value);
        }
        #endregion
        
        #region 公共方法
        /// <summary>
        /// 开始计时
        /// </summary>
        /// <param name="name">计时器名称</param>
        public void StartTimer(string name)
        {
            _timers[name] = Time.realtimeSinceStartup;
        }
        
        /// <summary>
        /// 结束计时并记录日志
        /// </summary>
        /// <param name="name">计时器名称</param>
        /// <param name="autoLog">是否自动记录日志</param>
        /// <returns>耗时（秒）</returns>
        public float EndTimer(string name, bool autoLog = true)
        {
            if (!_timers.ContainsKey(name))
            {
                FrameworkLogger.Warn($"Timer '{name}' was not started");
                return 0f;
            }
            
            float duration = Time.realtimeSinceStartup - _timers[name];
            _timers.Remove(name);
            
            if (autoLog && _enableAutoLogging)
            {
                FrameworkLogger.LogPerformance($"Timer '{name}': {duration:F3}s");
            }
            
            return duration;
        }
        
        /// <summary>
        /// 获取计时器当前耗时（不结束计时）
        /// </summary>
        /// <param name="name">计时器名称</param>
        /// <returns>当前耗时（秒）</returns>
        public float GetTimerElapsed(string name)
        {
            if (!_timers.ContainsKey(name))
            {
                return 0f;
            }
            
            return Time.realtimeSinceStartup - _timers[name];
        }
        
        /// <summary>
        /// 增加计数器
        /// </summary>
        /// <param name="name">计数器名称</param>
        /// <param name="increment">增量，默认为1</param>
        public void IncrementCounter(string name, int increment = 1)
        {
            _counters[name] = _counters.GetValueOrDefault(name, 0) + increment;
        }
        
        /// <summary>
        /// 记录计数器值
        /// </summary>
        /// <param name="name">计数器名称</param>
        /// <param name="autoLog">是否自动记录日志</param>
        public void LogCounter(string name, bool autoLog = true)
        {
            if (_counters.ContainsKey(name))
            {
                if (autoLog && _enableAutoLogging)
                {
                    FrameworkLogger.LogPerformance($"Counter '{name}': {_counters[name]}");
                }
            }
        }
        
        /// <summary>
        /// 获取计数器值
        /// </summary>
        /// <param name="name">计数器名称</param>
        /// <returns>计数器值</returns>
        public int GetCounterValue(string name)
        {
            return _counters.GetValueOrDefault(name, 0);
        }
        
        /// <summary>
        /// 重置计数器
        /// </summary>
        /// <param name="name">计数器名称</param>
        public void ResetCounter(string name)
        {
            _counters.Remove(name);
        }
        
        /// <summary>
        /// 记录平均值
        /// </summary>
        /// <param name="name">平均值名称</param>
        /// <param name="value">当前值</param>
        /// <param name="autoLog">是否自动记录日志</param>
        public void RecordAverage(string name, float value, bool autoLog = true)
        {
            if (!_averages.ContainsKey(name))
            {
                _averages[name] = 0f;
                _averageCounts[name] = 0;
            }
            
            _averages[name] = (_averages[name] * _averageCounts[name] + value) / (_averageCounts[name] + 1);
            _averageCounts[name]++;
            
            if (autoLog && _enableAutoLogging)
            {
                FrameworkLogger.LogPerformance($"Average '{name}': {_averages[name]:F3} (samples: {_averageCounts[name]})");
            }
        }
        
        /// <summary>
        /// 获取平均值
        /// </summary>
        /// <param name="name">平均值名称</param>
        /// <returns>平均值</returns>
        public float GetAverage(string name)
        {
            return _averages.GetValueOrDefault(name, 0f);
        }
        
        /// <summary>
        /// 重置平均值
        /// </summary>
        /// <param name="name">平均值名称</param>
        public void ResetAverage(string name)
        {
            _averages.Remove(name);
            _averageCounts.Remove(name);
        }
        
        /// <summary>
        /// 记录当前帧率
        /// </summary>
        public void LogFrameRate()
        {
            float fps = 1.0f / Time.deltaTime;
            FrameworkLogger.LogPerformance($"FPS: {fps:F1}");
        }
        
        /// <summary>
        /// 记录内存使用情况
        /// </summary>
        public void LogMemoryUsage()
        {
            long memory = GC.GetTotalMemory(false);
            FrameworkLogger.LogPerformance($"Memory: {memory / 1024 / 1024}MB");
        }
        
        /// <summary>
        /// 记录GC信息
        /// </summary>
        public void LogGCInfo()
        {
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            
            FrameworkLogger.LogPerformance($"GC - Gen0: {gen0}, Gen1: {gen1}, Gen2: {gen2}");
        }
        
        /// <summary>
        /// 记录渲染统计信息
        /// </summary>
        public void LogRenderStats()
        {
            FrameworkLogger.LogPerformance($"Draw Calls: {(UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset != null ? "Unknown" : "Built-in")}");
            FrameworkLogger.LogPerformance($"Triangles: {(UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset != null ? "Unknown" : "Built-in")}");
        }
        
        /// <summary>
        /// 更新性能监控（在Update中调用）
        /// </summary>
        public void Update()
        {
            _frameCount++;
            _lastFrameTime += Time.deltaTime;
            
            // 定期更新FPS
            if (_lastFrameTime >= _fpsUpdateInterval)
            {
                float fps = _frameCount / _lastFrameTime;
                if (_enableAutoLogging)
                {
                    FrameworkLogger.LogPerformance($"Average FPS: {fps:F1}");
                }
                
                _frameCount = 0;
                _lastFrameTime = 0f;
            }
        }
        
        /// <summary>
        /// 清空所有监控数据
        /// </summary>
        public void ClearAll()
        {
            _timers.Clear();
            _counters.Clear();
            _averages.Clear();
            _averageCounts.Clear();
        }
        
        /// <summary>
        /// 获取所有计时器名称
        /// </summary>
        /// <returns>计时器名称列表</returns>
        public string[] GetActiveTimers()
        {
            var timerNames = new string[_timers.Count];
            _timers.Keys.CopyTo(timerNames, 0);
            return timerNames;
        }
        
        /// <summary>
        /// 获取所有计数器名称
        /// </summary>
        /// <returns>计数器名称列表</returns>
        public string[] GetActiveCounters()
        {
            var counterNames = new string[_counters.Count];
            _counters.Keys.CopyTo(counterNames, 0);
            return counterNames;
        }
        #endregion
    }
}
