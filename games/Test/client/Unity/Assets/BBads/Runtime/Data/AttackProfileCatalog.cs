using System;
using System.Collections.Generic;
using UnityEngine;

namespace BBads.Data
{
    public enum ProjectileTrajectory
    {
        Homing,
        Straight,
        Arc
    }

    [Serializable]
    public sealed class AttackProfileDefinition
    {
        public string id;
        public string attackAnimation = "Attack";
        [Min(0f)] public float hitDelay = 0.3f;

        [Header("Projectile")]
        public string projectileResourcePath;
        [Min(0.1f)] public float projectileSpeed = 6f;
        [Min(0.01f)] public float projectileScale = 1f;
        public Vector2 projectileSpawnOffset = new Vector2(0f, 0.15f);
        public ProjectileTrajectory projectileTrajectory = ProjectileTrajectory.Homing;
        [Min(0f)] public float projectileArcHeight;
        public float projectileRotationOffset;

        [Header("Cast Effect")]
        public string castEffectResourcePath;
        [Min(0.01f)] public float castEffectScale = 1f;
        public Vector2 castEffectOffset;

        [Header("Hit Effect")]
        public string hitEffectResourcePath;
        [Min(0.01f)] public float hitEffectScale = 1f;
        public Vector2 hitEffectOffset;
        public bool randomHitRotation = true;
    }

    public sealed class AttackProfileCatalog : ScriptableObject
    {
        [SerializeField] List<AttackProfileDefinition> entries = new List<AttackProfileDefinition>();
        readonly Dictionary<string, AttackProfileDefinition> lookup =
            new Dictionary<string, AttackProfileDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<AttackProfileDefinition> Entries => entries;

        void OnEnable() => RebuildLookup();
        void OnValidate() => RebuildLookup();

        public bool TryGet(string id, out AttackProfileDefinition definition)
        {
            if (lookup.Count != entries.Count)
                RebuildLookup();
            return lookup.TryGetValue(id ?? string.Empty, out definition);
        }

        public void ReplaceAll(IEnumerable<AttackProfileDefinition> values)
        {
            entries = values != null
                ? new List<AttackProfileDefinition>(values)
                : new List<AttackProfileDefinition>();
            RebuildLookup();
        }

        public static AttackProfileCatalog CreateRuntime(IEnumerable<AttackProfileDefinition> values)
        {
            AttackProfileCatalog catalog = CreateInstance<AttackProfileCatalog>();
            catalog.ReplaceAll(values);
            return catalog;
        }

        void RebuildLookup()
        {
            lookup.Clear();
            entries ??= new List<AttackProfileDefinition>();
            foreach (AttackProfileDefinition entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    continue;
                lookup[entry.id.Trim()] = entry;
            }
        }
    }
}
