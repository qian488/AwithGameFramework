using UnityEngine;

namespace AwithGameFrame.Unity
{
    /// <summary>Only installing the compatibility package enables the legacy static frame driver.</summary>
    internal static class UnityCompatibilityBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var driver = new GameObject("AwithGameFrame.Unity.Compatibility") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(driver);
            driver.AddComponent<UnityFrameDriver>();
        }
    }
}
