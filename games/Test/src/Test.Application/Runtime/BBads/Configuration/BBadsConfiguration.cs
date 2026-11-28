using System;
using System.Collections.Generic;
using System.Linq;

namespace Test.Application.BBads.Configuration
{
    public sealed class BBadsConfiguration
    {
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        private BBadsConfiguration(
            IReadOnlyList<BBadsUnit> units,
            IReadOnlyList<BBadsAttackProfile> attackProfiles,
            IReadOnlyList<BBadsItem> items,
            IReadOnlyList<BBadsEncounter> encounters,
            IReadOnlyList<BBadsItemSpawn> itemSpawns,
            IReadOnlyList<BBadsSummonPosition> summonPositions)
        {
            Units = units;
            AttackProfiles = attackProfiles;
            Items = items;
            Encounters = encounters;
            ItemSpawns = itemSpawns;
            SummonPositions = summonPositions;
        }

        public IReadOnlyList<BBadsUnit> Units { get; }
        public IReadOnlyList<BBadsAttackProfile> AttackProfiles { get; }
        public IReadOnlyList<BBadsItem> Items { get; }
        public IReadOnlyList<BBadsEncounter> Encounters { get; }
        public IReadOnlyList<BBadsItemSpawn> ItemSpawns { get; }
        public IReadOnlyList<BBadsSummonPosition> SummonPositions { get; }

        // The reader receives a table name without .csv and returns null for an absent source.
        public static BBadsConfiguration Load(Func<string, string> readCsv)
        {
            if (readCsv == null)
                throw new ArgumentNullException(nameof(readCsv));

            var profiles = ReadCatalog(BBadsCsv.Read(readCsv, "attack_profile",
                "id,attackAnimation,hitDelay,projectileResourcePath,projectileSpeed,projectileScale," +
                "projectileSpawnOffsetX,projectileSpawnOffsetY,projectileTrajectory,projectileArcHeight,projectileRotationOffset," +
                "castEffectResourcePath,castEffectScale,castEffectOffsetX,castEffectOffsetY,hitEffectResourcePath," +
                "hitEffectScale,hitEffectOffsetX,hitEffectOffsetY,randomHitRotation"), ReadAttackProfile, out var profileEntries);

            var units = ReadCatalog(BBadsCsv.Read(readCsv, "unit",
                "id,displayName,camp,pictureResourcePath,hp,atk,moveSpeed,isMelee,attackRange,aoeRange,searchRange," +
                "attackInterval,visualScale,attackSize,bodyRadius,hpBarOffset,spineResourcePath,attackProfileId," +
                "hitEffectResourcePath,hitEffectScale,deathEffectResourcePath,deathEffectScale,flipX"), row =>
            {
                var unit = ReadUnit(row);
                RequireReference(row, "attackProfileId", unit.AttackProfileId, profiles, "attack_profile");
                return unit;
            }, out var unitEntries);

            var itemRows = BBadsCsv.Read(readCsv, "item",
                "id,displayName,type,frameResourcePath,pictureResourcePath,cooldown,fishCost,shape,mergeGroup,mergeLevel," +
                "nextItemId,summonUnitId,activationAmount").ToList();
            var items = ReadCatalog(itemRows, ReadItem, out var itemEntries);
            for (var index = 0; index < itemRows.Count; index++)
                ValidateItem(itemRows[index], itemEntries[index], items, units);

            var encounters = BBadsCsv.Read(readCsv, "encounter", "scenarioId,time,enemyId,x,y").Select(row =>
            {
                var id = row.Required("enemyId");
                var enemy = RequireReference(row, "enemyId", id, units, "unit");
                if (enemy.Camp != BBadsUnitCamp.Enemy)
                    throw row.Error("enemyId", $"Unit '{id}' must belong to the Enemy camp.");
                return new BBadsEncounter
                {
                    ScenarioId = row.Text("scenarioId"),
                    Time = row.Number("time", 0),
                    EnemyId = id,
                    X = row.Number("x"),
                    Y = row.Number("y")
                };
            }).ToList();

            var itemSpawns = BBadsCsv.Read(readCsv, "item_spawn", "scenarioId,itemId,appearTime,placeTime,x,y")
                .Select(row => ReadItemSpawn(row, items)).ToList();
            var summonPositions = ReadSummonPositions(BBadsCsv.Read(readCsv, "summon_position", "scenarioId,order,x,y"));

            // Catalog lookups serve validation. Public lists follow source order.
            return new BBadsConfiguration(
                unitEntries.AsReadOnly(), profileEntries.AsReadOnly(), itemEntries.AsReadOnly(),
                encounters.AsReadOnly(), itemSpawns.AsReadOnly(), summonPositions.AsReadOnly());
        }

