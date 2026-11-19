using System;
using System.Numerics;

namespace Test.Domain.Mathematics
{
    public sealed class SpiralFormula2D : ILaunchFormula2D
    {
        private readonly float radius;
        private readonly float turns;

        public SpiralFormula2D(float radius, float turns)
        {
            this.radius = radius;
            this.turns = turns;
        }

        public LaunchSample2D Sample(float fraction, float speed)
        {
            var angle = 2f * MathF.PI * turns * fraction;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            return new LaunchSample2D(direction * (radius * fraction), direction * speed);
        }
    }
}
