using Test.Domain.Mathematics;
using UnityEngine;

namespace Test.Client
{
    [CreateAssetMenu(menuName = "Test/Particle Formula/Projectile")]
    public sealed class ProjectileLaunchProfile : ParticleLaunchProfile
    {
        [SerializeField, Range(-180f, 180f)] private float launchAngle = 55f;
        [SerializeField, Range(0f, 180f)] private float launchSpread = 32f;

        public override ILaunchFormula2D CreateFormula() => new ProjectileFormula2D(launchAngle, launchSpread);
    }
}
