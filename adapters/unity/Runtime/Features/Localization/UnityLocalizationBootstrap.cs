using AwithGameFrame.Core.Config;
using UnityEngine;

namespace AwithGameFrame.Extensions.Localization
{
    /// <summary>Unity 的配置与 Resources 加载方式只在适配包中指定。</summary>
    public static class UnityLocalizationBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var manager = LocalizationManager.GetInstance();
            manager.DefaultLanguage = ConfigAPI.GetDefaultLanguage();
            manager.Source = new UnityLocalizationSource();
        }
    }
}
