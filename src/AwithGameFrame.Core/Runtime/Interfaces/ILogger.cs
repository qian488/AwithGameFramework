using System;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Core
{
    /// <summary>
    /// 日志系统接口
    /// 为不同的日志实现提供统一抽象
    /// </summary>
    public interface ILogger
    {
        void Log(LogLevel level, string category, string message);
        void LogException(LogLevel level, string category, string message, Exception exception);
        void SetLogLevel(LogLevel level);
        LogLevel GetLogLevel();
    }
}
