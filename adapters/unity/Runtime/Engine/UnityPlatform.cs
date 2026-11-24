using AwithGameFrame.Core.Logging;
using UnityEngine;

namespace AwithGameFrame.Unity
{
    /// <summary>Routes framework diagnostics to Unity's log.</summary>
    public static class UnityPlatform
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            LoggingHost.Output = WriteLog;
        }

        private static void WriteLog(LogLevel level, string message, object context)
        {
            var unityContext = context as Object;
            if (level >= LogLevel.Error) Debug.LogError(message, unityContext);
            else if (level == LogLevel.Warn) Debug.LogWarning(message, unityContext);
            else Debug.Log(message, unityContext);
        }
    }

}
