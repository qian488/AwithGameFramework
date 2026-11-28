using System;
using System.Collections.Generic;
using UnityEngine;

namespace BBads.Data
{
    public enum ItemType
    {
        None,
        Cat,
        Fish,
        Character,
        Weapon,
        Slot
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public string id;
        public string displayName;
        public ItemType type;
        [Tooltip("Resources 下的牌底图路径，例如 BBads/Items/Bag_Slot2。")]
        public string frameResourcePath;
        public string pictureResourcePath;
        [Tooltip("SlotShapes 下的形状资产名，例如 SlotH2。")]
        public string shapeResourcePath;
        [Tooltip("占格坐标，以左下角锚点为 (0,0)。")]
        public List<Vector2Int> cells = new List<Vector2Int> { Vector2Int.zero };
        public string mergeGroup;
        [Min(1)] public int mergeLevel = 1;
        public string nextItemId;
        [Min(0f)] public float cooldown;
        public string summonUnitId;
        [Min(0f)] public float fishCost;
        [Min(0f)] public float activationAmount = 1f;

        public IReadOnlyList<Vector2Int> EffectiveCells =>
            cells != null && cells.Count > 0 ? cells : DefaultCell;

        static readonly IReadOnlyList<Vector2Int> DefaultCell =
            new[] { Vector2Int.zero };
    }

    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] List<ItemDefinition> entries = new List<ItemDefinition>();
        readonly Dictionary<string, ItemDefinition> lookup =
            new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ItemDefinition> Entries => entries;

        void OnEnable() => RebuildLookup();
        void OnValidate() => RebuildLookup();

        public bool TryGet(string id, out ItemDefinition definition)
        {
            if (lookup.Count != entries.Count)
                RebuildLookup();
            return lookup.TryGetValue(id ?? string.Empty, out definition);
        }

        public void ReplaceAll(IEnumerable<ItemDefinition> values)
        {
            entries = values != null ? new List<ItemDefinition>(values) : new List<ItemDefinition>();
            RebuildLookup();
        }

        public static ItemCatalog CreateRuntime(IEnumerable<ItemDefinition> values)
        {
            ItemCatalog catalog = CreateInstance<ItemCatalog>();
            catalog.ReplaceAll(values);
            return catalog;
        }

        void RebuildLookup()
        {
            lookup.Clear();
            if (entries == null)
                entries = new List<ItemDefinition>();
            foreach (ItemDefinition entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    continue;
                lookup[entry.id.Trim()] = entry;
            }
        }
    }
}
