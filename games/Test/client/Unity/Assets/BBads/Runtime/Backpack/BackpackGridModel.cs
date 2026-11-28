using System.Linq;
using System.Collections.Generic;
using BBads.Data;
using UnityEngine;
using Test.Domain.BBads;

namespace BBads.Backpack
{
    public sealed class BackpackItemInstance
    {
        public int InstanceId { get; internal set; }
        public ItemDefinition Definition { get; internal set; }
        public Vector2Int Anchor { get; internal set; }
        public float CooldownRemaining { get; internal set; }
        public float ActivationProgress { get; internal set; }
    }

    public sealed class BackpackGridModel
    {
        readonly Dictionary<int, BackpackItemInstance> items =
            new Dictionary<int, BackpackItemInstance>();
        readonly BackpackGrid grid;

        public int Columns => grid.Columns;
        public int Rows => grid.Rows;
        public IReadOnlyDictionary<int, BackpackItemInstance> Items => items;
        public IReadOnlyCollection<Vector2Int> UnlockedCells => grid.UnlockedCells.Select(ToUnity).ToArray();

        public BackpackGridModel(int columns, int rows, IEnumerable<Vector2Int> initialUnlocked = null)
        {
            grid = new BackpackGrid(columns, rows, initialUnlocked?.Select(ToDomain));
        }

        public bool IsCellUnlocked(Vector2Int cell) => grid.IsCellUnlocked(ToDomain(cell));

        public bool TryGetItemAt(Vector2Int cell, out BackpackItemInstance item)
        {
            item = null;
            return grid.TryGetItemAt(ToDomain(cell), out BackpackPlacement placement) &&
                   items.TryGetValue(placement.InstanceId, out item);
        }

        public bool TryGetItem(int instanceId, out BackpackItemInstance item) =>
            items.TryGetValue(instanceId, out item);

        public IEnumerable<BackpackItemInstance> GetAdjacentItems(BackpackItemInstance source)
        {
            if (source != null && grid.TryGetItem(source.InstanceId, out BackpackPlacement placement))
                foreach (BackpackPlacement neighbor in grid.GetAdjacentItems(placement))
                    yield return items[neighbor.InstanceId];
        }

        public bool CanPlace(ItemDefinition definition, Vector2Int anchor, params int[] ignoredInstanceIds) =>
            grid.CanPlace(ToDomain(definition), ToDomain(anchor), ignoredInstanceIds);

        public bool TryUnlockSlotAt(ItemDefinition definition, Vector2Int anchor, out List<Vector2Int> unlockedNow)
        {
            bool success = grid.TryUnlockSlotAt(ToDomain(definition), ToDomain(anchor), out var cells);
            unlockedNow = success ? cells.Select(ToUnity).ToList() : null;
            return success;
        }

        public bool CanUnlockSlotAt(ItemDefinition definition, Vector2Int anchor) =>
            grid.CanUnlockSlotAt(ToDomain(definition), ToDomain(anchor));

        public bool TryPlaceNew(ItemDefinition definition, Vector2Int anchor, out BackpackItemInstance instance)
        {
            instance = null;
            if (!grid.TryPlaceNew(ToDomain(definition), ToDomain(anchor), out BackpackPlacement placement))
                return false;
            instance = Add(placement, definition);
            return true;
        }

        public bool TryMove(int instanceId, Vector2Int newAnchor)
        {
            if (!grid.TryMove(instanceId, ToDomain(newAnchor)))
                return false;
            items[instanceId].Anchor = newAnchor;
            return true;
        }

        public bool TryMerge(
            int sourceInstanceId, int targetInstanceId, ItemCatalog catalog, out BackpackItemInstance upgraded)
        {
            upgraded = null;
            if (!grid.TryMerge(sourceInstanceId, targetInstanceId, id => ToDomain(Resolve(catalog, id)),
                    out BackpackPlacement placement))
                return false;
            items.Remove(sourceInstanceId);
            items.Remove(targetInstanceId);
            upgraded = Add(placement, Resolve(catalog, placement.Definition.Id));
            return true;
        }

        public bool TryMergeNew(
            ItemDefinition sourceDefinition, int targetInstanceId, ItemCatalog catalog,
            out BackpackItemInstance upgraded)
        {
            upgraded = null;
            if (!grid.TryMergeNew(ToDomain(sourceDefinition), targetInstanceId, id => ToDomain(Resolve(catalog, id)),
                    out BackpackPlacement placement))
                return false;
            items.Remove(targetInstanceId);
            upgraded = Add(placement, Resolve(catalog, placement.Definition.Id));
            return true;
        }

        public bool CanMergeNew(
            ItemDefinition sourceDefinition, int targetInstanceId, ItemCatalog catalog, out ItemDefinition next)
        {
            bool success = grid.CanMergeNew(ToDomain(sourceDefinition), targetInstanceId,
                id => ToDomain(Resolve(catalog, id)), out BackpackItemDefinition nextDefinition);
            next = success ? Resolve(catalog, nextDefinition.Id) : null;
            return success;
        }

        public bool Remove(int instanceId) => grid.Remove(instanceId) && items.Remove(instanceId);

        public bool TryFindAndMerge(ItemCatalog catalog, out BackpackItemInstance upgraded)
        {
            upgraded = null;
            var snapshot = new List<BackpackItemInstance>(items.Values);
            for (int i = 0; i < snapshot.Count; i++)
                for (int j = i + 1; j < snapshot.Count; j++)
                    if (items.ContainsKey(snapshot[i].InstanceId) && items.ContainsKey(snapshot[j].InstanceId) && TryMerge(snapshot[i].InstanceId, snapshot[j].InstanceId, catalog, out upgraded)) return true;
            return false;
        }

        public void Tick(float deltaTime, List<BackpackItemInstance> readyBuffer)
        {
            if (deltaTime <= 0f || readyBuffer == null)
                return;
            foreach (BackpackItemInstance item in items.Values)
            {
                float cooldown = item.Definition != null ? GetCycle(item.Definition) : 0f;
                if (cooldown <= 0f)
                    continue;
                item.CooldownRemaining -= deltaTime;
                while (item.CooldownRemaining <= 0f)
                {
                    readyBuffer.Add(item);
                    item.CooldownRemaining += cooldown;
                }
            }
        }

        BackpackItemInstance Add(BackpackPlacement placement, ItemDefinition definition)
        {
            var instance = new BackpackItemInstance
            {
                InstanceId = placement.InstanceId,
                Definition = definition,
                Anchor = ToUnity(placement.Anchor),
                CooldownRemaining = GetCycle(definition)
            };
            items.Add(instance.InstanceId, instance);
            return instance;
        }

        static ItemDefinition Resolve(ItemCatalog catalog, string id) =>
            catalog != null && catalog.TryGet(id, out ItemDefinition definition) ? definition : null;

        static GridCell ToDomain(Vector2Int cell) => new GridCell(cell.x, cell.y);
        static Vector2Int ToUnity(GridCell cell) => new Vector2Int(cell.X, cell.Y);

        static BackpackItemDefinition ToDomain(ItemDefinition definition) =>
            definition == null ? null : new BackpackItemDefinition(definition.id,
                definition.EffectiveCells.Select(ToDomain), definition.type == ItemType.Slot,
                definition.mergeGroup, definition.mergeLevel, definition.nextItemId);

        static float GetCycle(ItemDefinition definition) => Mathf.Max(0f, definition.cooldown);
    }
}
