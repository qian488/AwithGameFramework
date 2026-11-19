using UnityEngine;

namespace Test.Client
{
    [DisallowMultipleComponent]
    public sealed class NativeParticleEffect : TestEffectBehaviour
    {
        [SerializeField] private bool playOnEnable = true;

        public override void Replay()
        {
            if (!isActiveAndEnabled || !UnityEngine.Application.isPlaying) return;
            foreach (var emitter in GetComponentsInChildren<ParticleSystem>())
            {
                emitter.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                emitter.Play(false);
            }
        }

        private void OnEnable()
        {
            if (playOnEnable) Replay();
        }
    }
}
