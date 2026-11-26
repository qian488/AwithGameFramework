using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Application.Combat;
using Test.Domain.Expedition;
using JsonException = Newtonsoft.Json.JsonException;

namespace Test.Application.Expedition
{
    public sealed class ExpeditionCatalog
    {
        private static readonly Regex Key = new Regex(@"\A[a-z0-9][a-z0-9-]{0,47}\z", RegexOptions.CultureInvariant);
        private static readonly JsonSerializer Serializer = new JsonSerializer();

        private ExpeditionCatalog(Document document, ExpeditionRoute finalRoute,
            IReadOnlyList<ExpeditionRoute> routes, IReadOnlyList<ExpeditionEquipment> equipment)
        {
            Version = document.Version;
            FirstClearMarks = document.FirstClearMarks;
            UnlockCost = document.UnlockCost;
            FinalRoute = finalRoute;
            Routes = routes;
            Equipment = equipment;
        }

        public string Version { get; }
        public int FirstClearMarks { get; }
        public int UnlockCost { get; }
        public ExpeditionRoute FinalRoute { get; }
        public IReadOnlyList<ExpeditionRoute> Routes { get; }
        public IReadOnlyList<ExpeditionEquipment> Equipment { get; }

        public static async Task<ExpeditionCatalog> LoadAsync(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            Document document;
            try { document = await Serializer.DeserializeAsync<Document>(content).ConfigureAwait(false); }
            catch (JsonException error)
            {
                throw new ConfigurationValidationException($"Rain Pass expedition JSON could not be read. {error.Message}", error);
            }

            if (document == null) throw Invalid("document", "expected a JSON object");
            if (document.SchemaVersion != 1) throw Invalid("schemaVersion", $"expected 1, got {document.SchemaVersion}");
            RequireKey(document.Version, "version");
            if (document.FirstClearMarks <= 0) throw Invalid("firstClearMarks", "expected a positive integer");
            if (document.UnlockCost <= 0) throw Invalid("unlockCost", "expected a positive integer");
            if (document.Routes == null || document.Routes.Length == 0)
                throw Invalid("routes", "expected at least one starting route");
            if (document.Equipment == null || document.Equipment.Length == 0)
                throw Invalid("equipment", "expected at least the 'standard' loadout");

            var finalRoute = ReadRoute(document.FinalRoute, "finalRoute");
            if (finalRoute.Id != ContractSession.CedarId || finalRoute.Map != CombatMapId.CedarGrove)
                throw Invalid("finalRoute", "expected 'cedar-grove' with map 'CedarGrove'");

            var routeIds = new HashSet<string>(StringComparer.Ordinal) { finalRoute.Id };
            var routes = new List<ExpeditionRoute>(document.Routes.Length);
            for (int index = 0; index < document.Routes.Length; index++)
            {
                string path = $"routes[{index}]";
                var route = ReadRoute(document.Routes[index], path);
                if (!routeIds.Add(route.Id)) throw Invalid(path + ".id", $"duplicate route ID '{route.Id}'");
                if (!ExpeditionRules.IsRoute(route.Id)) throw Invalid(path + ".id", $"unsupported starting route '{route.Id}'");
                var expectedMap = route.Id == ExpeditionRules.SaltCausewayId ? CombatMapId.SaltCauseway : CombatMapId.EmberFoundry;
                if (route.Map != expectedMap) throw Invalid(path + ".map", $"expected '{expectedMap}' for '{route.Id}'");
                routes.Add(route);
            }

            var equipmentIds = new HashSet<string>(StringComparer.Ordinal);
            var equipment = new List<ExpeditionEquipment>(document.Equipment.Length);
            for (int index = 0; index < document.Equipment.Length; index++)
            {
                string path = $"equipment[{index}]";
                var loadout = document.Equipment[index];
                if (loadout == null) throw Invalid(path, "expected an equipment object");
                RequireKey(loadout.Id, path + ".id");
                if (!equipmentIds.Add(loadout.Id)) throw Invalid(path + ".id", $"duplicate equipment ID '{loadout.Id}'");
                if (!ExpeditionRules.IsGear(loadout.Id)) throw Invalid(path + ".id", $"unsupported equipment '{loadout.Id}'");
                if (!ExpeditionRules.IsArrowCapacity(loadout.Arrows.GetValueOrDefault()))
                    throw Invalid(path + ".arrows", "expected a positive arrow capacity");
                if (loadout.Posture <= 0) throw Invalid(path + ".posture", "expected a positive integer");
                equipment.Add(new ExpeditionEquipment(loadout.Id, loadout.Arrows.Value, loadout.Posture));
            }
            if (!equipmentIds.Contains("standard")) throw Invalid("equipment", "missing the 'standard' loadout");

            return new ExpeditionCatalog(document, finalRoute, routes.AsReadOnly(), equipment.AsReadOnly());
        }

        private static ExpeditionRoute ReadRoute(RouteEntry entry, string path)
        {
            if (entry == null) throw Invalid(path, "expected a route object");
            RequireKey(entry.Id, path + ".id");
            if (!Enum.TryParse<CombatMapId>(entry.Map, out var map) || Enum.GetName(typeof(CombatMapId), map) != entry.Map)
                throw Invalid(path + ".map", $"unknown combat map '{entry.Map}'");
            return new ExpeditionRoute(entry.Id, map);
        }

        private static void RequireKey(string value, string path)
        {
            if (value == null || !Key.IsMatch(value))
                throw Invalid(path, "expected a lowercase ID of up to 48 ASCII letters, digits or hyphens, starting with a letter or digit");
        }

        private static ConfigurationValidationException Invalid(string path, string message) =>
            new ConfigurationValidationException($"Rain Pass expedition field '{path}': {message}.");

        private sealed class Document
        {
            public int SchemaVersion { get; set; }
            public string Version { get; set; }
            public int FirstClearMarks { get; set; }
            public int UnlockCost { get; set; }
            public RouteEntry[] Routes { get; set; }
            public RouteEntry FinalRoute { get; set; }
            public EquipmentEntry[] Equipment { get; set; }
        }

        private sealed class RouteEntry
        {
            public string Id { get; set; }
            public string Map { get; set; }
        }

        private sealed class EquipmentEntry
        {
            public string Id { get; set; }
            public int? Arrows { get; set; }
            public int Posture { get; set; }
        }
    }

    public sealed class ExpeditionRoute
    {
        internal ExpeditionRoute(string id, CombatMapId map)
        {
            Id = id;
            Map = map;
        }

        public string Id { get; }
        public CombatMapId Map { get; }
    }

    public sealed class ExpeditionEquipment
    {
        internal ExpeditionEquipment(string id, int arrows, int posture)
        {
            Id = id;
            Arrows = arrows;
            Posture = posture;
        }

        public string Id { get; }
        public int Arrows { get; }
        public int Posture { get; }
    }
}