        public BBadsScenario GetScenario(string scenarioId)
        {
            if (string.IsNullOrWhiteSpace(scenarioId))
                throw new ArgumentException("A scenario ID is required.", nameof(scenarioId));

            var id = scenarioId.Trim();
            return new BBadsScenario(id,
                Encounters.Where(row => Matches(row.ScenarioId, id)).OrderBy(row => row.Time).ToList().AsReadOnly(),
                ItemSpawns.Where(row => Matches(row.ScenarioId, id)).OrderBy(row => row.AppearTime).ToList().AsReadOnly(),
                SummonPositions.Where(row => Matches(row.ScenarioId, id)).OrderBy(row => row.Order).ToList().AsReadOnly());
        }

        private static bool Matches(string selector, string id) => selector.Length == 0 || IdComparer.Equals(selector, id);

        private static Dictionary<string, T> ReadCatalog<T>(
            IEnumerable<BBadsCsv.Row> rows, Func<BBadsCsv.Row, T> read, out List<T> entries)
        {
            var catalog = new Dictionary<string, T>(IdComparer);
            entries = new List<T>();
            foreach (var row in rows)
            {
                var id = row.Required("id");
                if (catalog.ContainsKey(id))
                    throw row.Error("id", $"Duplicate ID '{id}'.");
                var entry = read(row);
                catalog.Add(id, entry);
                entries.Add(entry);
            }
            return catalog;
        }

        private static T RequireReference<T>(
            BBadsCsv.Row row, string field, string id, IReadOnlyDictionary<string, T> catalog, string table)
        {
            if (!catalog.TryGetValue(id, out var value))
                throw row.Error(field, $"Unknown {table}.id '{id}'.");
            return value;
        }

        private static BBadsUnit ReadUnit(BBadsCsv.Row row) => new BBadsUnit
        {
            Id = row.Required("id"),
            DisplayName = row.Required("displayName"),
            Camp = row.Choice<BBadsUnitCamp>("camp"),
            PictureResourcePath = row.Text("pictureResourcePath"),
            MaxHealth = row.Number("hp", 1),
            Attack = row.Number("atk", 0),
            MoveSpeed = row.Number("moveSpeed", 0),
            IsMelee = row.Boolean("isMelee"),
            AttackRange = row.Number("attackRange", 0),
            AoeRange = row.Number("aoeRange", 0),
            SearchRange = row.Number("searchRange", 0),
            AttackInterval = row.Number("attackInterval", 0.01f),
            VisualScale = row.Number("visualScale", 0.01f),
            AttackSize = row.Number("attackSize", 0.01f),
            BodyRadius = row.Number("bodyRadius", 0.01f),
            HpBarOffset = row.Number("hpBarOffset"),
            SpineResourcePath = row.Text("spineResourcePath"),
            AttackProfileId = row.Required("attackProfileId"),
            HitEffectResourcePath = row.Text("hitEffectResourcePath"),
            HitEffectScale = row.Number("hitEffectScale", 0.01f),
            DeathEffectResourcePath = row.Text("deathEffectResourcePath"),
            DeathEffectScale = row.Number("deathEffectScale", 0.01f),
            FlipX = row.Boolean("flipX")
        };

        private static BBadsAttackProfile ReadAttackProfile(BBadsCsv.Row row) => new BBadsAttackProfile
        {
            Id = row.Required("id"),
            AttackAnimation = row.Text("attackAnimation"),
            HitDelay = row.Number("hitDelay", 0),
            ProjectileResourcePath = row.Text("projectileResourcePath"),
            ProjectileSpeed = row.Number("projectileSpeed", 0.1f),
            ProjectileScale = row.Number("projectileScale", 0.01f),
            ProjectileSpawnOffsetX = row.Number("projectileSpawnOffsetX"),
            ProjectileSpawnOffsetY = row.Number("projectileSpawnOffsetY"),
            ProjectileTrajectory = row.Choice<BBadsProjectileTrajectory>("projectileTrajectory"),
            ProjectileArcHeight = row.Number("projectileArcHeight", 0),
            ProjectileRotationOffset = row.Number("projectileRotationOffset"),
            CastEffectResourcePath = row.Text("castEffectResourcePath"),
            CastEffectScale = row.Number("castEffectScale", 0.01f),
            CastEffectOffsetX = row.Number("castEffectOffsetX"),
            CastEffectOffsetY = row.Number("castEffectOffsetY"),
            HitEffectResourcePath = row.Text("hitEffectResourcePath"),
            HitEffectScale = row.Number("hitEffectScale", 0.01f),
            HitEffectOffsetX = row.Number("hitEffectOffsetX"),
            HitEffectOffsetY = row.Number("hitEffectOffsetY"),
            RandomHitRotation = row.Boolean("randomHitRotation")
        };

