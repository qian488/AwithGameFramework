using AwithGameFrame.Core.Logging;
using AwithGameFrame.Core.Config;
using UnityEngine;

namespace AwithGameFrame.Unity
{
    /// <summary>只有宿主负责帧驱动，内核不持有 MonoBehaviour。</summary>
    public sealed class UnityFrameDriver : MonoBehaviour
    {
        private void Update()
        {
            ConfigManager.GetInstance().Tick();
            PerformanceMonitor.GetInstance().Tick(Time.unscaledDeltaTime);
            LoggingManager.GetInstance().Update();
        }

        private void OnApplicationQuit()
        {
            LoggingManager.GetInstance().Shutdown();
        }
    }
}
