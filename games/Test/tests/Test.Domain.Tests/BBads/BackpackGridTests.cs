using System.Collections.Generic;
using NUnit.Framework;
using Test.Domain.BBads;

namespace Test.Domain.Tests.BBads
{
    [TestFixture]
    public sealed class BackpackGridTests
    {
        [Test]
        public void IrregularFootprintChecksEveryCellAndLeavesHolesAvailable()
        {
            var offsets = new List<GridCell> { Cell(0, 0), Cell(1, 0), Cell(0, 1) };
            var item = new BackpackItemDefinition("corner", offsets);
            offsets.Clear();
            var grid = new BackpackGrid(3, 3, new[]
            {
                Cell(0, 0), Cell(1, 0), Cell(0, 1), Cell(1, 1), Cell(9, 9)
            });

            Assert.That(grid.CanPlace(item, Cell(-1, 0)), Is.False);
            Assert.That(grid.CanPlace(item, Cell(1, 0)), Is.False);
            Assert.That(grid.IsCellUnlocked(Cell(9, 9)), Is.False);
            Assert.That(grid.TryPlaceNew(item, Cell(0, 0), out BackpackPlacement placed), Is.True);
            Assert.That(grid.TryGetItemAt(Cell(0, 1), out BackpackPlacement owner), Is.True);
            Assert.That(owner, Is.SameAs(placed));
            Assert.That(grid.TryPlaceNew(new BackpackItemDefinition("single"), Cell(1, 0), out _), Is.False);
            Assert.That(grid.TryPlaceNew(new BackpackItemDefinition("single"), Cell(1, 1), out _), Is.True);
        }

        [Test]
        public void MoveReusesOwnCellsAndFailedMoveLeavesOccupancyIntact()
        {
            var grid = new BackpackGrid(5, 1);
            var pair = new BackpackItemDefinition("pair", new[] { Cell(0, 0), Cell(1, 0) });
            Assert.That(grid.TryPlaceNew(pair, Cell(0, 0), out BackpackPlacement placed), Is.True);
            Assert.That(grid.TryPlaceNew(new BackpackItemDefinition("single"), Cell(3, 0), out _), Is.True);

            Assert.That(grid.TryMove(placed.InstanceId, Cell(1, 0)), Is.True);
            Assert.That(grid.TryMove(placed.InstanceId, Cell(2, 0)), Is.False);
            Assert.That(placed.Anchor, Is.EqualTo(Cell(1, 0)));
            Assert.That(grid.TryGetItemAt(Cell(0, 0), out _), Is.False);
            Assert.That(grid.TryGetItemAt(Cell(2, 0), out BackpackPlacement occupied), Is.True);
            Assert.That(occupied, Is.SameAs(placed));
            Assert.That(grid.Remove(placed.InstanceId), Is.True);
            Assert.That(grid.Remove(placed.InstanceId), Is.False);
            Assert.That(grid.TryGetItemAt(Cell(1, 0), out _), Is.False);
            Assert.That(grid.TryGetItemAt(Cell(2, 0), out _), Is.False);
        }

        [Test]
        public void SlotUnlockRequiresLockedFootprintAndAnOrthogonalConnection()
        {
            var grid = new BackpackGrid(4, 4, new[] { Cell(0, 0) });
            var slot = new BackpackItemDefinition("slot", new[] { Cell(0, 0), Cell(1, 0) }, isSlot: true);

            Assert.That(grid.TryUnlockSlotAt(slot, Cell(1, 1), out _), Is.False);
            Assert.That(grid.TryUnlockSlotAt(slot, Cell(0, 0), out _), Is.False);
            Assert.That(grid.TryUnlockSlotAt(new BackpackItemDefinition("single"), Cell(1, 0), out _), Is.False);
            Assert.That(grid.IsCellUnlocked(Cell(1, 0)), Is.False);

            Assert.That(grid.TryUnlockSlotAt(slot, Cell(1, 0), out IReadOnlyList<GridCell> unlocked), Is.True);
            Assert.That(unlocked, Is.EqualTo(new[] { Cell(1, 0), Cell(2, 0) }));
            Assert.That(grid.TryUnlockSlotAt(slot, Cell(1, 0), out _), Is.False);
            Assert.That(grid.TryPlaceNew(new BackpackItemDefinition("single"), Cell(2, 0), out _), Is.True);
        }