        private static BBadsItem ReadItem(BBadsCsv.Row row) => new BBadsItem
        {
            Id = row.Required("id"),
            DisplayName = row.Required("displayName"),
            Type = row.Choice<BBadsItemType>("type"),
            FrameResourcePath = row.Text("frameResourcePath"),
            PictureResourcePath = row.Text("pictureResourcePath"),
            Cooldown = row.Number("cooldown", 0),
            FishCost = row.Number("fishCost", 0),
            ShapeResourcePath = row.Required("shape"),
            MergeGroup = row.Text("mergeGroup"),
            MergeLevel = row.Integer("mergeLevel", 1),
            NextItemId = row.Text("nextItemId"),
            SummonUnitId = row.Text("summonUnitId"),
            ActivationAmount = row.Number("activationAmount", 0)
        };

        private static void ValidateItem(
            BBadsCsv.Row row, BBadsItem item, IReadOnlyDictionary<string, BBadsItem> items,
            IReadOnlyDictionary<string, BBadsUnit> units)
        {
            if (item.NextItemId.Length > 0)
                RequireReference(row, "nextItemId", item.NextItemId, items, "item");

            if (item.Type == BBadsItemType.Cat)
                row.Required("summonUnitId");
            if (item.SummonUnitId.Length > 0)
            {
                var unit = RequireReference(row, "summonUnitId", item.SummonUnitId, units, "unit");
                if (item.Type == BBadsItemType.Cat && unit.Camp != BBadsUnitCamp.Player)
                    throw row.Error("summonUnitId", $"Unit '{unit.Id}' must belong to the Player camp for a Cat item.");
            }
        }

        private static BBadsItemSpawn ReadItemSpawn(BBadsCsv.Row row, IReadOnlyDictionary<string, BBadsItem> items)
        {
            var itemId = row.Required("itemId");
            RequireReference(row, "itemId", itemId, items, "item");
            var spawn = new BBadsItemSpawn
            {
                ScenarioId = row.Text("scenarioId"),
                ItemId = itemId,
                AppearTime = row.Number("appearTime", 0),
                PlaceTime = -1,
                X = -1,
                Y = -1
            };

            if (row.Text("placeTime").Length == 0 && row.Text("x").Length == 0 && row.Text("y").Length == 0)
                return spawn;

            var placeTime = row.Number("placeTime", -1);
            var x = row.Integer("x", -1);
            var y = row.Integer("y", -1);
            if (placeTime == -1 && x == -1 && y == -1)
                return spawn;

            if (placeTime < spawn.AppearTime)
                throw row.Error("placeTime", "Placement time must be at or after appearance time.");
            if (x < 0)
                throw row.Error("x", "Scheduled placement requires a nonnegative X grid coordinate.");
            if (y < 0)
                throw row.Error("y", "Scheduled placement requires a nonnegative Y grid coordinate.");
            spawn.PlaceTime = placeTime;
            spawn.X = x;
            spawn.Y = y;
            return spawn;
        }

        private static List<BBadsSummonPosition> ReadSummonPositions(IEnumerable<BBadsCsv.Row> rows)
        {
            var ordersByScenario = new Dictionary<string, HashSet<int>>(IdComparer);
            var positions = new List<BBadsSummonPosition>();
            foreach (var row in rows)
            {
                var scenario = row.Text("scenarioId");
                var order = row.Integer("order", 0);
                if (!ordersByScenario.TryGetValue(scenario, out var orders))
                {
                    orders = new HashSet<int>();
                    ordersByScenario.Add(scenario, orders);
                }
                if (!orders.Add(order))
                    throw row.Error("order", $"Duplicate summon order {order} for selector '{scenario}'.");

                positions.Add(new BBadsSummonPosition
                {
                    ScenarioId = scenario,
                    Order = order,
                    X = row.Number("x"),
                    Y = row.Number("y")
                });
            }
            return positions;
        }
    }
}
