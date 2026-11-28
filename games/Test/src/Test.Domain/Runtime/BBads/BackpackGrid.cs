using System;
using System.Collections.Generic;
using System.Linq;

namespace Test.Domain.BBads
{
    public readonly struct GridCell : IEquatable<GridCell>
    {
        public GridCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(GridCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
    }

    public sealed class BackpackItemDefinition
    {
        public BackpackItemDefinition(
            string id,
            IEnumerable<GridCell> cells = null,
            bool isSlot = false,
            string mergeGroup = null,
            int mergeLevel = 1,
            string nextItemId = null)
        {
            GridCell[] footprint = cells?.Distinct().ToArray();
            Id = id;
            Cells = Array.AsReadOnly(footprint?.Length > 0 ? footprint : new[] { default(GridCell) });
            IsSlot = isSlot;
            MergeGroup = mergeGroup;
            MergeLevel = mergeLevel;
            NextItemId = nextItemId;
        }

        public string Id { get; }
        public IReadOnlyList<GridCell> Cells { get; }
        public bool IsSlot { get; }
        public string MergeGroup { get; }
        public int MergeLevel { get; }
        public string NextItemId { get; }
    }

    public sealed class BackpackPlacement
    {
        internal BackpackPlacement(int instanceId, BackpackItemDefinition definition, GridCell anchor)
        {
            InstanceId = instanceId;
            Definition = definition;
            Anchor = anchor;
        }

        public int InstanceId { get; }
        public BackpackItemDefinition Definition { get; }
        public GridCell Anchor { get; internal set; }
    }

    public sealed class BackpackGrid
    {
        static readonly GridCell[] Neighbors =
        {
            new GridCell(0, 1),
            new GridCell(0, -1),
            new GridCell(-1, 0),
            new GridCell(1, 0)
        };

        readonly Dictionary<int, BackpackPlacement> items = new Dictionary<int, BackpackPlacement>();
        readonly Dictionary<GridCell, int> occupied = new Dictionary<GridCell, int>();
        readonly HashSet<GridCell> unlocked = new HashSet<GridCell>();
        int nextInstanceId = 1;

        public BackpackGrid(int columns, int rows, IEnumerable<GridCell> initialUnlocked = null)
        {
            if (columns <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0)
                throw new ArgumentOutOfRangeException(nameof(rows));

            Columns = columns;
            Rows = rows;
            if (initialUnlocked == null)
            {
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                        unlocked.Add(new GridCell(x, y));
            }
            else
            {
                foreach (GridCell cell in initialUnlocked)
                    if (TryGetCell(cell, default, out _))
                        unlocked.Add(cell);
            }
        }

        public int Columns { get; }
        public int Rows { get; }
        public IReadOnlyCollection<GridCell> UnlockedCells => unlocked.ToArray();

        public bool IsCellUnlocked(GridCell cell) => unlocked.Contains(cell);

        public bool TryGetItem(int instanceId, out BackpackPlacement item) =>
            items.TryGetValue(instanceId, out item);

        public bool TryGetItemAt(GridCell cell, out BackpackPlacement item)
        {
            item = null;
            return occupied.TryGetValue(cell, out int id) && items.TryGetValue(id, out item);
        }

        public IReadOnlyList<BackpackPlacement> GetAdjacentItems(BackpackPlacement source)
        {
            if (source == null || !items.TryGetValue(source.InstanceId, out BackpackPlacement current) ||
                current != source)
                return Array.Empty<BackpackPlacement>();

            var adjacent = new List<BackpackPlacement>();
            var seen = new HashSet<int> { source.InstanceId };
            foreach (GridCell offset in source.Definition.Cells)
            {
                GridCell cell = Add(source.Anchor, offset);
                foreach (GridCell direction in Neighbors)
                    if (TryGetItemAt(Add(cell, direction), out BackpackPlacement item) &&
                        seen.Add(item.InstanceId))
                        adjacent.Add(item);
            }
            return adjacent;
        }

        public bool CanPlace(BackpackItemDefinition definition, GridCell anchor, params int[] ignoredInstanceIds)
        {
            if (definition == null)
                return false;

            foreach (GridCell offset in definition.Cells)
            {
                if (!TryGetCell(anchor, offset, out GridCell cell) || !unlocked.Contains(cell))
                    return false;
                if (occupied.TryGetValue(cell, out int id) &&
                    (ignoredInstanceIds == null || Array.IndexOf(ignoredInstanceIds, id) < 0))
                    return false;
            }
            return true;
        }

        public bool CanUnlockSlotAt(BackpackItemDefinition definition, GridCell anchor)
        {
            if (definition == null || !definition.IsSlot)
                return false;

            bool touchesUnlocked = false;
            foreach (GridCell offset in definition.Cells)
            {
                if (!TryGetCell(anchor, offset, out GridCell cell) || unlocked.Contains(cell))
                    return false;

                foreach (GridCell direction in Neighbors)
                    touchesUnlocked |= unlocked.Contains(Add(cell, direction));
            }
            return touchesUnlocked;
        }

        public bool TryUnlockSlotAt(
            BackpackItemDefinition definition, GridCell anchor, out IReadOnlyList<GridCell> unlockedNow)
        {
            unlockedNow = Array.Empty<GridCell>();
            if (!CanUnlockSlotAt(definition, anchor))
                return false;

            var cells = new List<GridCell>();
            foreach (GridCell offset in definition.Cells)
            {
                GridCell cell = Add(anchor, offset);
                unlocked.Add(cell);
                cells.Add(cell);
            }
            unlockedNow = cells;
            return true;
        }

        public bool TryPlaceNew(BackpackItemDefinition definition, GridCell anchor, out BackpackPlacement item)
        {
            item = null;
            if (!CanPlace(definition, anchor))
                return false;

            item = Place(definition, anchor);
            return true;
        }

        public bool TryMove(int instanceId, GridCell newAnchor)
        {
            if (!items.TryGetValue(instanceId, out BackpackPlacement item) ||
                !CanPlace(item.Definition, newAnchor, instanceId))
                return false;

            ClearOccupied(item);
            item.Anchor = newAnchor;
            MarkOccupied(item);
            return true;
        }

        public bool TryMerge(
            int sourceInstanceId,
            int targetInstanceId,
            Func<string, BackpackItemDefinition> resolveItem,
            out BackpackPlacement upgraded)
        {
            upgraded = null;
            if (sourceInstanceId == targetInstanceId ||
                !items.TryGetValue(sourceInstanceId, out BackpackPlacement source) ||
                !items.TryGetValue(targetInstanceId, out BackpackPlacement target) ||
                !TryResolveMerge(source.Definition, target.Definition, resolveItem, out BackpackItemDefinition next) ||
                !CanPlace(next, target.Anchor, sourceInstanceId, targetInstanceId))
                return false;

            Remove(sourceInstanceId);
            Remove(targetInstanceId);
            upgraded = Place(next, target.Anchor);
            return true;
        }

        public bool CanMergeNew(
            BackpackItemDefinition source,
            int targetInstanceId,
            Func<string, BackpackItemDefinition> resolveItem,
            out BackpackItemDefinition next)
        {
            next = null;
            return items.TryGetValue(targetInstanceId, out BackpackPlacement target) &&
                   TryResolveMerge(source, target.Definition, resolveItem, out next) &&
                   CanPlace(next, target.Anchor, targetInstanceId);
        }

        public bool TryMergeNew(
            BackpackItemDefinition source,
            int targetInstanceId,
            Func<string, BackpackItemDefinition> resolveItem,
            out BackpackPlacement upgraded)
        {
            upgraded = null;
            if (!CanMergeNew(source, targetInstanceId, resolveItem, out BackpackItemDefinition next))
                return false;

            GridCell anchor = items[targetInstanceId].Anchor;
            Remove(targetInstanceId);
            upgraded = Place(next, anchor);
            return true;
        }

        public bool Remove(int instanceId)
        {
            if (!items.TryGetValue(instanceId, out BackpackPlacement item))
                return false;

            ClearOccupied(item);
            return items.Remove(instanceId);
        }

        static bool TryResolveMerge(
            BackpackItemDefinition source,
            BackpackItemDefinition target,
            Func<string, BackpackItemDefinition> resolveItem,
            out BackpackItemDefinition next)
        {
            next = null;
            if (source == null || source.MergeLevel != target.MergeLevel ||
                string.IsNullOrWhiteSpace(source.MergeGroup) ||
                !string.Equals(source.MergeGroup, target.MergeGroup, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(target.NextItemId))
                return false;

            next = resolveItem?.Invoke(target.NextItemId);
            return next != null;
        }

        BackpackPlacement Place(BackpackItemDefinition definition, GridCell anchor)
        {
            var item = new BackpackPlacement(nextInstanceId++, definition, anchor);
            items.Add(item.InstanceId, item);
            MarkOccupied(item);
            return item;
        }

        void MarkOccupied(BackpackPlacement item)
        {
            foreach (GridCell offset in item.Definition.Cells)
                occupied.Add(Add(item.Anchor, offset), item.InstanceId);
        }

        void ClearOccupied(BackpackPlacement item)
        {
            foreach (GridCell offset in item.Definition.Cells)
                occupied.Remove(Add(item.Anchor, offset));
        }

        bool TryGetCell(GridCell anchor, GridCell offset, out GridCell cell)
        {
            long x = (long)anchor.X + offset.X;
            long y = (long)anchor.Y + offset.Y;
            cell = default;
            if (x < 0 || y < 0 || x >= Columns || y >= Rows)
                return false;

            cell = new GridCell((int)x, (int)y);
            return true;
        }

        static GridCell Add(GridCell cell, GridCell offset) =>
            new GridCell(cell.X + offset.X, cell.Y + offset.Y);
    }
}