        [Test]
        public void AdjacencyFindsEachNeighborOnceAcrossSeveralSharedEdges()
        {
            var grid = new BackpackGrid(4, 3);
            var pair = new BackpackItemDefinition("pair", new[] { Cell(0, 0), Cell(1, 0) });
            Assert.That(grid.TryPlaceNew(pair, Cell(0, 0), out BackpackPlacement fish), Is.True);
            Assert.That(grid.TryPlaceNew(pair, Cell(0, 1), out BackpackPlacement cat), Is.True);
            Assert.That(grid.TryPlaceNew(new BackpackItemDefinition("diagonal"), Cell(2, 1), out _), Is.True);

            Assert.That(grid.GetAdjacentItems(fish), Is.EqualTo(new[] { cat }));
        }

        [Test]
        public void GridMergeUsesTargetUpgradeAndCommitsOnlyWhenItsFootprintFits()
        {
            var grid = new BackpackGrid(4, 1);
            var source = new BackpackItemDefinition("source", mergeGroup: "CAT", nextItemId: "other");
            var target = new BackpackItemDefinition("target", mergeGroup: "cat", nextItemId: "upgraded");
            var next = new BackpackItemDefinition("upgraded", new[] { Cell(0, 0), Cell(1, 0), Cell(2, 0) });
            var blocker = new BackpackItemDefinition("blocker");
            BackpackItemDefinition Resolve(string id) => id == next.Id ? next : null;
            Assert.That(grid.TryPlaceNew(target, Cell(0, 0), out BackpackPlacement right), Is.True);
            Assert.That(grid.TryPlaceNew(source, Cell(1, 0), out BackpackPlacement left), Is.True);
            Assert.That(grid.TryPlaceNew(blocker, Cell(2, 0), out BackpackPlacement blocked), Is.True);

            Assert.That(grid.TryMerge(left.InstanceId, left.InstanceId, Resolve, out _), Is.False);
            Assert.That(grid.TryMerge(left.InstanceId, right.InstanceId, Resolve, out _), Is.False);
            Assert.That(grid.TryGetItemAt(Cell(0, 0), out BackpackPlacement stillTarget), Is.True);
            Assert.That(grid.TryGetItemAt(Cell(1, 0), out BackpackPlacement stillSource), Is.True);
            Assert.That(stillTarget, Is.SameAs(right));
            Assert.That(stillSource, Is.SameAs(left));

            Assert.That(grid.Remove(blocked.InstanceId), Is.True);
            Assert.That(grid.TryMerge(left.InstanceId, right.InstanceId, Resolve, out BackpackPlacement upgraded), Is.True);
            Assert.That(upgraded.Definition, Is.SameAs(next));
            Assert.That(upgraded.Anchor, Is.EqualTo(right.Anchor));
            Assert.That(grid.TryGetItemAt(Cell(2, 0), out BackpackPlacement owner), Is.True);
            Assert.That(owner, Is.SameAs(upgraded));
            Assert.That(grid.TryMerge(left.InstanceId, right.InstanceId, Resolve, out _), Is.False);
        }

        [Test]
        public void TrayMergePreviewMatchesCommitAndChecksMergeLevel()
        {
            var grid = new BackpackGrid(2, 1);
            var target = new BackpackItemDefinition("cat", mergeGroup: "cat", nextItemId: "upgraded");
            var wrongLevel = new BackpackItemDefinition("cat3", mergeGroup: "cat", mergeLevel: 3);
            var next = new BackpackItemDefinition("upgraded", mergeGroup: "cat", mergeLevel: 2);
            BackpackItemDefinition Resolve(string id) => id == next.Id ? next : null;
            Assert.That(grid.TryPlaceNew(target, Cell(1, 0), out BackpackPlacement placed), Is.True);

            Assert.That(grid.CanMergeNew(wrongLevel, placed.InstanceId, Resolve, out _), Is.False);
            Assert.That(grid.CanMergeNew(target, placed.InstanceId, Resolve, out BackpackItemDefinition preview), Is.True);
            Assert.That(preview, Is.SameAs(next));
            Assert.That(grid.TryGetItem(placed.InstanceId, out _), Is.True);

            Assert.That(grid.TryMergeNew(target, placed.InstanceId, Resolve, out BackpackPlacement upgraded), Is.True);
            Assert.That(upgraded.Definition, Is.SameAs(preview));
            Assert.That(upgraded.Anchor, Is.EqualTo(placed.Anchor));
            Assert.That(grid.TryMergeNew(target, placed.InstanceId, Resolve, out _), Is.False);
        }

        static GridCell Cell(int x, int y) => new GridCell(x, y);
    }
}