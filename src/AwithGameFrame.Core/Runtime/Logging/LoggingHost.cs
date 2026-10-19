using System;
using System.IO;

namespace AwithGameFrame.Core.Logging
{
    /// <summary>Host integration points. Configure before initializing framework logging.</summary>
    public static class LoggingHost
    {
        private static Action<LogLevel, string, object> _output = WriteToConsole;
        private static string _dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwithGameFrame");

        /// <summary>
        /// Receives the severity, formatted message and optional host context.
        /// Invoked synchronously on the caller's thread; the host handles any thread dispatch.
        /// Assign null to restore the default console output.
        /// </summary>
        public static Action<LogLevel, string, object> Output
        {
            get => _output;
            set => _output = value ?? WriteToConsole;
        }

        /// <summary>Default writable data directory; file logs use its Logs subdirectory.</summary>
        public static string DataPath
        {
            get => _dataPath;
            set => _dataPath = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A writable data directory is required.", nameof(value))
                : value;
        }

        private static void WriteToConsole(LogLevel level, string message, object context)
        {
            if (level >= LogLevel.Warn)
                Console.Error.WriteLine(message);
            else
                Console.WriteLine(message);
        }
    }
}
