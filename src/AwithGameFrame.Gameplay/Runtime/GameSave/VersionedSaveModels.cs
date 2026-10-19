using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Extensions.GameSave
{
    public enum SaveLoadStatus
    {
        NotFound,
        Loaded,
        RecoveredFromBackup
    }

    /// <summary>Metadata committed in the same file as the game data.</summary>
    public sealed class SaveMetadata
    {
        public int SlotIndex { get; }
        public int SchemaVersion { get; }
        public DateTime SavedAtUtc { get; }
        public string Description { get; }

        internal SaveMetadata(int slotIndex, int schemaVersion, DateTime savedAtUtc, string description)
        {
            SlotIndex = slotIndex;
            SchemaVersion = schemaVersion;
            SavedAtUtc = savedAtUtc;
            Description = description;
        }
    }

    public sealed class SaveLoadResult<T> where T : class
    {
        public SaveLoadStatus Status { get; }
        public T Data { get; }
        public SaveMetadata Metadata { get; }
        public bool WasMigrated { get; }

        internal SaveLoadResult(SaveLoadStatus status, T data = null, SaveMetadata metadata = null, bool wasMigrated = false)
        {
            Status = status;
            Data = data;
            Metadata = metadata;
            WasMigrated = wasMigrated;
        }
    }

    /// <summary>A single schema upgrade. Delegates must return serialized data for ToVersion.</summary>
    public sealed class SaveMigration
    {
        public int FromVersion { get; }
        public int ToVersion { get; }
        private readonly Func<byte[], CancellationToken, Task<byte[]>> migrate;

        public SaveMigration(int fromVersion, int toVersion, Func<byte[], CancellationToken, Task<byte[]>> migrate)
        {
            if (fromVersion < 1) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            if (toVersion != fromVersion + 1) throw new ArgumentException("A migration must advance exactly one schema version.", nameof(toVersion));
            this.migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }

        internal Task<byte[]> ApplyAsync(byte[] data, CancellationToken cancellationToken) => migrate(data, cancellationToken);
    }

    public sealed class SaveCorruptedException : IOException
    {
        public int SlotIndex { get; }
        public SaveCorruptedException(int slotIndex, Exception innerException)
            : base($"Save slot {slotIndex} has no valid primary file or backup.", innerException)
        {
            SlotIndex = slotIndex;
        }
    }

    /// <summary>Unsupported schema or failed migration. These errors never fall back to an older backup.</summary>
    public sealed class SaveVersionException : InvalidOperationException
    {
        public int StoredVersion { get; }
        public int CurrentVersion { get; }

        public SaveVersionException(int storedVersion, int currentVersion, string message, Exception innerException = null)
            : base(message, innerException)
        {
            StoredVersion = storedVersion;
            CurrentVersion = currentVersion;
        }
    }
}
