using System;
using System.Numerics;

namespace Test.Domain.Mathematics
{
    public sealed class ProjectileFormula2D : ILaunchFormula2D
    {
        private readonly float launchAngleDegrees;
        private readonly float spreadDegrees;

        public ProjectileFormula2D(float launchAngleDegrees, float spreadDegrees)
        {
            this.launchAngleDegrees = launchAngleDegrees;
            this.spreadDegrees = spreadDegrees;
        }

        public LaunchSample2D Sample(float fraction, float speed)
        {
            var angleDegrees = launchAngleDegrees + (fraction - 0.5f) * spreadDegrees;
            return new LaunchSample2D(Vector2.Zero, LaunchVelocity(speed, angleDegrees));
        }

        public static Vector2 LaunchVelocity(float speed, float angleDegrees)
        {
            var angleRadians = angleDegrees * (MathF.PI / 180f);
            return speed * new Vector2(MathF.Cos(angleRadians), MathF.Sin(angleRadians));
        }
    }
}
