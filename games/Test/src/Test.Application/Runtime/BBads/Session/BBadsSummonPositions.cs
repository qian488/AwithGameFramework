using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Test.Application.BBads.Session
{
    public sealed class BBadsSummonPositions
    {
        public const int MaxPositions = 999;

        readonly Vector2[] positions;

        // Supply positions in the runtime scenario's configured summon order.
        public BBadsSummonPositions(IEnumerable<Vector2> positions)
        {
            this.positions = positions?.Take(MaxPositions).ToArray() ?? System.Array.Empty<Vector2>();
        }

        public int Count => positions.Length;
        public int NextIndex { get; private set; }

        public bool TryTakeNext(out Vector2 position)
        {
            if (positions.Length == 0)
            {
                position = default;
                return false;
            }

            position = positions[NextIndex];
            NextIndex = (NextIndex + 1) % positions.Length;
            return true;
        }

        public void Reset() => NextIndex = 0;
    }
}