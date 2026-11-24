using System;

namespace AwithGameFrame.Core.Logging
{
    /// <summary>Writes framework diagnostics through the configured host sink.</summary>
    public static class FrameworkLogger
    {
        public static void Debug(string message, LogCategory category = LogCategory.Core) =>
            Write(LogLevel.Debug, category, message);

        public static void Info(string message, LogCategory category = LogCategory.Core) =>
            Write(LogLevel.Info, category, message);

        public static void Warn(string message, LogCategory category = LogCategory.Core) =>
            Write(LogLevel.Warn, category, message);

        public static void Error(string message, LogCategory category = LogCategory.Core) =>
            Write(LogLevel.Error, category, message);

        public static void LogException(string message, Exception exception) =>
            Error(message + Environment.NewLine + exception);

        private static void Write(LogLevel level, LogCategory category, string message) =>
            LoggingHost.Output(level, $"[{category}] {message}", null);
    }
}
