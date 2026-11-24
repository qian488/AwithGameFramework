using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Application.Configuration;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public sealed class MapBossOption
    {
        internal MapBossOption(int id, CombatMap route)
        {
            Id = id;
            Route = route;
        }

        public int Id { get; }
        public CombatMap Route { get; }
    }

    public sealed class MapBossCatalogResult : IReadOnlyList<MapBossOption>
    {
        private readonly IReadOnlyList<MapBossOption> options;

        internal MapBossCatalogResult(IReadOnlyList<MapBossOption> options, string configurationHash)
        {
            this.options = options;
            ConfigurationHash = configurationHash;
        }

        public string ConfigurationHash { get; }
        public int Count => options.Count;
        public MapBossOption this[int index] => options[index];
        public IEnumerator<MapBossOption> GetEnumerator() => options.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class MapBossCatalog
    {
        public static async Task<MapBossCatalogResult> LoadAsync()
        {
            var strikes = await ReadTableAsync("BossStrike").ConfigureAwait(false);
            var bosses = await ReadTableAsync("BossChallenge").ConfigureAwait(false);
            return await LoadAsync(strikes, bosses).ConfigureAwait(false);
        }

        public static async Task<MapBossCatalogResult> LoadAsync(byte[] strikeData, byte[] bossData)
        {
            var serializer = new JsonSerializer();
            var strikes = await ConfigTable<BossStrike>.LoadAsync(strikeData, serializer, BossStrike.SchemaHash, row => row.Id).ConfigureAwait(false);
            var bosses = await ConfigTable<BossChallenge>.LoadAsync(bossData, serializer, BossChallenge.SchemaHash, row => row.Id).ConfigureAwait(false);
            var moves = new Dictionary<int, Strike>();
            foreach (var strike in strikes.Rows)
            {
                Require(strike.Id, "BossStrike", nameof(strike.Windup), strike.Windup, 12, 60);
                Require(strike.Id, "BossStrike", nameof(strike.Active), strike.Active, 1, 20);
                Require(strike.Id, "BossStrike", nameof(strike.Recovery), strike.Recovery, 1, 180);
                Require(strike.Id, "BossStrike", nameof(strike.Damage), strike.Damage, 1, 100);
                Require(strike.Id, "BossStrike", nameof(strike.Pressure), strike.Pressure, 1, 100);
                Require(strike.Id, "BossStrike", nameof(strike.Reach), strike.Reach, 1, 4);
                Require(strike.Id, "BossStrike", nameof(strike.Arc), strike.Arc, 1, 180);
                moves.Add(strike.Id, new Strike(strike.Windup, strike.Active, strike.Recovery, strike.Damage, strike.Pressure, strike.Reach, strike.Arc));
            }

            var options = new List<MapBossOption>(bosses.Count);
            foreach (var boss in bosses.Rows)
            {
                var route = CombatMap.Routes.FirstOrDefault(map => map.Id.ToString() == boss.Map);
                if (route == null) throw new ConfigurationValidationException($"BossChallenge ID {boss.Id}: unknown Map '{boss.Map}'.");
                if (!Enum.TryParse<EnemyWeapon>(boss.Weapon, out var weapon) || Enum.GetName(typeof(EnemyWeapon), weapon) != boss.Weapon)
                    throw new ConfigurationValidationException($"BossChallenge ID {boss.Id}: unknown Weapon '{boss.Weapon}'.");
                RequireText(boss.Id, nameof(boss.Name), boss.Name, 30);
                RequireText(boss.Id, nameof(boss.Advice), boss.Advice, 120);
                Require(boss.Id, "BossChallenge", nameof(boss.Health), boss.Health, 1, 1000);
                Require(boss.Id, "BossChallenge", nameof(boss.Posture), boss.Posture, 1, 1000);
                Require(boss.Id, "BossChallenge", nameof(boss.Cooldown), boss.Cooldown, 1, 600);
                Require(boss.Id, "BossChallenge", nameof(boss.Speed), boss.Speed, .1f, 6);
                Require(boss.Id, "BossChallenge", nameof(boss.Lunge), boss.Lunge, 0, 1.5f);
                var enemy = new MapEnemy(boss.Name, weapon, boss.Health, boss.Posture, boss.Cooldown, boss.Speed, boss.Lunge, boss.Guards,
                    ReadMoves(boss, nameof(boss.Opening), boss.Opening, moves, required: true),
                    ReadMoves(boss, nameof(boss.FollowUp), boss.FollowUp, moves),
                    ReadMoves(boss, nameof(boss.Desperation), boss.Desperation, moves));
                options.Add(new MapBossOption(boss.Id, route.WithBoss(enemy, boss.Advice)));
            }
            return new MapBossCatalogResult(options.AsReadOnly(), HashConfiguration(strikes, bosses));
        }

        private static string HashConfiguration(ConfigTable<BossStrike> strikes, ConfigTable<BossChallenge> bosses)
        {
            using var hash = SHA256.Create();
            var input = Encoding.UTF8.GetBytes(strikes.SchemaHash + ":" + strikes.ContentHash + "|" + bosses.SchemaHash + ":" + bosses.ContentHash);
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static async Task<byte[]> ReadTableAsync(string table)
        {
            var name = "Test.RainPass." + table + ".json";
            using var resource = typeof(MapBossCatalog).Assembly.GetManifestResourceStream(name)
                ?? throw new FileNotFoundException($"Missing Rain Pass configuration resource {name}.");
            using var buffer = new MemoryStream();
            await resource.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }

        private static Strike[] ReadMoves(BossChallenge boss, string field, int[] ids, Dictionary<int, Strike> strikes, bool required = false)
        {
            if (ids == null || required && ids.Length == 0)
                throw new ConfigurationValidationException($"BossChallenge ID {boss.Id}: {field} needs an attack chain.");
            if (ids.Length == 0) return null;
            var result = new Strike[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                if (!strikes.TryGetValue(ids[index], out result[index]))
                    throw new ConfigurationValidationException($"BossChallenge ID {boss.Id}: {field}[{index}] references missing BossStrike ID {ids[index]}.");
            }
            return result;
        }

        private static void Require(int id, string table, string field, float value, float minimum, float maximum)
        {
            if (!(value >= minimum && value <= maximum))
                throw new ConfigurationValidationException($"{table} ID {id}, field '{field}': expected {minimum} to {maximum}, got {value}.");
        }

        private static void RequireText(int id, string field, string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Contains('\r') || value.Contains('\n'))
                throw new ConfigurationValidationException($"BossChallenge ID {id}, field '{field}': expected nonempty text on one line (up to {maximum} characters).");
        }
    }
}
