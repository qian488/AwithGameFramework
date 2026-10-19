using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Data.Configuration
{
    public sealed class ConfigurationValidationException : IOException
    {
        public ConfigurationValidationException(string message) : base(message) { }
        public ConfigurationValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>Serialized configuration document: { "schemaHash": "sha256hex", "rows": [...] }.</summary>
    public sealed class ConfigTableDocument<T>
    {
        public string SchemaHash { get; set; }
        public List<T> Rows { get; set; }
    }

    /// <summary>Validated typed table. Treat row DTOs as immutable after publication.</summary>
    public sealed class ConfigTable<T>
    {
        private readonly Dictionary<int, T> byId;
        private readonly Func<T, int> getId;
        public string SchemaHash { get; }
        public IReadOnlyList<T> Rows { get; }
        public int Count => Rows.Count;
        public T this[int id] => byId[id];

        private ConfigTable(string schemaHash, List<T> rows, Func<T, int> getId)
        {
            SchemaHash = schemaHash;
            this.getId = getId;
            byId = new Dictionary<int, T>();
            var snapshot = new List<T>(rows);
            for (int index = 0; index < snapshot.Count; index++)
            {
                var row = snapshot[index];
                if (ReferenceEquals(row, null)) throw new ConfigurationValidationException($"Configuration row {index} is null.");
                int id = getId(row);
                if (byId.ContainsKey(id)) throw new ConfigurationValidationException($"Duplicate configuration ID {id}.");
                byId.Add(id, row);
            }
            Rows = new ReadOnlyCollection<T>(snapshot);
        }

        public static async Task<ConfigTable<T>> LoadAsync(byte[] data, ISerializer serializer, string expectedSchemaHash,
            Func<T, int> getId, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            if (getId == null) throw new ArgumentNullException(nameof(getId));
            if (!IsSha256(expectedSchemaHash)) throw new ArgumentException("An expected SHA-256 schema hash is required.", nameof(expectedSchemaHash));
            cancellationToken.ThrowIfCancellationRequested();
            ConfigTableDocument<T> document;
            try { document = await serializer.DeserializeAsync<ConfigTableDocument<T>>(data).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { throw new ConfigurationValidationException("The configuration document cannot be deserialized.", error); }
            cancellationToken.ThrowIfCancellationRequested();
            if (document == null || document.Rows == null) throw new ConfigurationValidationException("A configuration document must contain rows.");
            if (!string.Equals(document.SchemaHash, expectedSchemaHash, StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationValidationException($"Configuration schema mismatch: expected {expectedSchemaHash}, got {document.SchemaHash ?? "<missing>"}.");
            return new ConfigTable<T>(document.SchemaHash, document.Rows, getId);
        }

        public bool ContainsId(int id) => byId.ContainsKey(id);
        public bool TryGet(int id, out T row) => byId.TryGetValue(id, out row);

        public void ValidateReferences<TTarget>(ConfigTable<TTarget> target, Func<T, int> getReferenceId,
            string referenceName, bool allowZero = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (getReferenceId == null) throw new ArgumentNullException(nameof(getReferenceId));
            if (string.IsNullOrWhiteSpace(referenceName)) throw new ArgumentException("A reference name is required.", nameof(referenceName));
            foreach (var row in Rows)
            {
                int targetId = getReferenceId(row);
                if (allowZero && targetId == 0) continue;
                if (!target.ContainsId(targetId))
                    throw new ConfigurationValidationException($"Configuration row {getId(row)} reference '{referenceName}' points to missing ID {targetId}.");
            }
        }

        private static bool IsSha256(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char value in hash)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F'))) return false;
            return true;
        }
    }
}
