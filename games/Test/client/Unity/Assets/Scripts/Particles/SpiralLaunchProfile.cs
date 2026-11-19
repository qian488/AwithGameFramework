using Test.Domain.Mathematics;
using UnityEngine;

namespace Test.Client
{
    [CreateAssetMenu(menuName = "Test/Particle Formula/Spiral")]
    public sealed class SpiralLaunchProfile : ParticleLaunchProfile
    {
        [SerializeField, Min(0.01f)] private float radius = 1f;
        [SerializeField, Range(0.25f, 8f)] private float turns = 2f;

        public override ILaunchFormula2D CreateFormula() => new SpiralFormula2D(Mathf.Max(0.01f, radius), turns);
    }
}
