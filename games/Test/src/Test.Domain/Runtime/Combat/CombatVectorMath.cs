using System;
using System.Numerics;

namespace Test.Domain.Combat
{
    // Use scalar arithmetic and round once when returning to the combat state.
    internal static class CombatVectorMath
    {
        internal static double Dot(Vector3 a, Vector3 b) => ((double)a.X * b.X + (double)a.Y * b.Y) + (double)a.Z * b.Z;
        internal static double LengthSquared(Vector3 value) => Dot(value, value);
        internal static double Length(Vector3 value) => Math.Sqrt(LengthSquared(value));
        internal static float Distance(Vector3 a, Vector3 b) => (float)Length(a - b);
        private static Vector3 Divide(Vector3 value, double divisor) =>
            new Vector3((float)(value.X / divisor), (float)(value.Y / divisor), (float)(value.Z / divisor));
        internal static Vector3 Normalize(Vector3 value) => Divide(value, Length(value));
    }
}
