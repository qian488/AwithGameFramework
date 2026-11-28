using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Test.Application.BBads.Configuration;

namespace Test.Application.Tests
{
    public sealed class BBadsConfigurationTests
    {
        [Test]
        [SetCulture("fr-FR")]
        public void LoadsAllSourceRowsAndPreservesWaveSeconds()
        {
            var config = BBadsConfiguration.Load(ReadSource);
            var scenario = config.GetScenario("DEMO_001");
            var grenade = config.AttackProfiles.Single(row => row.Id == "bbads_cat_grenade_projectile");
            var cat = config.Units.Single(row => row.Id == "CatGrenade1");
            var item = config.Items.Single(row => row.Id == "CatGrenade1");

            Assert.Multiple(() =>
            {
                Assert.That(config.AttackProfiles, Has.Count.EqualTo(13));
                Assert.That(config.Units, Has.Count.EqualTo(44));
                Assert.That(config.Items, Has.Count.EqualTo(25));
                Assert.That(config.Encounters, Has.Count.EqualTo(96));
                Assert.That(config.ItemSpawns, Has.Count.EqualTo(32));
                Assert.That(config.SummonPositions, Has.Count.EqualTo(3));
                Assert.That(scenario.EnemySpawns, Has.Count.EqualTo(96));
                Assert.That(scenario.ItemSpawns, Has.Count.EqualTo(32));
                Assert.That(scenario.SummonPositions, Has.Count.EqualTo(3));
                Assert.That(grenade.ProjectileTrajectory, Is.EqualTo(BBadsProjectileTrajectory.Arc));
                Assert.That(grenade.ProjectileArcHeight, Is.EqualTo(0.6f));
                Assert.That(grenade.HitDelay, Is.EqualTo(0.3f));
                Assert.That(cat.AttackProfileId, Is.EqualTo(grenade.Id));
                Assert.That(cat.MaxHealth, Is.EqualTo(80f));
                Assert.That(cat.FlipX, Is.True);
                Assert.That(item.SummonUnitId, Is.EqualTo(cat.Id));
                Assert.That(item.NextItemId, Is.EqualTo("CatGrenade2"));
                Assert.That(item.ShapeResourcePath, Is.EqualTo("Slot1"));
                Assert.That(scenario.EnemySpawns[0].Time, Is.EqualTo(3f));
                Assert.That(scenario.EnemySpawns.Count(row => row.Time == 8.5f), Is.EqualTo(5));
                Assert.That(scenario.EnemySpawns.Last().Time, Is.EqualTo(158f));
                Assert.That(scenario.ItemSpawns[0].AppearTime, Is.EqualTo(4f));
                Assert.That(scenario.ItemSpawns[0].PlaceTime, Is.EqualTo(4.8f));
                Assert.That(scenario.ItemSpawns[0].HasScheduledPlacement, Is.True);
                Assert.That(scenario.ItemSpawns.Count(row => row.HasScheduledPlacement), Is.EqualTo(26));
                Assert.That((scenario.ItemSpawns.Last().PlaceTime, scenario.ItemSpawns.Last().X, scenario.ItemSpawns.Last().Y),
                    Is.EqualTo((-1f, -1, -1)));
                Assert.That(scenario.SummonPositions.Select(row => (row.Order, row.X, row.Y)),
                    Is.EqualTo(new[] { (0, -1.6f, -0.3f), (1, -1.3f, 0.5f), (2, -1.6f, 1.3f) }));
            });
        }

