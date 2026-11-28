using System.Collections.Generic;
using System.Linq;
using Test.Application.BBads.Configuration;
using UnityEngine;
using BBads.Backpack;
using BBads.Combat;
using BBads.Timeline;

namespace BBads.Data
{
    public sealed class CsvRuntimeData : MonoBehaviour
    {
        [SerializeField] TextAsset unitCsv;
        [SerializeField] TextAsset attackProfileCsv;
        [SerializeField] TextAsset itemCsv;
        [SerializeField] TextAsset encounterCsv;
        [SerializeField] TextAsset itemSpawnCsv;
        [SerializeField] TextAsset summonPositionCsv;
        [SerializeField] string scenarioId = "demo_001";
        [SerializeField] CombatSpawner combatSpawner;
        [SerializeField] BackpackController backpackController;
        [SerializeField] ScenarioRunner scenarioRunner;
        bool initialized;

        public UnitCatalog Units { get; private set; }
        public AttackProfileCatalog AttackProfiles { get; private set; }
        public ItemCatalog Items { get; private set; }
        public ScenarioDefinition Scenario { get; private set; }

        void Awake()
        {
            Initialize();
        }

        public void Configure(CombatSpawner spawner, BackpackController backpack, ScenarioRunner runner)
        {
            combatSpawner = spawner;
            backpackController = backpack;
            scenarioRunner = runner;
            if (initialized)
                BindRuntimeData();
        }

        void Initialize()
        {
            if (initialized)
                return;
            BBadsConfiguration data = BBadsConfiguration.Load(ReadTable);
            AttackProfiles = AttackProfileCatalog.CreateRuntime(data.AttackProfiles.Select(MapAttackProfile));
            Units = UnitCatalog.CreateRuntime(data.Units.Select(MapUnit));
            foreach (UnitDefinition unit in Units.Entries)
                if (AttackProfiles.TryGet(unit.attackProfileId, out AttackProfileDefinition profile))
                    unit.SetAttackProfile(profile);
            Items = ItemCatalog.CreateRuntime(data.Items.Select(MapItem));
            BBadsScenario rows = data.GetScenario(scenarioId);
            Scenario = ScriptableObject.CreateInstance<ScenarioDefinition>();
            Scenario.LoadRuntimeData(rows.Id,
                rows.EnemySpawns.Select(row => new EnemySpawnEvent
                {
                    time = row.Time, unitId = row.EnemyId, position = new Vector2(row.X, row.Y)
                }),
                rows.ItemSpawns.Select(row => new ItemSpawnEvent
                {
                    appearTime = row.AppearTime, placeTime = row.PlaceTime,
                    x = row.X, y = row.Y, itemId = row.ItemId
                }),
                rows.SummonPositions.Select(row => new SummonPosition
                {
                    order = row.Order, position = new Vector2(row.X, row.Y)
                }));
            initialized = true;
            BindRuntimeData();
        }

        void BindRuntimeData()
        {
            if (combatSpawner != null) combatSpawner.SetUnitCatalog(Units);
            if (backpackController != null) backpackController.SetItemCatalog(Items);
            if (scenarioRunner != null) scenarioRunner.SetScenario(Scenario);
        }

        string ReadTable(string name)
        {
            TextAsset asset = name switch
            {
                "unit" => unitCsv,
                "attack_profile" => attackProfileCsv,
                "item" => itemCsv,
                "encounter" => encounterCsv,
                "item_spawn" => itemSpawnCsv,
                "summon_position" => summonPositionCsv,
                _ => null
            };
            if (asset == null)
                asset = Resources.Load<TextAsset>("BBads/Csv/" + name);
            return asset != null ? asset.text : null;
        }

        void OnDestroy()
        {
            if (Scenario != null) Destroy(Scenario);
            if (Items != null) Destroy(Items);
            if (Units != null) Destroy(Units);
            if (AttackProfiles != null) Destroy(AttackProfiles);
        }

        static UnitDefinition MapUnit(BBadsUnit row) => new UnitDefinition
        {
            id = row.Id, displayName = row.DisplayName,
            camp = row.Camp == BBadsUnitCamp.Player ? UnitCamp.Player : UnitCamp.Enemy,
            flipX = row.FlipX, pictureResourcePath = row.PictureResourcePath,
            attack = row.Attack, maxHealth = row.MaxHealth, moveSpeed = row.MoveSpeed,
            isMelee = row.IsMelee, attackRange = row.AttackRange, aoeRange = row.AoeRange,
            searchRange = row.SearchRange, attackInterval = row.AttackInterval,
            visualScale = row.VisualScale, attackSize = row.AttackSize, bodyRadius = row.BodyRadius,
            hpBarOffset = row.HpBarOffset, spineResourcePath = row.SpineResourcePath,
            attackProfileId = row.AttackProfileId, hitEffectResourcePath = row.HitEffectResourcePath,
            hitEffectScale = row.HitEffectScale, deathEffectResourcePath = row.DeathEffectResourcePath,
            deathEffectScale = row.DeathEffectScale
        };

        static AttackProfileDefinition MapAttackProfile(BBadsAttackProfile row) => new AttackProfileDefinition
        {
            id = row.Id, attackAnimation = row.AttackAnimation, hitDelay = row.HitDelay,
            projectileResourcePath = row.ProjectileResourcePath, projectileSpeed = row.ProjectileSpeed,
            projectileScale = row.ProjectileScale,
            projectileSpawnOffset = new Vector2(row.ProjectileSpawnOffsetX, row.ProjectileSpawnOffsetY),
            projectileTrajectory = (ProjectileTrajectory)row.ProjectileTrajectory,
            projectileArcHeight = row.ProjectileArcHeight, projectileRotationOffset = row.ProjectileRotationOffset,
            castEffectResourcePath = row.CastEffectResourcePath, castEffectScale = row.CastEffectScale,
            castEffectOffset = new Vector2(row.CastEffectOffsetX, row.CastEffectOffsetY),
            hitEffectResourcePath = row.HitEffectResourcePath, hitEffectScale = row.HitEffectScale,
            hitEffectOffset = new Vector2(row.HitEffectOffsetX, row.HitEffectOffsetY),
            randomHitRotation = row.RandomHitRotation
        };

        static ItemDefinition MapItem(BBadsItem row)
        {
            BackpackSlotSO shape = Resources.Load<BackpackSlotSO>("BBads/SlotShapes/" + row.ShapeResourcePath);
            if (shape == null || !shape.TryBuildGridCells(out List<Vector2Int> cells))
                throw new System.InvalidOperationException(
                    $"Item '{row.Id}' requires a valid BBads/SlotShapes/{row.ShapeResourcePath} asset.");
            return new ItemDefinition
            {
                id = row.Id, displayName = row.DisplayName, type = (ItemType)row.Type,
                frameResourcePath = row.FrameResourcePath, pictureResourcePath = row.PictureResourcePath,
                cooldown = row.Cooldown, fishCost = row.FishCost,
                shapeResourcePath = row.ShapeResourcePath, cells = cells,
                mergeGroup = row.MergeGroup, mergeLevel = row.MergeLevel, nextItemId = row.NextItemId,
                summonUnitId = row.SummonUnitId, activationAmount = row.ActivationAmount
            };
        }
    }
}
