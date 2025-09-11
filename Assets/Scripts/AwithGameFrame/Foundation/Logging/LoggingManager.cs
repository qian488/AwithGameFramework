using System;
using System.Collections.Generic;
using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Foundation.Logging
{
    /// <summary>
    /// 日志管理器 - 统一管理框架日志系统
    /// </summary>
    public class LoggingManager : BaseManager<LoggingManager>
    {
        
        #region 字段
        private LoggingConfig _config;
        private FileLogger _fileLogger;
        private FrameworkValidator _frameworkValidator;
        #endregion
        
        #region 属性
        /// <summary>
        /// 当前配置
        /// </summary>
        public LoggingConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = new LoggingConfig();
                }
                return _config;
            }
            set => _config = value;
        }
        
        /// <summary>
        /// 当前日志级别
        /// </summary>
        public LogLevel CurrentLevel => Config.Level;
        
        /// <summary>
        /// 当前日志模式
        /// </summary>
        public LogMode CurrentMode => Config.Mode;
        
        /// <summary>
        /// 是否启用时间戳
        /// </summary>
        public bool EnableTimestamp
        {
            get => Config.EnableTimestamp;
            set => Config.EnableTimestamp = value;
        }
        
        /// <summary>
        /// 是否启用堆栈跟踪
        /// </summary>
        public bool EnableStackTrace
        {
            get => Config.EnableStackTrace;
            set => Config.EnableStackTrace = value;
        }
        
        /// <summary>
        /// 是否启用文件日志
        /// </summary>
        public bool EnableFileLogging
        {
            get => Config.FileConfig.EnableFileLogging;
            set
            {
                if (Config.FileConfig.EnableFileLogging != value)
                {
                    Config.FileConfig.EnableFileLogging = value;
                    if (value)
                    {
                        InitializeFileLogger();
                    }
                    else
                    {
                        ShutdownFileLogger();
                    }
                }
            }
        }
        
        /// <summary>
        /// 文件日志器实例
        /// </summary>
        public FileLogger FileLogger => _fileLogger;

        /// <summary>
        /// 框架验证器实例
        /// </summary>
        public FrameworkValidator FrameworkValidator
        {
            get
            {
                if (_frameworkValidator == null)
                {
                    _frameworkValidator = FrameworkValidator.GetInstance();
                }
                return _frameworkValidator;
            }
        }
        #endregion
        
        #region 构造函数
        public LoggingManager()
        {
            _config = new LoggingConfig();
        }
        #endregion
        
        #region 公共方法
        /// <summary>
        /// 设置日志级别
        /// </summary>
        /// <param name="level">日志级别</param>
        public void SetLogLevel(LogLevel level)
        {
            Config.Level = level;
        }
        
        /// <summary>
        /// 设置分类是否启用
        /// </summary>
        /// <param name="category">日志分类</param>
        /// <param name="enabled">是否启用</param>
        public void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            Config.CategoryEnabled[category] = enabled;
        }
        
        /// <summary>
        /// 使用配置初始化日志系统
        /// </summary>
        /// <param name="config">日志配置</param>
        public void Initialize(LoggingConfig config)
        {
            _config = config ?? new LoggingConfig();
            ApplyConfiguration();
        }
        
        /// <summary>
        /// 应用配置
        /// </summary>
        private void ApplyConfiguration()
        {
            // 初始化框架验证器
            if (Config.EnableFrameworkValidation)
            {
                FrameworkValidator.EnableValidation = true;
                FrameworkValidator.EnableWarnings = true;
                FrameworkValidator.EnableErrors = true;
            }
            
            // 根据模式配置文件日志
            switch (Config.Mode)
            {
                case LogMode.UnityDebug:
                    Config.FileConfig.EnableFileLogging = false;
                    break;
                    
                case LogMode.FrameworkLog:
                case LogMode.Both:
                    Config.FileConfig.EnableFileLogging = true;
                    if (!string.IsNullOrEmpty(Config.FileConfig.LogDirectory))
                    {
                        InitializeFileLogger(Config.FileConfig.LogDirectory);
                    }
                    else
                    {
                        InitializeFileLogger();
                    }
                    break;
                    
                case LogMode.None:
                    Config.FileConfig.EnableFileLogging = false;
                    break;
            }
            
            Log(LogLevel.Info, LogCategory.Core, $"日志系统配置应用完成 - 模式: {Config.Mode}");
        }
        
        /// <summary>
        /// 初始化日志系统
        /// </summary>
        /// <param name="mode">日志模式</param>
        /// <param name="enableValidation">是否启用框架验证</param>
        /// <param name="customLogPath">自定义日志路径</param>
        public void Initialize(LogMode mode = LogMode.FrameworkLog, bool enableValidation = true, string customLogPath = null)
        {
            // 创建配置
            var config = new LoggingConfig
            {
                Mode = mode,
                EnableFrameworkValidation = enableValidation,
                FileConfig = { LogDirectory = customLogPath }
            };
            
            // 使用新的配置初始化
            Initialize(config);
        }
        
        /// <summary>
        /// 切换日志模式
        /// </summary>
        /// <param name="mode">新的日志模式</param>
        public void SwitchMode(LogMode mode)
        {
            if (Config.Mode == mode) return;
            
            Config.Mode = mode;
            
            // 重新配置系统
            ApplyConfiguration();
        }
        
        /// <summary>
        /// 设置自定义日志路径
        /// </summary>
        /// <param name="path">日志路径</param>
        public void SetCustomLogPath(string path)
        {
            Config.FileConfig.LogDirectory = path;
            if (Config.FileConfig.EnableFileLogging)
            {
                InitializeFileLogger(path);
            }
        }

        /// <summary>
        /// 验证框架使用
        /// </summary>
        /// <param name="type">验证类型</param>
        /// <param name="message">验证消息</param>
        /// <param name="context">上下文对象</param>
        public void ValidateFrameworkUsage(ValidationType type, string message, object context = null)
        {
            if (Config.EnableFrameworkValidation)
            {
                FrameworkValidator.ValidateFrameworkUsage(type, message, context);
            }
        }
        
        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public void Log(LogLevel level, LogCategory category, string message, object context = null)
        {
            // 检查日志级别
            if (level < Config.Level) return;
            
            // 检查分类是否启用
            if (!Config.CategoryEnabled.GetValueOrDefault(category, true)) return;
            
            // 格式化消息
            string formattedMessage = FormatMessage(level, category, message, context);
            
            // 输出到Unity Console
            OutputToUnityConsole(level, formattedMessage, context);
            
            // 输出到文件
            if (Config.FileConfig.EnableFileLogging && _fileLogger != null)
            {
                _fileLogger.WriteLog(level, category, message, context);
            }
        }
        
        /// <summary>
        /// 记录异常日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public void LogException(LogLevel level, LogCategory category, string message, Exception exception, object context = null)
        {
            // 检查日志级别
            if (level < Config.Level) return;
            
            // 检查分类是否启用
            if (!Config.CategoryEnabled.GetValueOrDefault(category, true)) return;
            
            string exceptionMessage = $"{message}\nException: {exception.Message}";
            if (Config.EnableStackTrace)
            {
                exceptionMessage += $"\nStackTrace: {exception.StackTrace}";
            }
            
            // 格式化消息
            string formattedMessage = FormatMessage(level, category, exceptionMessage, context);
            
            // 输出到Unity Console
            OutputToUnityConsole(level, formattedMessage, context);
            
            // 输出到文件
            if (Config.FileConfig.EnableFileLogging && _fileLogger != null)
            {
                _fileLogger.WriteException(level, category, message, exception, context);
            }
        }
        
        /// <summary>
        /// 清空所有日志设置
        /// </summary>
        public void ResetSettings()
        {
            // 重置为默认配置
            _config = new LoggingConfig();
            ApplyConfiguration();
        }
        
        /// <summary>
        /// 初始化文件日志器
        /// </summary>
        /// <param name="logDirectory">日志目录</param>
        public void InitializeFileLogger(string logDirectory = null)
        {
            if (_fileLogger == null)
            {
                _fileLogger = FileLogger.GetInstance();
            }
            
            _fileLogger.Initialize(logDirectory);
            _fileLogger.MinLevel = Config.Level;
            _fileLogger.EnableTimestamp = Config.EnableTimestamp;
            _fileLogger.EnableStackTrace = Config.EnableStackTrace;
            
            // 同步分类设置
            foreach (var kvp in Config.CategoryEnabled)
            {
                _fileLogger.SetCategoryEnabled(kvp.Key, kvp.Value);
            }
        }
        
        /// <summary>
        /// 关闭文件日志器
        /// </summary>
        public void ShutdownFileLogger()
        {
            if (_fileLogger != null)
            {
                _fileLogger.Shutdown();
            }
        }
        
        /// <summary>
        /// 手动轮转日志文件
        /// </summary>
        public void RotateLogFile()
        {
            if (_fileLogger != null)
            {
                _fileLogger.RotateLogFile();
            }
        }
        
        /// <summary>
        /// 清理旧日志文件
        /// </summary>
        public void CleanupOldFiles()
        {
            if (_fileLogger != null)
            {
                _fileLogger.CleanupOldFiles();
            }
        }
        
        /// <summary>
        /// 更新日志管理器（在Update中调用）
        /// </summary>
        public void Update()
        {
            if (_fileLogger != null)
            {
                _fileLogger.Update();
            }
        }
        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 格式化日志消息
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">原始消息</param>
        /// <param name="context">上下文对象</param>
        /// <returns>格式化后的消息</returns>
        private string FormatMessage(LogLevel level, LogCategory category, string message, object context)
        {
            var parts = new List<string>();
            
            // 时间戳
            if (Config.EnableTimestamp)
            {
                parts.Add($"[{DateTime.Now:HH:mm:ss.fff}]");
            }
            
            // 日志级别
            parts.Add($"[{level.ToString().ToUpper()}]");
            
            // 日志分类
            parts.Add($"[{category.ToString()}]");
            
            // 上下文信息
            if (context != null)
            {
                string contextInfo = context is UnityEngine.Object ? context.GetType().Name : context.ToString();
                parts.Add($"[{contextInfo}]");
            }
            
            // 消息内容
            parts.Add(message);
            
            return string.Join(" ", parts);
        }
        
        /// <summary>
        /// 输出到Unity Console
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">格式化后的消息</param>
        /// <param name="context">上下文对象</param>
        private void OutputToUnityConsole(LogLevel level, string message, object context)
        {
            UnityEngine.Object unityObject = context as UnityEngine.Object;
            
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    Debug.Log(message, unityObject);
                    break;
                case LogLevel.Info:
                    Debug.Log(message, unityObject);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning(message, unityObject);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(message, unityObject);
                    break;
            }
        }
        #endregion
    }
}
