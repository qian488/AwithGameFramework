using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Application.Expedition;
using Test.Domain.Combat;

namespace Test.Application.Tests
{
    public sealed class ExpeditionCatalogTests
    {
        [Test]
        public async Task ShippedSourceLoadsWithTheAgreedRoutesAndSupplies()
        {
            var catalog = await ExpeditionCatalog.LoadAsync(ReadSource());

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Version, Is.EqualTo("v1"));
                Assert.That(catalog.FirstClearMarks, Is.EqualTo(1));
                Assert.That(catalog.UnlockCost, Is.EqualTo(1));
                Assert.That(catalog.Routes.Select(route => (route.Id, route.Map)), Is.EqualTo(new[]
                {
                    ("salt-causeway", CombatMapId.SaltCauseway),
                    ("ember-foundry", CombatMapId.EmberFoundry)
                }));
                Assert.That(catalog.FinalRoute.Id, Is.EqualTo(ContractSession.CedarId));
                Assert.That(catalog.FinalRoute.Map, Is.EqualTo(CombatMapId.CedarGrove));
                Assert.That(catalog.Equipment.Select(item => (item.Id, item.Arrows, item.Posture)), Is.EqualTo(new[]
                {
                    ("standard", BowRules.QuiverCapacity, 100),
                    ("guard", 4, 125),
                    ("archer", 10, 80)
                }));
            });
        }

        [Test]
        public async Task ReadsRevisedPolicyFromBytes()
        {
            var source = JsonNode.Parse(ReadSource());
            source["version"] = "v2";
            source["firstClearMarks"] = 2;
            source["unlockCost"] = 3;
            source["equipment"][0]["arrows"] = 0;

            var catalog = await ExpeditionCatalog.LoadAsync(Encoding.UTF8.GetBytes(source.ToJsonString()));

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Version, Is.EqualTo("v2"));
                Assert.That(catalog.FirstClearMarks, Is.EqualTo(2));
                Assert.That(catalog.UnlockCost, Is.EqualTo(3));
                Assert.That(catalog.Equipment[0].Arrows, Is.Zero);
            });
        }

        [TestCase("schemaVersion", "2", "schemaVersion")]
        [TestCase("version", "\"\"", "version")]
        [TestCase("unlockCost", "0", "unlockCost")]
        [TestCase("finalRoute.map", "\"EmberFoundry\"", "finalRoute")]
        [TestCase("routes.0.map", "\"1\"", "routes[0].map")]
        [TestCase("routes.1.id", "\"salt-causeway\"", "routes[1].id")]
        [TestCase("equipment.1.id", "\"standard\"", "equipment[1].id")]
        [TestCase("equipment.1.arrows", "null", "equipment[1].arrows")]
        [TestCase("equipment.2.posture", "-1", "equipment[2].posture")]
        public void RejectsInvalidSourceWithFieldDiagnostic(string field, string replacement, string diagnostic)
        {
            var document = JsonNode.Parse(ReadSource());
            var path = field.Split('.');
            var parent = document;
            foreach (string part in path.Take(path.Length - 1))
                parent = int.TryParse(part, out int index) ? parent[index] : parent[part];
            parent[path[path.Length - 1]] = JsonNode.Parse(replacement);

            var error = Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ExpeditionCatalog.LoadAsync(Encoding.UTF8.GetBytes(document.ToJsonString())));

            Assert.That(error.Message, Does.Contain(diagnostic));
        }

        [Test]
        public void ReportsMalformedJson()
        {
            var error = Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ExpeditionCatalog.LoadAsync(Encoding.UTF8.GetBytes("{ \"routes\": [")));

            Assert.That(error.Message, Does.Contain("expedition JSON"));
            Assert.That(error.InnerException, Is.Not.Null);
        }

        private static byte[] ReadSource([CallerFilePath] string testFile = "") =>
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(testFile), "../../configuration/expedition.json"));
    }
}
