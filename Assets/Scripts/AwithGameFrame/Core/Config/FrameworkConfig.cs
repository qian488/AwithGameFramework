using UnityEngine;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Core.Config
{
    /// <summary>
    /// 框架配置 - 使用ScriptableObject存储框架内部配置
    /// 相对稳定，类型安全，编辑器友好
    /// 集成日志系统配置
    /// </summary>
    [CreateAssetMenu(fileName = "FrameworkConfig", menuName = "AwithGameFrame/Framework Config")]
    public class FrameworkConfig : ScriptableObject
    {
        #region 属性变更检测
        // 用于检测属性变更的字段
        private System.Collections.Generic.Dictionary<string, object> _lastValues = new System.Collections.Generic.Dictionary<string, object>();
        private bool _isInitialized = false;
        #endregion

        [Header("路径配置")]
        public string resourceRootPath = "Assets/Resources";
        public string saveGamePath = "Saves";
        public string configPath = "Configs";
        
        [Header("模块开关")]
        public bool enableAudioModule = true;
        public bool enableUIModule = true;
        public bool enableInputModule = true;
        public bool enablePoolModule = true;
        public bool enableLoggingModule = true;
        
        [Header("默认设置")]
        public string defaultLanguage = "zh-CN";
        [Range(0f, 1f)]
        public float defaultVolume = 1.0f;
        public int targetFrameRate = 60;
        
        [Header("日志系统配置")]
        [Tooltip("日志级别")]
        public LogLevel logLevel = LogLevel.Info;
        
        [Tooltip("日志模式")]
        public LogMode logMode = LogMode.FrameworkLog;
        
        [Tooltip("是否启用时间戳")]
        public bool enableTimestamp = true;
        
        [Tooltip("是否启用堆栈跟踪")]
        public bool enableStackTrace = false;
        
        [Tooltip("是否启用框架验证")]
        public bool enableFrameworkValidation = true;
        
        [Header("文件日志配置")]
        [Tooltip("是否启用文件日志")]
        public bool enableFileLogging = false;
        
        [Tooltip("日志目录路径（为空则使用默认路径）")]
        public string logDirectory = "";
        
        [Tooltip("最大文件大小（MB）")]
        [Range(1, 100)]
        public int maxFileSizeMB = 10;
        
        [Tooltip("最大文件数量")]
        [Range(1, 50)]
        public int maxFiles = 10;
        
        [Tooltip("清理间隔（小时）")]
        [Range(0.1f, 24f)]
        public float cleanupIntervalHours = 1f;
        
        [Header("性能监控配置")]
        [Tooltip("是否启用性能监控")]
        public bool enablePerformanceMonitoring = false;
        
        [Tooltip("是否启用性能日志记录")]
        public bool enablePerformanceLogging = true;
        
        [Tooltip("FPS更新间隔（秒）")]
        [Range(0.1f, 10f)]
        public float fpsUpdateInterval = 2f;
        
        [Header("对象池配置")]
        [Tooltip("对象池默认最大容量")]
        public int poolDefaultMaxSize = 100;
        
        [Tooltip("是否启用对象池自动清理")]
        public bool poolEnableAutoCleanup = true;
        
        [Tooltip("对象池自动清理间隔（秒）")]
        public float poolAutoCleanupInterval = 30.0f;
        
        [Header("音频配置")]
        [Tooltip("最大并发音效数量")]
        public int maxConcurrentSFX = 10;
        
        [Header("调试配置")]
        [Tooltip("是否启用调试模式")]
        public bool enableDebugMode = false;
        
        [Tooltip("是否显示调试信息")]
        public bool showDebugInfo = false;
        
        #region 日志配置转换方法
        /// <summary>
        /// 获取日志配置
        /// </summary>
        /// <returns>日志配置对象</returns>
        public LoggingConfig GetLoggingConfig()
        {
            var config = new LoggingConfig
            {
                Level = logLevel,
                Mode = logMode,
                EnableTimestamp = enableTimestamp,
                EnableStackTrace = enableStackTrace,
                EnableFrameworkValidation = enableFrameworkValidation,
                FileConfig = new FileLoggerConfig
                {
                    EnableFileLogging = enableFileLogging,
                    LogDirectory = string.IsNullOrEmpty(logDirectory) ? null : logDirectory,
                    MaxFileSize = maxFileSizeMB * 1024 * 1024, // 转换为字节
                    MaxFiles = maxFiles,
                    EnableTimestamp = enableTimestamp,
                    EnableStackTrace = enableStackTrace,
                    MinLevel = logLevel,
                    CleanupInterval = cleanupIntervalHours * 3600f // 转换为秒
                },
                PerformanceConfig = new PerformanceConfig
                {
                    EnablePerformanceLogging = enablePerformanceLogging,
                    EnableAutoLogging = enablePerformanceMonitoring,
                    FpsUpdateInterval = fpsUpdateInterval
                }
            };
            
            // 初始化分类配置
            config.InitializeCategories();
            
            return config;
        }
        
        /// <summary>
        /// 从日志配置更新框架配置
        /// </summary>
        /// <param name="loggingConfig">日志配置</param>
        public void UpdateFromLoggingConfig(LoggingConfig loggingConfig)
        {
            if (loggingConfig == null) return;
            
            logLevel = loggingConfig.Level;
            logMode = loggingConfig.Mode;
            enableTimestamp = loggingConfig.EnableTimestamp;
            enableStackTrace = loggingConfig.EnableStackTrace;
            enableFrameworkValidation = loggingConfig.EnableFrameworkValidation;
            
            if (loggingConfig.FileConfig != null)
            {
                enableFileLogging = loggingConfig.FileConfig.EnableFileLogging;
                logDirectory = loggingConfig.FileConfig.LogDirectory ?? "";
                maxFileSizeMB = loggingConfig.FileConfig.MaxFileSize / (1024 * 1024);
                maxFiles = loggingConfig.FileConfig.MaxFiles;
                cleanupIntervalHours = loggingConfig.FileConfig.CleanupInterval / 3600f;
            }
            
            if (loggingConfig.PerformanceConfig != null)
            {
                enablePerformanceLogging = loggingConfig.PerformanceConfig.EnablePerformanceLogging;
                enablePerformanceMonitoring = loggingConfig.PerformanceConfig.EnableAutoLogging;
                fpsUpdateInterval = loggingConfig.PerformanceConfig.FpsUpdateInterval;
            }
        }
        #endregion

        #region Unity回调
        /// <summary>
        /// Unity在Inspector中修改值时调用
        /// 仅在编辑器中有效，运行时不会调用
        /// </summary>
        private void OnValidate()
        {
            #if UNITY_EDITOR
            // 在编辑器中，通知配置管理器配置已变更
            if (Application.isPlaying)
            {
                // 延迟一帧执行，确保Inspector修改完成
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (Application.isPlaying)
                    {
                        NotifyConfigChanged();
                    }
                };
            }
            #endif
        }

        /// <summary>
        /// 通知配置变更
        /// </summary>
        private void NotifyConfigChanged()
        {
            try
            {
                // 检测具体哪些属性发生了变化
                var changedProperties = DetectChangedProperties();
                
                if (changedProperties.Count > 0)
                {
                    // 通过事件中心通知配置变更
                    var eventData = new ConfigAPI.ConfigChangedEventData
                    {
                        key = "FrameworkConfig",
                        value = $"Inspector修改: {string.Join(", ", changedProperties)}"
                    };
                    EventCenter.GetInstance()?.EventTrigger<ConfigAPI.ConfigChangedEventData>("ConfigChanged", eventData);
                    
                    Debug.Log($"[FrameworkConfig] 检测到Inspector修改，变更的属性: {string.Join(", ", changedProperties)}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FrameworkConfig] 发送配置变更事件失败: {e.Message}");
            }
        }

        /// <summary>
        /// 检测发生变化的属性
        /// 通过比较当前值与上次记录的值来检测具体哪个属性发生了变化
        /// </summary>
        private System.Collections.Generic.List<string> DetectChangedProperties()
        {
            var changedProperties = new System.Collections.Generic.List<string>();
            
            // 如果还未初始化，先记录当前所有值
            if (!_isInitialized)
            {
                RecordCurrentValues();
                _isInitialized = true;
                return changedProperties; // 首次初始化，不报告变更
            }
            
            // 检查每个属性是否发生变化
            CheckPropertyChange("resourceRootPath", resourceRootPath, ref changedProperties);
            CheckPropertyChange("saveGamePath", saveGamePath, ref changedProperties);
            CheckPropertyChange("configPath", configPath, ref changedProperties);
            
            CheckPropertyChange("enableAudioModule", enableAudioModule, ref changedProperties);
            CheckPropertyChange("enableUIModule", enableUIModule, ref changedProperties);
            CheckPropertyChange("enableInputModule", enableInputModule, ref changedProperties);
            CheckPropertyChange("enablePoolModule", enablePoolModule, ref changedProperties);
            CheckPropertyChange("enableLoggingModule", enableLoggingModule, ref changedProperties);
            
            CheckPropertyChange("defaultLanguage", defaultLanguage, ref changedProperties);
            CheckPropertyChange("defaultVolume", defaultVolume, ref changedProperties);
            CheckPropertyChange("targetFrameRate", targetFrameRate, ref changedProperties);
            
            CheckPropertyChange("logLevel", logLevel, ref changedProperties);
            CheckPropertyChange("logMode", logMode, ref changedProperties);
            CheckPropertyChange("enableTimestamp", enableTimestamp, ref changedProperties);
            CheckPropertyChange("enableStackTrace", enableStackTrace, ref changedProperties);
            CheckPropertyChange("enableFrameworkValidation", enableFrameworkValidation, ref changedProperties);
            
            CheckPropertyChange("enableFileLogging", enableFileLogging, ref changedProperties);
            CheckPropertyChange("logDirectory", logDirectory, ref changedProperties);
            CheckPropertyChange("maxFileSizeMB", maxFileSizeMB, ref changedProperties);
            CheckPropertyChange("maxFiles", maxFiles, ref changedProperties);
            CheckPropertyChange("cleanupIntervalHours", cleanupIntervalHours, ref changedProperties);
            
            CheckPropertyChange("enablePerformanceMonitoring", enablePerformanceMonitoring, ref changedProperties);
            CheckPropertyChange("enablePerformanceLogging", enablePerformanceLogging, ref changedProperties);
            CheckPropertyChange("fpsUpdateInterval", fpsUpdateInterval, ref changedProperties);
            
            CheckPropertyChange("poolDefaultMaxSize", poolDefaultMaxSize, ref changedProperties);
            CheckPropertyChange("poolEnableAutoCleanup", poolEnableAutoCleanup, ref changedProperties);
            CheckPropertyChange("poolAutoCleanupInterval", poolAutoCleanupInterval, ref changedProperties);
            
            CheckPropertyChange("maxConcurrentSFX", maxConcurrentSFX, ref changedProperties);
            CheckPropertyChange("enableDebugMode", enableDebugMode, ref changedProperties);
            CheckPropertyChange("showDebugInfo", showDebugInfo, ref changedProperties);
            
            // 更新记录的值
            RecordCurrentValues();
            
            return changedProperties;
        }

        /// <summary>
        /// 检查单个属性是否发生变化
        /// </summary>
        private void CheckPropertyChange<T>(string propertyName, T currentValue, ref System.Collections.Generic.List<string> changedProperties)
        {
            if (_lastValues.TryGetValue(propertyName, out object lastValue))
            {
                if (!Equals(currentValue, lastValue))
                {
                    changedProperties.Add($"{propertyName}({lastValue} -> {currentValue})");
                }
            }
        }

        /// <summary>
        /// 记录当前所有属性值
        /// </summary>
        private void RecordCurrentValues()
        {
            _lastValues["resourceRootPath"] = resourceRootPath;
            _lastValues["saveGamePath"] = saveGamePath;
            _lastValues["configPath"] = configPath;
            
            _lastValues["enableAudioModule"] = enableAudioModule;
            _lastValues["enableUIModule"] = enableUIModule;
            _lastValues["enableInputModule"] = enableInputModule;
            _lastValues["enablePoolModule"] = enablePoolModule;
            _lastValues["enableLoggingModule"] = enableLoggingModule;
            
            _lastValues["defaultLanguage"] = defaultLanguage;
            _lastValues["defaultVolume"] = defaultVolume;
            _lastValues["targetFrameRate"] = targetFrameRate;
            
            _lastValues["logLevel"] = logLevel;
            _lastValues["logMode"] = logMode;
            _lastValues["enableTimestamp"] = enableTimestamp;
            _lastValues["enableStackTrace"] = enableStackTrace;
            _lastValues["enableFrameworkValidation"] = enableFrameworkValidation;
            
            _lastValues["enableFileLogging"] = enableFileLogging;
            _lastValues["logDirectory"] = logDirectory;
            _lastValues["maxFileSizeMB"] = maxFileSizeMB;
            _lastValues["maxFiles"] = maxFiles;
            _lastValues["cleanupIntervalHours"] = cleanupIntervalHours;
            
            _lastValues["enablePerformanceMonitoring"] = enablePerformanceMonitoring;
            _lastValues["enablePerformanceLogging"] = enablePerformanceLogging;
            _lastValues["fpsUpdateInterval"] = fpsUpdateInterval;
            
            _lastValues["poolDefaultMaxSize"] = poolDefaultMaxSize;
            _lastValues["poolEnableAutoCleanup"] = poolEnableAutoCleanup;
            _lastValues["poolAutoCleanupInterval"] = poolAutoCleanupInterval;
            
            _lastValues["maxConcurrentSFX"] = maxConcurrentSFX;
            _lastValues["enableDebugMode"] = enableDebugMode;
            _lastValues["showDebugInfo"] = showDebugInfo;
        }
        #endregion
    }
}