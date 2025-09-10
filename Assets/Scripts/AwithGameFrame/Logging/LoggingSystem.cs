using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Logging
{
    /// <summary>
    /// 日志系统统一入口 - 提供一键配置和便捷访问
    /// </summary>
    public static class LoggingSystem
    {
        #region 预设配置枚举
        /// <summary>
        /// 日志系统预设配置
        /// </summary>
        public enum Preset
        {
            /// <summary>
            /// 开发环境 - 显示所有日志，启用文件输出和验证
            /// </summary>
            Development,
            
            /// <summary>
            /// 生产环境 - 只显示重要日志，启用文件输出，关闭验证
            /// </summary>
            Production,
            
            /// <summary>
            /// 调试模式 - 显示所有日志，启用性能监控和验证
            /// </summary>
            Debug,
            
            /// <summary>
            /// 性能测试 - 关闭所有日志输出
            /// </summary>
            Performance,
            
            /// <summary>
            /// 最小配置 - 只显示错误日志
            /// </summary>
            Minimal
        }
        #endregion

        #region 一键初始化方法
        /// <summary>
        /// 使用预设配置初始化日志系统
        /// </summary>
        /// <param name="preset">预设配置</param>
        /// <param name="customLogPath">自定义日志路径（可选）</param>
        public static void Initialize(Preset preset, string customLogPath = null)
        {
            var config = LoggingConfig.CreatePreset(preset);
            
            // 设置自定义路径
            if (!string.IsNullOrEmpty(customLogPath))
            {
                config.FileConfig.LogDirectory = customLogPath;
            }
            
            // 初始化日志管理器
            var loggingManager = LoggingManager.GetInstance();
            loggingManager.Initialize(config);
            
            // 初始化性能监控
            var performanceMonitor = PerformanceMonitor.GetInstance();
            performanceMonitor.EnablePerformanceLogging = config.PerformanceConfig.EnablePerformanceLogging;
            performanceMonitor.EnableAutoLogging = config.PerformanceConfig.EnableAutoLogging;
            performanceMonitor.FpsUpdateInterval = config.PerformanceConfig.FpsUpdateInterval;
        }

        /// <summary>
        /// 快速初始化开发环境配置
        /// </summary>
        public static void InitializeDevelopment()
        {
            Initialize(Preset.Development);
        }

        /// <summary>
        /// 快速初始化生产环境配置
        /// </summary>
        public static void InitializeProduction()
        {
            Initialize(Preset.Production);
        }

        /// <summary>
        /// 快速初始化调试模式配置
        /// </summary>
        public static void InitializeDebug()
        {
            Initialize(Preset.Debug);
        }
        #endregion


        #region 便捷访问方法
        /// <summary>
        /// 获取当前日志系统状态
        /// </summary>
        /// <returns>状态信息字符串</returns>
        public static string GetStatus()
        {
            var loggingManager = LoggingManager.GetInstance();
            var performanceMonitor = PerformanceMonitor.GetInstance();
            var config = loggingManager.Config;

            return $"日志系统状态:\n" +
                   $"- 日志级别: {config.Level}\n" +
                   $"- 日志模式: {config.Mode}\n" +
                   $"- 文件日志: {(config.FileConfig.EnableFileLogging ? "启用" : "禁用")}\n" +
                   $"- 时间戳: {(config.EnableTimestamp ? "启用" : "禁用")}\n" +
                   $"- 堆栈跟踪: {(config.EnableStackTrace ? "启用" : "禁用")}\n" +
                   $"- 性能监控: {(performanceMonitor.EnablePerformanceLogging ? "启用" : "禁用")}\n" +
                   $"- 框架验证: {(config.EnableFrameworkValidation ? "启用" : "禁用")}";
        }

        /// <summary>
        /// 重置日志系统到默认状态
        /// </summary>
        public static void Reset()
        {
            Initialize(Preset.Development);
        }

        /// <summary>
        /// 切换日志模式
        /// </summary>
        /// <param name="mode">新的日志模式</param>
        public static void SwitchMode(LogMode mode)
        {
            LoggingManager.GetInstance().SwitchMode(mode);
        }

        /// <summary>
        /// 设置日志级别
        /// </summary>
        /// <param name="level">新的日志级别</param>
        public static void SetLogLevel(LogLevel level)
        {
            LoggingManager.GetInstance().SetLogLevel(level);
        }

        /// <summary>
        /// 启用/禁用特定分类的日志
        /// </summary>
        /// <param name="category">日志分类</param>
        /// <param name="enabled">是否启用</param>
        public static void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            LoggingManager.GetInstance().SetCategoryEnabled(category, enabled);
        }
        #endregion

        #region 便捷日志方法
        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Info(string message)
        {
            FrameworkLogger.Info(message);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Warn(string message)
        {
            FrameworkLogger.Warn(message);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Error(string message)
        {
            FrameworkLogger.Error(message);
        }

        /// <summary>
        /// 记录调试日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Debug(string message)
        {
            FrameworkLogger.Debug(message);
        }
        #endregion
    }
}
