using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Core.Config
{
    /// <summary>
    /// 配置管理器
    /// 核心包提供基础实现，支持框架配置和游戏配置的统一管理
    /// 继承BaseManager，使用框架日志系统
    /// </summary>
    public class ConfigManager : BaseManager<ConfigManager>
    {
        #region 字段
        private FrameworkConfig _frameworkConfig;
        private Dictionary<string, object> _gameConfigs = new Dictionary<string, object>();
        private Dictionary<string, object> _runtimeConfigs = new Dictionary<string, object>();
        private bool _isInitialized = false;
        private FrameworkConfig _lastFrameworkConfig; // 用于检测配置变更
        #endregion

        #region 属性
        /// <summary>
        /// 框架配置
        /// </summary>
        public FrameworkConfig FrameworkConfig
        {
            get
            {
                if (_frameworkConfig == null)
                {
                    LoadFrameworkConfig();
                }
                return _frameworkConfig;
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                LoadFrameworkConfig();
                InitializeLoggingSystem();
                _isInitialized = true;
                LoggingAPI.Info(LogCategory.Config, "配置管理器初始化完成");
            }
            catch (System.Exception e)
            {
                LoggingAPI.Error(LogCategory.Config, $"配置管理器初始化失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 更新方法，检测FrameworkConfig变更
        /// 注意：Inspector修改主要通过FrameworkConfig的OnValidate方法检测
        /// </summary>
        void Update()
        {
            if (!_isInitialized || _frameworkConfig == null) return;
            
            // 检测FrameworkConfig对象引用是否发生变化（资源重新加载等）
            if (_lastFrameworkConfig != null && _lastFrameworkConfig != _frameworkConfig)
            {
                // 配置对象发生了变化，重新加载
                LoadFrameworkConfig();
                NotifyFrameworkConfigChanged();
            }
            else if (_lastFrameworkConfig == null)
            {
                // 首次设置
                _lastFrameworkConfig = _frameworkConfig;
            }
        }

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        private void InitializeLoggingSystem()
        {
            try
            {
                if (!_frameworkConfig.enableLoggingModule)
                {
                    LoggingAPI.Info(LogCategory.Config, "日志模块已禁用，跳过日志系统初始化");
                    return;
                }

                var loggingManager = LoggingManager.GetInstance();
                var loggingConfig = _frameworkConfig.GetLoggingConfig();
                
                // 使用框架配置初始化日志系统
                loggingManager.Initialize(loggingConfig);
                
                LoggingAPI.Info(LogCategory.Config, "日志系统初始化完成");
            }
            catch (System.Exception e)
            {
                LoggingAPI.Error(LogCategory.Config, $"日志系统初始化失败: {e.Message}");
            }
        }

        /// <summary>
        /// 加载框架配置
        /// </summary>
        private void LoadFrameworkConfig()
        {
            try
            {
                _frameworkConfig = Resources.Load<FrameworkConfig>("FrameworkConfig");
                if (_frameworkConfig == null)
                {
                    LoggingAPI.Warn(LogCategory.Config, "未找到FrameworkConfig，创建默认配置文件");
                    _frameworkConfig = CreateDefaultFrameworkConfig();
                    CreateDefaultFrameworkConfigAsset();
                }
                LoggingAPI.Info(LogCategory.Config, "框架配置加载成功");
            }
            catch (System.Exception e)
            {
                LoggingAPI.Error(LogCategory.Config, $"加载框架配置失败: {e.Message}");
                _frameworkConfig = CreateDefaultFrameworkConfig();
                CreateDefaultFrameworkConfigAsset();
            }
        }

        /// <summary>
        /// 创建默认框架配置
        /// </summary>
        private FrameworkConfig CreateDefaultFrameworkConfig()
        {
            var config = ScriptableObject.CreateInstance<FrameworkConfig>();
            
            // 设置默认值
            config.resourceRootPath = "Assets/Resources";
            config.saveGamePath = "Saves";
            config.configPath = "Configs";
            
            config.enableAudioModule = true;
            config.enableUIModule = true;
            config.enableInputModule = true;
            config.enablePoolModule = true;
            config.enableLoggingModule = true;
            
            config.defaultLanguage = "zh-CN";
            config.defaultVolume = 1.0f;
            config.targetFrameRate = 60;
            
            config.logLevel = LogLevel.Info;
            config.logMode = LogMode.FrameworkLog;
            config.enableTimestamp = true;
            config.enableStackTrace = false;
            config.enableFrameworkValidation = true;
            
            config.enableFileLogging = false;
            config.logDirectory = "";
            config.maxFileSizeMB = 10;
            config.maxFiles = 10;
            config.cleanupIntervalHours = 1f;
            
            config.enablePerformanceMonitoring = false;
            config.enablePerformanceLogging = true;
            config.fpsUpdateInterval = 2f;
            
            config.poolDefaultMaxSize = 100;
            config.poolEnableAutoCleanup = true;
            config.poolAutoCleanupInterval = 30.0f;
            
            config.maxConcurrentSFX = 10;
            
            config.enableDebugMode = false;
            config.showDebugInfo = false;
            
            LoggingAPI.Info(LogCategory.Config, "创建默认框架配置");
            return config;
        }

        /// <summary>
        /// 重新加载框架配置
        /// </summary>
        public void ReloadFrameworkConfig()
        {
            LoadFrameworkConfig();
            InitializeLoggingSystem();
            EventCenter.GetInstance().EventTrigger("ConfigReloaded");
        }

        /// <summary>
        /// 创建默认FrameworkConfig资源文件
        /// </summary>
        public void CreateDefaultFrameworkConfigAsset()
        {
            try
            {
                var defaultConfig = CreateDefaultFrameworkConfig();
                
                // 确保Resources目录存在
                string resourcesPath = "Assets/Resources";
                if (!System.IO.Directory.Exists(resourcesPath))
                {
                    System.IO.Directory.CreateDirectory(resourcesPath);
                }
                
                // 创建资源文件
                string assetPath = "Assets/Resources/FrameworkConfig.asset";
                
                #if UNITY_EDITOR
                // 编辑器模式下使用AssetDatabase
                UnityEditor.AssetDatabase.CreateAsset(defaultConfig, assetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
                LoggingAPI.Info(LogCategory.Config, $"默认FrameworkConfig资源已创建: {assetPath}");
                #else
                // 运行时模式下，配置已经在内存中，不需要创建文件
                LoggingAPI.Info(LogCategory.Config, "运行时模式：使用内存中的默认配置");
                #endif
            }
            catch (System.Exception e)
            {
                LoggingAPI.Error(LogCategory.Config, $"创建默认FrameworkConfig资源失败: {e.Message}");
            }
        }
        #endregion

        #region 游戏配置管理
        /// <summary>
        /// 加载游戏配置
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <param name="filePath">文件路径</param>
        public void LoadGameConfig<T>(string configName, string filePath) where T : GameConfigData
        {
            try
            {
                var textAsset = Resources.Load<TextAsset>(filePath);
                if (textAsset == null)
                {
                    LoggingAPI.Error(LogCategory.Config, $"未找到配置文件: {filePath}");
                    return;
                }

                var configs = JsonUtility.FromJson<ConfigWrapper<T>>(textAsset.text);
                _gameConfigs[configName] = configs.data;
                LoggingAPI.Info(LogCategory.Config, $"游戏配置加载成功: {configName}, 数量: {configs.data.Length}");
            }
            catch (System.Exception e)
            {
                LoggingAPI.Error(LogCategory.Config, $"加载游戏配置失败: {configName}, 错误: {e.Message}");
            }
        }

        /// <summary>
        /// 获取游戏配置数据
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <param name="id">数据ID</param>
        /// <returns>配置数据</returns>
        public T GetGameConfigData<T>(string configName, int id) where T : GameConfigData
        {
            if (!_gameConfigs.TryGetValue(configName, out object configData))
            {
                LoggingAPI.Warn(LogCategory.Config, $"未找到游戏配置: {configName}");
                return null;
            }

            var configs = configData as T[];
            if (configs == null)
            {
                LoggingAPI.Warn(LogCategory.Config, $"游戏配置类型不匹配: {configName}");
                return null;
            }

            foreach (var config in configs)
            {
                if (config.id == id)
                {
                    return config;
                }
            }

            LoggingAPI.Warn(LogCategory.Config, $"未找到指定ID的配置数据: {configName}, ID: {id}");
            return null;
        }

        /// <summary>
        /// 获取所有游戏配置数据
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <returns>所有配置数据</returns>
        public T[] GetAllGameConfigData<T>(string configName) where T : GameConfigData
        {
            if (!_gameConfigs.TryGetValue(configName, out object configData))
            {
                LoggingAPI.Warn(LogCategory.Config, $"未找到游戏配置: {configName}");
                return new T[0];
            }

            return configData as T[];
        }
        #endregion

        #region 运行时配置管理
        /// <summary>
        /// 获取运行时配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        public T Get<T>(string key, T defaultValue = default(T))
        {
            if (_runtimeConfigs.TryGetValue(key, out object value))
            {
                return (T)value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 设置运行时配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        public void Set<T>(string key, T value)
        {
            _runtimeConfigs[key] = value;
            NotifyConfigChanged(key, value);
        }

        /// <summary>
        /// 检查运行时配置是否存在
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否存在</returns>
        public bool Has(string key)
        {
            return _runtimeConfigs.ContainsKey(key);
        }

        /// <summary>
        /// 移除运行时配置
        /// </summary>
        /// <param name="key">配置键</param>
        public void Remove(string key)
        {
            _runtimeConfigs.Remove(key);
        }

        /// <summary>
        /// 清空所有运行时配置
        /// </summary>
        public void Clear()
        {
            _runtimeConfigs.Clear();
        }
        #endregion

        #region 配置变更通知
        /// <summary>
        /// 配置变更事件
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        private void NotifyConfigChanged(string key, object value)
        {
            var eventData = new ConfigAPI.ConfigChangedEventData
            {
                key = key,
                value = value
            };
            EventCenter.GetInstance().EventTrigger<ConfigAPI.ConfigChangedEventData>("ConfigChanged", eventData);
        }
        

        /// <summary>
        /// 框架配置变更事件
        /// </summary>
        private void NotifyFrameworkConfigChanged()
        {
            LoggingAPI.Info(LogCategory.Config, "检测到FrameworkConfig在Inspector中被修改");
            
            // 重新初始化日志系统
            InitializeLoggingSystem();
            
            // 触发框架配置变更事件
            var eventData = new ConfigAPI.ConfigChangedEventData
            {
                key = "FrameworkConfig",
                value = "Inspector修改"
            };
            EventCenter.GetInstance().EventTrigger<ConfigAPI.ConfigChangedEventData>("ConfigChanged", eventData);
        }
        #endregion

        #region 状态检查
        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        /// <returns>是否已初始化</returns>
        public bool IsInitialized()
        {
            return _isInitialized;
        }
        #endregion

        #region 内部类
        /// <summary>
        /// 配置包装器，用于JSON反序列化
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        [System.Serializable]
        private class ConfigWrapper<T>
        {
            public T[] data;
        }

        #endregion
    }
}
