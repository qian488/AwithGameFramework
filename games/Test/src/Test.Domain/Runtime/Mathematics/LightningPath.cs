using System;
using System.Numerics;

namespace Test.Domain.Mathematics
{
    public static class LightningPath
    {
        public static Vector3[] Build(Vector3 start, Vector3 end, int segments, float jitter, int seed)
        {
            if (segments < 2 || segments > 64) throw new ArgumentOutOfRangeException(nameof(segments));
            if (!float.IsFinite(jitter) || jitter < 0f) throw new ArgumentOutOfRangeException(nameof(jitter));

            var points = new Vector3[segments + 1];
            points[0] = start;
            points[segments] = end;
            var direction = end - start;
            var side = Vector3.Cross(direction, Vector3.UnitZ);
            if (side.LengthSquared() < 0.000001f) side = Vector3.Cross(direction, Vector3.UnitX);
            if (side.LengthSquared() > 0f) side = Vector3.Normalize(side);
            var random = new Random(seed);
            for (var index = 1; index < segments; index++)
            {
                var fraction = index / (float)segments;
                var displacement = ((float)random.NextDouble() * 2f - 1f) * jitter;
                points[index] = Vector3.Lerp(start, end, fraction) + side * displacement;
            }
            return points;
        }
    }
}