        [Test]
        public void FiltersScenariosAndKeepsSourceOrderForSimultaneousSpawns()
        {
            const string encounters =
                "\uFEFFy,x,enemyId,time,scenarioId\r\n" +
                "0,1,enemycatbaseball,5,Other\r" +
                "0,2,EnemyCatBaseball,5,\n" +
                "0,3,EnemyZombie,2,other\r\n" +
                "0,4,EnemyZombie,0,demo_001";
            var config = LoadWith("encounter", encounters);
            var other = config.GetScenario(" other ");
            var demo = config.GetScenario("demo_001");

            Assert.Multiple(() =>
            {
                Assert.That(config.Encounters.Select(row => row.X), Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
                Assert.That(other.EnemySpawns.Select(row => row.X), Is.EqualTo(new[] { 3f, 1f, 2f }));
                Assert.That(demo.EnemySpawns.Select(row => row.X), Is.EqualTo(new[] { 4f, 2f }));
            });
        }

        [Test]
        public void HandlesQuotedCommasEscapedQuotesAndMultilineNames()
        {
            const string name = "\"Warrior, \"\"Captain\"\"\r\nLv.1\"";
            var config = LoadWith("unit", ChangeField("unit", 2, "displayName", name));
            Assert.That(config.Units[0].DisplayName, Is.EqualTo("Warrior, \"Captain\"\r\nLv.1"));
        }

        [Test]
        public void ReportsPhysicalRecordStartAfterEmbeddedNewline()
        {
            var units = ChangeField("unit", 3, "hp", "0").Replace("Player Warrior Lv.1", "\"Warrior\r\nLv.1\"");
            AssertDiagnostic("unit", units, 4, "hp", "at least");
        }

        [Test]
        public void ValidatesReferencesEvenInOtherScenarios()
        {
            var encounters = ReadSource("encounter").TrimEnd() + "\nother,200,missing_unit,0,0";
            AssertDiagnostic("encounter", encounters, 98, "enemyId", "Unknown unit.id");
        }

        [TestCase("unit", 2, "attackProfileId", "unknown", "Unknown attack_profile.id")]
        [TestCase("unit", 2, "id", "", "required")]
        [TestCase("unit", 3, "id", "playerwarrior", "Duplicate ID")]
        [TestCase("unit", 2, "hp", "0", "at least")]
        [TestCase("unit", 2, "hp", "NaN", "finite number")]
        [TestCase("unit", 2, "camp", "0", "Expected Player, Enemy")]
        [TestCase("unit", 2, "isMelee", "maybe", "true, false")]
        [TestCase("attack_profile", 3, "id", "BBADS_CAT_BASEBALL_MELEE", "Duplicate ID")]
        [TestCase("attack_profile", 2, "hitDelay", "1e99", "finite number")]
        [TestCase("attack_profile", 2, "projectileTrajectory", "Ballistic", "Homing, Straight, Arc")]
        [TestCase("item", 2, "nextItemId", "unknown", "Unknown item.id")]
        [TestCase("item", 3, "id", "fish1", "Duplicate ID")]
        [TestCase("item", 2, "shape", "", "required")]
        [TestCase("item", 6, "summonUnitId", "", "required")]
        [TestCase("item", 6, "summonUnitId", "unknown", "Unknown unit.id")]
        [TestCase("item", 6, "summonUnitId", "EnemyZombie", "Player camp")]
        [TestCase("encounter", 2, "enemyId", "PlayerWarrior", "Enemy camp")]
        [TestCase("encounter", 2, "time", "-1", "at least")]
        [TestCase("item_spawn", 2, "itemId", "unknown", "Unknown item.id")]
        [TestCase("item_spawn", 2, "placeTime", "3", "at or after")]
        [TestCase("item_spawn", 2, "x", "", "integer")]
        [TestCase("item_spawn", 2, "y", "1.5", "integer")]
        [TestCase("summon_position", 3, "order", "0", "Duplicate summon order")]
        [TestCase("summon_position", 3, "x", "", "finite number")]
        public void RejectsInvalidRows(string table, int row, string field, string value, string reason) =>
            AssertDiagnostic(table, ChangeField(table, row, field, value), row, field, reason);

        [TestCase("scenarioId,time,enemyId,x", 1, "y", "Required column")]
        [TestCase("scenarioId,time,enemyId,x,TIME", 1, "TIME", "Duplicate column")]
        [TestCase("scenarioId,time,enemyId,x,unexpected", 1, "unexpected", "Unexpected column")]
        [TestCase("scenarioId,time,enemyId,x,y\nother,5,EnemyZombie,1", 2, "y", "Expected 5 fields")]
        [TestCase("scenarioId,time,enemyId,x,y\nother,5,EnemyZombie,1,1,extra", 2, "column 6", "Expected 5 fields")]
        [TestCase("scenarioId,time,enemyId,x,y\nother,5,\"EnemyZombie,1,1", 2, "enemyId", "Unclosed quoted field")]
        [TestCase("scenarioId,time,enemyId,x,y\nother,5,\"EnemyZombie\"extra,1,1", 2, "enemyId", "after a closing quote")]
        [TestCase("scenarioId,time,enemyId,x,y\nother,5,En\"emyZombie,1,1", 2, "enemyId", "unquoted field")]
        [TestCase("scenarioId,time,enemyId,x,y\n,,,,", 2, "enemyId", "required")]
        [TestCase("", 1, "<header>", "header is missing")]
        [TestCase(null, 1, "<table>", "source is missing")]
        public void RejectsInvalidCsv(string csv, int row, string field, string reason) =>
            AssertDiagnostic("encounter", csv, row, field, reason);

        [Test]
        public void ReadsExplicitUnscheduledPlacementValues()
        {
            const string spawns = "scenarioId,itemId,appearTime,placeTime,x,y\n,Fish1,4,-1.0,-1,-1";
            var config = LoadWith("item_spawn", spawns);

            Assert.That(config.GetScenario("demo_001").ItemSpawns.Single().HasScheduledPlacement, Is.False);
        }

        private static void AssertDiagnostic(string table, string csv, int row, string field, string reason)
        {
            var error = Assert.Throws<BBadsConfigurationException>(() => LoadWith(table, csv));
            Assert.Multiple(() =>
            {
                Assert.That(error.Table, Is.EqualTo(table));
                Assert.That(error.Row, Is.EqualTo(row));
                Assert.That(error.Field, Is.EqualTo(field));
                Assert.That(error.Message, Does.Contain(reason));
            });
        }

        private static BBadsConfiguration LoadWith(string table, string text) =>
            BBadsConfiguration.Load(name => name == table ? text : ReadSource(name));

        private static string ChangeField(string table, int row, string field, string value)
        {
            var lines = ReadSource(table).Replace("\r\n", "\n").Split('\n');
            var index = Array.IndexOf(lines[0].Split(','), field);
            var cells = lines[row - 1].Split(',');
            cells[index] = value;
            lines[row - 1] = string.Join(",", cells);
            return string.Join("\n", lines);
        }

        private static string ReadSource(string table) => File.ReadAllText(SourcePath(table));

        private static string SourcePath(string table, [CallerFilePath] string testFile = "") =>
            Path.Combine(Path.GetDirectoryName(testFile), "../../configuration/BBads", table + ".csv");
    }
}
