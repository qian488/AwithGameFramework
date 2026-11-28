using System.Collections.Generic;

namespace Test.Application.BBads.Configuration
{
    public enum BBadsUnitCamp
    {
        Player,
        Enemy
    }

    public enum BBadsProjectileTrajectory
    {
        Homing,
        Straight,
        Arc
    }

    public enum BBadsItemType
    {
        None,
        Cat,
        Fish,
        Character,
        Weapon,
        Slot
    }

    public sealed class BBadsUnit
    {
        internal BBadsUnit() { }

        public string Id { get; internal set; }
        public string DisplayName { get; internal set; }
        public string PictureResourcePath { get; internal set; }
        public string SpineResourcePath { get; internal set; }
        public string AttackProfileId { get; internal set; }
        public string HitEffectResourcePath { get; internal set; }
        public string DeathEffectResourcePath { get; internal set; }
        public BBadsUnitCamp Camp { get; internal set; }
        public float MaxHealth { get; internal set; }
        public float Attack { get; internal set; }
        public float MoveSpeed { get; internal set; }
        public float AttackRange { get; internal set; }
        public float AoeRange { get; internal set; }
        public float SearchRange { get; internal set; }
        public float AttackInterval { get; internal set; }
        public float VisualScale { get; internal set; }
        public float AttackSize { get; internal set; }
        public float BodyRadius { get; internal set; }
        public float HpBarOffset { get; internal set; }
        public float HitEffectScale { get; internal set; }
        public float DeathEffectScale { get; internal set; }
        public bool IsMelee { get; internal set; }
        public bool FlipX { get; internal set; }
    }

    public sealed class BBadsAttackProfile
    {
        internal BBadsAttackProfile() { }

        public string Id { get; internal set; }
        public string AttackAnimation { get; internal set; }
        public string ProjectileResourcePath { get; internal set; }
        public string CastEffectResourcePath { get; internal set; }
        public string HitEffectResourcePath { get; internal set; }
        public float HitDelay { get; internal set; }
        public float ProjectileSpeed { get; internal set; }
        public float ProjectileScale { get; internal set; }
        public float ProjectileSpawnOffsetX { get; internal set; }
        public float ProjectileSpawnOffsetY { get; internal set; }
        public float ProjectileArcHeight { get; internal set; }
        public float ProjectileRotationOffset { get; internal set; }
        public float CastEffectScale { get; internal set; }
        public float CastEffectOffsetX { get; internal set; }
        public float CastEffectOffsetY { get; internal set; }
        public float HitEffectScale { get; internal set; }
        public float HitEffectOffsetX { get; internal set; }
        public float HitEffectOffsetY { get; internal set; }
        public BBadsProjectileTrajectory ProjectileTrajectory { get; internal set; }
        public bool RandomHitRotation { get; internal set; }
    }

    public sealed class BBadsItem
    {
        internal BBadsItem() { }

        public string Id { get; internal set; }
        public string DisplayName { get; internal set; }
        public string FrameResourcePath { get; internal set; }
        public string PictureResourcePath { get; internal set; }
        public string ShapeResourcePath { get; internal set; }
        public string MergeGroup { get; internal set; }
        public string NextItemId { get; internal set; }
        public string SummonUnitId { get; internal set; }
        public BBadsItemType Type { get; internal set; }
        public float Cooldown { get; internal set; }
        public float FishCost { get; internal set; }
        public float ActivationAmount { get; internal set; }
        public int MergeLevel { get; internal set; }
    }

    public sealed class BBadsEncounter
    {
        internal BBadsEncounter() { }

        public string ScenarioId { get; internal set; }
        public string EnemyId { get; internal set; }
        public float Time { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
    }

    public sealed class BBadsItemSpawn
    {
        internal BBadsItemSpawn() { }

        public string ScenarioId { get; internal set; }
        public string ItemId { get; internal set; }
        public float AppearTime { get; internal set; }
        public float PlaceTime { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }

        public bool HasScheduledPlacement => PlaceTime >= AppearTime && X >= 0 && Y >= 0;
    }

    public sealed class BBadsSummonPosition
    {
        internal BBadsSummonPosition() { }

        public string ScenarioId { get; internal set; }
        public int Order { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
    }

    public sealed class BBadsScenario
    {
        internal BBadsScenario(
            string id,
            IReadOnlyList<BBadsEncounter> enemySpawns,
            IReadOnlyList<BBadsItemSpawn> itemSpawns,
            IReadOnlyList<BBadsSummonPosition> summonPositions)
        {
            Id = id;
            EnemySpawns = enemySpawns;
            ItemSpawns = itemSpawns;
            SummonPositions = summonPositions;
        }

        public string Id { get; }
        public IReadOnlyList<BBadsEncounter> EnemySpawns { get; }
        public IReadOnlyList<BBadsItemSpawn> ItemSpawns { get; }
        public IReadOnlyList<BBadsSummonPosition> SummonPositions { get; }
    }
}
