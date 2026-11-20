using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Domain.Combat;

namespace Test.Application.Combat
{
    public static class TrialCatalog
    {
        private const string ResourceName = "Test.RainPass.Opponents.json";
        private const string SchemaHash = "b614cdd5c458e0e1d6e93fa6aab9170f435b525814343db25fd226b39bee9c36";

        public static async Task<IReadOnlyList<TrialOpponent>> LoadAsync()
        {
            using var resource = typeof(TrialCatalog).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new FileNotFoundException($"Missing Rain Pass configuration resource {ResourceName}.");
            using var buffer = new MemoryStream();
            await resource.CopyToAsync(buffer).ConfigureAwait(false);
            return await LoadAsync(buffer.ToArray()).ConfigureAwait(false);
        }

        public static async Task<IReadOnlyList<TrialOpponent>> LoadAsync(byte[] data)
        {
            var table = await ConfigTable<Row>.LoadAsync(data, new JsonSerializer(), SchemaHash, row => row.Id).ConfigureAwait(false);
            if (table.Count == 0) throw new ConfigurationValidationException("Rain Pass opponents: rows must contain at least one opponent.");
            var opponents = new List<TrialOpponent>(table.Count);
            foreach (var row in table.Rows)
            {
                RequireRange(row, "id", row.Id, 1, int.MaxValue);
                RequireText(row, "name", row.Name, 30);
                RequireText(row, "description", row.Description, 120);
                RequireRange(row, "health", row.Health, 1, 1000);
                RequireRange(row, "posture", row.Posture, 1, 1000);
                RequireRange(row, "attackIntervalTicks", row.AttackIntervalTicks, 1, 600);
                RequireRange(row, "windupTicks", row.WindupTicks, 12, 180);
                RequireRange(row, "recoveryTicks", row.RecoveryTicks, 1, 180);
                RequireRange(row, "damage", row.Damage, 1, 100);
                RequireRange(row, "pressure", row.Pressure, 1, 100);
                var strike = new Strike(row.WindupTicks, 4, row.RecoveryTicks, row.Damage, row.Pressure, 2.55f, 100);
                opponents.Add(new TrialOpponent(row.Id, row.Name, row.Description, row.Health, row.Posture, row.AttackIntervalTicks, strike));
            }
            return opponents.AsReadOnly();
        }

        private static void RequireRange(Row row, string field, int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
                throw new ConfigurationValidationException($"Rain Pass opponent ID {row.Id}, field '{field}': expected {minimum}–{maximum}, got {value}.");
        }

        private static void RequireText(Row row, string field, string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Contains('\r') || value.Contains('\n'))
                throw new ConfigurationValidationException($"Rain Pass opponent ID {row.Id}, field '{field}': expected nonempty text on one line (up to {maximum} characters).");
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public int Health { get; set; }
            public int Posture { get; set; }
            public int WindupTicks { get; set; }
            public int RecoveryTicks { get; set; }
            public int AttackIntervalTicks { get; set; }
            public int Damage { get; set; }
            public int Pressure { get; set; }
        }
    }
}
