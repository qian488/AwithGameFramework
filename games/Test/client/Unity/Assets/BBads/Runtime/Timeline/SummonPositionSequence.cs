using System.Collections.Generic;
using System.Linq;
using BBads.Data;
using Test.Application.BBads.Session;
using UnityEngine;
using Point = System.Numerics.Vector2;

namespace BBads.Timeline
{
    public sealed class SummonPositionSequence
    {
        readonly BBadsSummonPositions positions;

        public int Count => positions.Count;
        public int NextIndex => positions.NextIndex;

        public SummonPositionSequence(IEnumerable<SummonPosition> source)
        {
            positions = new BBadsSummonPositions(source?.Where(entry => entry != null)
                .Select(entry => new Point(entry.position.x, entry.position.y)));
        }

        public bool TryTakeNext(out Vector2 position)
        {
            bool found = positions.TryTakeNext(out Point point);
            position = new Vector2(point.X, point.Y);
            return found;
        }

        public void Reset() => positions.Reset();
    }
}
