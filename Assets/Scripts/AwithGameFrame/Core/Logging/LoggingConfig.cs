using System;
using System.Collections.Generic;

namespace AwithGameFrame.Core.Logging
{
    /// <summary>
    /// 日志系统配置类
    /// </summary>
    [Serializable]
    public class LoggingConfig
    {
        #region 基础配置
        /// <summary>
        /// 日志级别
        /// </summary>
        public LogLevel Level = LogLevel.Debug;
        
        /// <summary>
        /// 日志模式
        /// </summary>
        public LogMode Mode = LogMode.FrameworkLog;
        
        /// <summary>
        /// 是否启用时间戳
        /// </summary>
        public bool EnableTimestamp = true;
        
        /// <summary>
        /// 是否启用堆栈跟踪
        /// </summary>
        public bool EnableStackTrace = false;
        
        /// <summary>
        /// 是否启用框架验证
        /// </summary>
        public bool EnableFrameworkValidation = true;
        #endregion
        
        #region 分类配置
        /// <summary>
        /// 分类开关配置
        /// </summary>
        public Dictionary<LogCategory, bool> CategoryEnabled = new Dictionary<LogCategory, bool>();
        
        /// <summary>
        /// 初始化分类配置
        /// </summary>
        public void InitializeCategories()
        {
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                if (!CategoryEnabled.ContainsKey(category))
                {
                    CategoryEnabled[category] = true;
                }
            }
        }
        #endregion
        
        #region 文件日志配置
        /// <summary>
        /// 文件日志配置
        /// </summary>
        public FileLoggerConfig FileConfig = new FileLoggerConfig();
        #endregion
        
        #region 性能监控配置
        /// <summary>
        /// 性能监控配置
        /// </summary>
        public PerformanceConfig PerformanceConfig = new PerformanceConfig();
        #endregion
        
        #region 构造函数
        public LoggingConfig()
        {
            InitializeCategories();
        }
        
        /// <summary>
        /// 使用预设创建配置
        /// </summary>
        /// <param name="preset">预设类型</param>
        /// <returns>配置实例</returns>
        public static LoggingConfig CreatePreset(LoggingAPI.Preset preset)
        {
            var config = new LoggingConfig();
            
            switch (preset)
            {
                case LoggingAPI.Preset.Development:
                    config.Level = LogLevel.Debug;
                    config.Mode = LogMode.FrameworkLog;
                    config.EnableFrameworkValidation = true;
                    config.FileConfig.EnableFileLogging = true;
                    config.PerformanceConfig.EnablePerformanceLogging = true;
                    break;
                    
                case LoggingAPI.Preset.Production:
                    config.Level = LogLevel.Info;
                    config.Mode = LogMode.FrameworkLog;
                    config.EnableFrameworkValidation = false;
                    config.FileConfig.EnableFileLogging = true;
                    config.PerformanceConfig.EnablePerformanceLogging = false;
                    // 只启用核心分类
                    foreach (var category in config.CategoryEnabled.Keys)
                    {
                        config.CategoryEnabled[category] = (category == LogCategory.Core || category == LogCategory.UI || category == LogCategory.Network || category == LogCategory.Pool || category == LogCategory.Config);
                    }
                    break;
                    
                case LoggingAPI.Preset.Debug:
                    config.Level = LogLevel.Debug;
                    config.Mode = LogMode.Both;
                    config.EnableFrameworkValidation = true;
                    config.FileConfig.EnableFileLogging = true;
                    config.PerformanceConfig.EnablePerformanceLogging = true;
                    config.PerformanceConfig.FpsUpdateInterval = 1f;
                    break;
                    
                case LoggingAPI.Preset.Performance:
                    config.Level = LogLevel.Fatal;
                    config.Mode = LogMode.None;
                    config.EnableFrameworkValidation = false;
                    config.FileConfig.EnableFileLogging = false;
                    config.PerformanceConfig.EnablePerformanceLogging = false;
                    break;
                    
                case LoggingAPI.Preset.Minimal:
                    config.Level = LogLevel.Error;
                    config.Mode = LogMode.FrameworkLog;
                    config.EnableFrameworkValidation = false;
                    config.FileConfig.EnableFileLogging = true;
                    config.PerformanceConfig.EnablePerformanceLogging = false;
                    // 只启用核心分类
                    foreach (var category in config.CategoryEnabled.Keys)
                    {
                        config.CategoryEnabled[category] = (category == LogCategory.Core);
                    }
                    break;
            }
            
            return config;
        }
        #endregion
    }
    
    /// <summary>
    /// 文件日志配置
    /// </summary>
    [Serializable]
    public class FileLoggerConfig
    {
        /// <summary>
        /// 是否启用文件日志
        /// </summary>
        public bool EnableFileLogging = false;
        
        /// <summary>
        /// 日志目录路径
        /// </summary>
        public string LogDirectory = null;
        
        /// <summary>
        /// 最大文件大小（字节）
        /// </summary>
        public int MaxFileSize = 10 * 1024 * 1024; // 10MB
        
        /// <summary>
        /// 最大文件数量
        /// </summary>
        public int MaxFiles = 10;
        
        /// <summary>
        /// 是否启用时间戳
        /// </summary>
        public bool EnableTimestamp = true;
        
        /// <summary>
        /// 是否启用堆栈跟踪
        /// </summary>
        public bool EnableStackTrace = false;
        
        /// <summary>
        /// 最小日志级别
        /// </summary>
        public LogLevel MinLevel = LogLevel.Debug;
        
        /// <summary>
        /// 清理间隔（秒）
        /// </summary>
        public float CleanupInterval = 3600f; // 1小时
    }
    
    /// <summary>
    /// 性能监控配置
    /// </summary>
    [Serializable]
    public class PerformanceConfig
    {
        /// <summary>
        /// 是否启用性能日志记录
        /// </summary>
        public bool EnablePerformanceLogging = true;
        
        /// <summary>
        /// 是否启用自动日志记录
        /// </summary>
        public bool EnableAutoLogging = true;
        
        /// <summary>
        /// FPS更新间隔（秒）
        /// </summary>
        public float FpsUpdateInterval = 2f;
    }
}
