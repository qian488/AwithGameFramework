using AwithGameFrame.Core;
using AwithGameFrame.Core.Logging;
using UnityEngine;

namespace AwithGameFrame.Unity
{
    /// <summary>将 Unity 的时间、可写目录和日志输出接入通用内核。</summary>
    public static class UnityPlatform
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            LoggingHost.DataPath = Application.persistentDataPath;
            LoggingHost.Output = WriteLog;
            TimeHelper.TimeSource = new UnityTimeSource();
        }

        private static void WriteLog(LogLevel level, string message, object context)
        {
            var unityContext = context as Object;
            if (level >= LogLevel.Error) Debug.LogError(message, unityContext);
            else if (level == LogLevel.Warn) Debug.LogWarning(message, unityContext);
            else Debug.Log(message, unityContext);
        }
    }

    internal sealed class UnityTimeSource : ITimeSource
    {
        public float GameTime => Time.time;
        public float UnscaledGameTime => Time.unscaledTime;
        public float DeltaTime => Time.deltaTime;
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
        public float FixedDeltaTime => Time.fixedDeltaTime;
        public float TimeScale { get => Time.timeScale; set => Time.timeScale = value; }
        public int TargetFrameRate { get => Application.targetFrameRate; set => Application.targetFrameRate = value; }
    }
}
