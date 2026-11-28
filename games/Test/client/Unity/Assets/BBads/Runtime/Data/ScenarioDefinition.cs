using System;
using System.Collections.Generic;
using UnityEngine;

namespace BBads.Data
{
    [Serializable]
    public sealed class EnemySpawnEvent
    {
        [Min(0f)] public float time;
        public string unitId;
        public Vector2 position;
    }

    [Serializable]
    public sealed class ItemSpawnEvent
    {
        [Min(0f)] public float appearTime;
        public float placeTime = -1f;
        public int x = -1;
        public int y = -1;
        public string itemId;

        public bool HasScheduledPlacement => placeTime >= appearTime && x >= 0 && y >= 0;
    }

    [Serializable]
    public sealed class SummonPosition
    {
        [Min(0)] public int order;
        public Vector2 position;
    }

    public sealed class ScenarioDefinition : ScriptableObject
    {
        public const int MaxSummonPositions = 999;

        [SerializeField] string scenarioId = "demo_001";
        [SerializeField, Min(0f)] float duration = 30f;
        [SerializeField] Rect battleBounds = new Rect(-2.5f, -1.25f, 5f, 2.5f);
        [SerializeField] List<EnemySpawnEvent> enemySpawns = new List<EnemySpawnEvent>();
        [SerializeField] List<ItemSpawnEvent> itemSpawns = new List<ItemSpawnEvent>();
        [SerializeField] List<SummonPosition> summonPositions = new List<SummonPosition>();

        public string ScenarioId => scenarioId;
        public float Duration => duration;
        public Rect BattleBounds => battleBounds;
        public IReadOnlyList<EnemySpawnEvent> EnemySpawns => enemySpawns;
        public IReadOnlyList<ItemSpawnEvent> ItemSpawns => itemSpawns;
        public IReadOnlyList<SummonPosition> SummonPositions => summonPositions;

        public void ReplaceImportedData(
            string id,
            IEnumerable<EnemySpawnEvent> enemies,
            IEnumerable<ItemSpawnEvent> items,
            IEnumerable<SummonPosition> positions)
        {
            scenarioId = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
            enemySpawns = enemies != null ? new List<EnemySpawnEvent>(enemies) : new List<EnemySpawnEvent>();
            itemSpawns = items != null ? new List<ItemSpawnEvent>(items) : new List<ItemSpawnEvent>();
            summonPositions = positions != null ? new List<SummonPosition>(positions) : new List<SummonPosition>();
            Normalize();
        }

        public void LoadRuntimeData(
            string id,
            IEnumerable<EnemySpawnEvent> enemies,
            IEnumerable<ItemSpawnEvent> items,
            IEnumerable<SummonPosition> positions)
        {
            ReplaceImportedData(id, enemies, items, positions);
        }

        void OnValidate() => Normalize();

        void Normalize()
        {
            enemySpawns ??= new List<EnemySpawnEvent>();
            itemSpawns ??= new List<ItemSpawnEvent>();
            summonPositions ??= new List<SummonPosition>();
            enemySpawns.Sort((a, b) => SafeTime(a).CompareTo(SafeTime(b)));
            itemSpawns.Sort((a, b) => SafeTime(a).CompareTo(SafeTime(b)));
            summonPositions.Sort((a, b) => SafeOrder(a).CompareTo(SafeOrder(b)));
            if (summonPositions.Count > MaxSummonPositions)
                summonPositions.RemoveRange(MaxSummonPositions, summonPositions.Count - MaxSummonPositions);

            float lastEventTime = 0f;
            for (int i = 0; i < enemySpawns.Count; i++)
                if (enemySpawns[i] != null)
                    lastEventTime = Mathf.Max(lastEventTime, enemySpawns[i].time);
            for (int i = 0; i < itemSpawns.Count; i++)
            {
                ItemSpawnEvent item = itemSpawns[i];
                if (item == null)
                    continue;
                lastEventTime = Mathf.Max(lastEventTime, item.appearTime);
                if (item.HasScheduledPlacement)
                    lastEventTime = Mathf.Max(lastEventTime, item.placeTime);
            }
            if (lastEventTime > 0f)
                duration = lastEventTime + 2f;
        }

        static float SafeTime(EnemySpawnEvent value) => value != null ? value.time : float.MaxValue;
        static float SafeTime(ItemSpawnEvent value) => value != null ? value.appearTime : float.MaxValue;
        static int SafeOrder(SummonPosition value) => value != null ? value.order : int.MaxValue;
    }
}
