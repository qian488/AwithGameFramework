using UnityEngine;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]

namespace AwithGameFrame.Foundation.Providers
{
    internal static class DOTweenIntegration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            ProviderManager.RegisterDefaultAnimationProvider(() => new DOTweenProvider());
        }
    }
}
