using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Unity.Hosting;
using Test.Application;
using UnityEngine;

namespace Test.Client
{
    public sealed class TestBootstrap : GameHostBehaviour
    {
        [SerializeField] private ProceduralParticleEffect particleEffect;
        [SerializeField, Min(0.1f)] private float repeatSeconds = 4f;

        protected override void ConfigureHost(GameHost host)
        {
            host.AddModule(new TestModule());
            if (particleEffect != null)
                host.AddModule(new ParticleDemoModule(Mathf.Max(0.1f, repeatSeconds), particleEffect.Replay));
        }
    }
}
