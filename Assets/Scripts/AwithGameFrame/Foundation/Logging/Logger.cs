using UnityEngine;
using AwithGameFrame.Core;

namespace AwithGameFrame.Foundation.Logging
{
    /// <summary>
    /// 静态日志工具类 - 提供便捷的日志记录方法
    /// </summary>
    public static class FrameworkLogger
    {
        #region 框架验证日志方法
        /// <summary>
        /// 验证并记录Info级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="validationType">验证类型</param>
        /// <param name="context">上下文对象</param>
        public static void InfoWithValidation(string message, ValidationType validationType, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Core, message, context);
            LoggingManager.GetInstance().ValidateFrameworkUsage(validationType, message, context);
        }

        /// <summary>
        /// 验证并记录Warning级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="validationType">验证类型</param>
        /// <param name="context">上下文对象</param>
        public static void WarnWithValidation(string message, ValidationType validationType, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Warn, LogCategory.Core, message, context);
            LoggingManager.GetInstance().ValidateFrameworkUsage(validationType, message, context);
        }

        /// <summary>
        /// 验证并记录Error级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="validationType">验证类型</param>
        /// <param name="context">上下文对象</param>
        public static void ErrorWithValidation(string message, ValidationType validationType, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Error, LogCategory.Core, message, context);
            LoggingManager.GetInstance().ValidateFrameworkUsage(validationType, message, context);
        }
        #endregion

        #region 基础日志方法
        /// <summary>
        /// 记录Trace级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Trace(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Trace, LogCategory.Core, message, context);
        }
        
        /// <summary>
        /// 记录Debug级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Debug(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Debug, LogCategory.Core, message, context);
        }
        
        /// <summary>
        /// 记录Info级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Info(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Core, message, context);
        }
        
        /// <summary>
        /// 记录Warn级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Warn(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Warn, LogCategory.Core, message, context);
        }
        
        /// <summary>
        /// 记录Error级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Error(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Error, LogCategory.Core, message, context);
        }
        
        /// <summary>
        /// 记录Fatal级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void Fatal(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Fatal, LogCategory.Core, message, context);
        }
        #endregion
        
        #region 分类日志方法
        /// <summary>
        /// 记录UI相关日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogUI(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.UI, message, context);
        }
        
        /// <summary>
        /// 记录音频相关日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogAudio(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Audio, message, context);
        }
        
        /// <summary>
        /// 记录输入相关日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogInput(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Input, message, context);
        }
        
        /// <summary>
        /// 记录网络相关日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogNetwork(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Network, message, context);
        }
        
        /// <summary>
        /// 记录性能相关日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogPerformance(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Performance, message, context);
        }
        #endregion
        
        #region 异常日志方法
        /// <summary>
        /// 记录异常日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public static void LogException(string message, System.Exception exception, object context = null)
        {
            LoggingManager.GetInstance().LogException(LogLevel.Error, LogCategory.Core, message, exception, context);
        }
        
        /// <summary>
        /// 记录UI异常日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public static void LogUIException(string message, System.Exception exception, object context = null)
        {
            LoggingManager.GetInstance().LogException(LogLevel.Error, LogCategory.UI, message, exception, context);
        }
        
        /// <summary>
        /// 记录网络异常日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public static void LogNetworkException(string message, System.Exception exception, object context = null)
        {
            LoggingManager.GetInstance().LogException(LogLevel.Error, LogCategory.Network, message, exception, context);
        }
        #endregion
        
        #region 格式化日志方法
        /// <summary>
        /// 记录格式化日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="format">格式化字符串</param>
        /// <param name="args">参数</param>
        public static void LogFormat(LogLevel level, LogCategory category, string format, params object[] args)
        {
            string message = string.Format(format, args);
            LoggingManager.GetInstance().Log(level, category, message);
        }
        
        /// <summary>
        /// 记录Info级别格式化日志
        /// </summary>
        /// <param name="format">格式化字符串</param>
        /// <param name="args">参数</param>
        public static void LogInfoFormat(string format, params object[] args)
        {
            LogFormat(LogLevel.Info, LogCategory.Core, format, args);
        }
        
        /// <summary>
        /// 记录Error级别格式化日志
        /// </summary>
        /// <param name="format">格式化字符串</param>
        /// <param name="args">参数</param>
        public static void LogErrorFormat(string format, params object[] args)
        {
            LogFormat(LogLevel.Error, LogCategory.Core, format, args);
        }
        #endregion

        #region 简化日志方法 - 自动获取调用者信息
        /// <summary>
        /// 记录信息日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Info(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Core, message, caller);
        }

        /// <summary>
        /// 记录警告日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Warn(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Warn, LogCategory.Core, message, caller);
        }

        /// <summary>
        /// 记录错误日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Error(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Error, LogCategory.Core, message, caller);
        }

        /// <summary>
        /// 记录调试日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Debug(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Debug, LogCategory.Core, message, caller);
        }

        /// <summary>
        /// 记录UI相关日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogUI(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.UI, message, caller);
        }

        /// <summary>
        /// 记录音频相关日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogAudio(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Audio, message, caller);
        }

        /// <summary>
        /// 记录输入相关日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogInput(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Input, message, caller);
        }

        /// <summary>
        /// 记录资源相关日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogResource(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Resource, message, caller);
        }

        /// <summary>
        /// 记录性能相关日志（自动获取调用者信息）
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogPerformance(string message)
        {
            var caller = GetCallerInfo();
            LoggingManager.GetInstance().Log(LogLevel.Info, LogCategory.Performance, message, caller);
        }

        /// <summary>
        /// 获取调用者信息
        /// </summary>
        /// <returns>调用者对象</returns>
        private static object GetCallerInfo()
        {
            try
            {
                var stackTrace = new System.Diagnostics.StackTrace(2, false); // 跳过当前方法和调用方法
                var frame = stackTrace.GetFrame(0);
                if (frame != null)
                {
                    var method = frame.GetMethod();
                    if (method != null && method.DeclaringType != null)
                    {
                        // 尝试获取调用者的实例
                        var declaringType = method.DeclaringType;
                        
                        // 如果是MonoBehaviour子类，尝试通过FindObjectOfType获取实例
                        if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(declaringType))
                        {
                            var instance = UnityEngine.Object.FindObjectOfType(declaringType);
                            if (instance != null)
                            {
                                return instance;
                            }
                        }
                        
                        // 如果是单例模式，尝试通过GetInstance获取
                        var getInstanceMethod = declaringType.GetMethod("GetInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (getInstanceMethod != null)
                        {
                            try
                            {
                                return getInstanceMethod.Invoke(null, null);
                            }
                            catch
                            {
                                // 如果获取实例失败，返回类型信息
                            }
                        }
                        
                        // 返回类型信息作为fallback
                        return declaringType;
                    }
                }
            }
            catch
            {
                // 如果获取调用者信息失败，返回null
            }
            
            return null;
        }
        #endregion
    }
}
