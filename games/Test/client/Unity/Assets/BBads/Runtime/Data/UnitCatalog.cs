using System;
using System.Collections.Generic;
using UnityEngine;

namespace BBads.Data
{
    public enum UnitCamp
    {
        Player,
        Enemy
    }

    [Serializable]
    public sealed class UnitDefinition
    {
        public string id;
        public string displayName;
        public UnitCamp camp;
        [Tooltip("Spine 初始水平翻转；用于适配资源本身的朝向。")]
        public bool flipX;
        [Tooltip("Resources 下的 Spine 目录路径，例如 characterSpine/NormalWarrior。目录内第一个 SkeletonDataAsset 会作为表现。")]
        public string spineResourcePath;
        public string pictureResourcePath;
        [Min(0f)] public float attack = 10f;
        [Min(1f)] public float maxHealth = 100f;
        [Min(0f)] public float moveSpeed = 2f;
        public bool isMelee = true;
        [Min(0f)] public float attackRange = 1f;
        [Min(0f)] public float aoeRange;
        [Min(0f)] public float searchRange = 6f;
        [Min(0.01f)] public float attackInterval = 1f;
        [Tooltip("Spine 相对通用单位预制体的显示缩放。")]
        [Min(0.01f)] public float visualScale = 1f;
        [Tooltip("攻击判定半径；参与攻击距离和范围伤害命中判定。")]
        [Min(0.01f)] public float attackSize = 0.18f;
        [Tooltip("视觉身体半径；只用于出生避让、移动分离和交战站位。")]
        [Min(0.01f)] public float bodyRadius = 0.22f;
        [Tooltip("单位血条相对角色根节点的纵向偏移。")]
        public float hpBarOffset = 0.45f;
        [Tooltip("attack_profile.csv 中的攻击表现配置 ID。")]
        public string attackProfileId;
        public string hitEffectResourcePath;
        [Min(0.01f)] public float hitEffectScale = 1f;
        [Tooltip("Resources 下的死亡特效目录路径。")]
        public string deathEffectResourcePath;
        [Min(0.01f)] public float deathEffectScale = 1f;

        [NonSerialized] AttackProfileDefinition attackProfile;
        public AttackProfileDefinition AttackProfile => attackProfile;

        internal void SetAttackProfile(AttackProfileDefinition value)
        {
            attackProfile = value;
        }
    }

    public sealed class UnitCatalog : ScriptableObject
    {
        [SerializeField] List<UnitDefinition> entries = new List<UnitDefinition>();
        readonly Dictionary<string, UnitDefinition> lookup =
            new Dictionary<string, UnitDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<UnitDefinition> Entries => entries;

        void OnEnable() => RebuildLookup();
        void OnValidate() => RebuildLookup();

        public bool TryGet(string id, out UnitDefinition definition)
        {
            if (lookup.Count != entries.Count)
                RebuildLookup();
            return lookup.TryGetValue(id ?? string.Empty, out definition);
        }

        public void ReplaceAll(IEnumerable<UnitDefinition> values)
        {
            entries = values != null ? new List<UnitDefinition>(values) : new List<UnitDefinition>();
            RebuildLookup();
        }

        public static UnitCatalog CreateRuntime(IEnumerable<UnitDefinition> values)
        {
            UnitCatalog catalog = CreateInstance<UnitCatalog>();
            catalog.ReplaceAll(values);
            return catalog;
        }

        void RebuildLookup()
        {
            lookup.Clear();
            if (entries == null)
                entries = new List<UnitDefinition>();
            foreach (UnitDefinition entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    continue;
                lookup[entry.id.Trim()] = entry;
            }
        }
    }
}
