using System;

namespace AwithGameFrame.Core.Logging
{
    /// <summary>The process host's diagnostic output.</summary>
    public static class LoggingHost
    {
        private static Action<LogLevel, string, object> _output = WriteToConsole;

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

        private static void WriteToConsole(LogLevel level, string message, object context)
        {
            if (level >= LogLevel.Warn)
                Console.Error.WriteLine(message);
            else
                Console.WriteLine(message);
        }
    }
}
