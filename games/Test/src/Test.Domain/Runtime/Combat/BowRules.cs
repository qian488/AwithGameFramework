using System;
using System.Numerics;

namespace Test.Domain.Combat
{
    public static class BowRules
    {
        public const int QuiverCapacity = 6;
        public const float AimDistance = 24f;
        public const float FlightLifetime = 1.25f;
        private const float Speed = 23f;
        private static readonly Vector3 Gravity = new Vector3(0, -12f, 0);
        public static Strike Shot { get; } = new Strike(23, 1, 25, 18, 7, AimDistance, 0);

        public static ArrowFlight Launch(Vector3 origin, Vector3 target, Vector3 forward)
        {
            var offset = target - origin;
            double distance = CombatVectorMath.Length(offset);
            if (distance < .25f || CombatVectorMath.Dot(offset, forward) <= 0)
            {
                offset = forward * AimDistance;
                distance = AimDistance;
            }
            double flightTime = Math.Min(distance, AimDistance) / Speed;
            double scale = Speed / distance;
            var velocity = new Vector3((float)(offset.X * scale),
                (float)(offset.Y * scale - Gravity.Y * (.5d * flightTime)), (float)(offset.Z * scale));
            return new ArrowFlight(origin, velocity, 0);
        }

        public static ArrowFlight Advance(ArrowFlight arrow, float seconds) => new ArrowFlight(
            arrow.Position + arrow.Velocity * seconds + Gravity * (.5f * seconds * seconds),
            arrow.Velocity + Gravity * seconds, arrow.Age + seconds);
    }

    public readonly struct ArrowFlight
    {
        internal ArrowFlight(Vector3 position, Vector3 velocity, float age)
        {
            Position = position;
            Velocity = velocity;
            Age = age;
        }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Age { get; }
    }
}
