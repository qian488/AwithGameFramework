using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Extensions.GameSave
{
    /// <summary>
    /// Instance-based, versioned local saves. One checksummed envelope holds data and metadata.
    /// Same-process operations on the same path are serialized, including across service instances.
    /// </summary>
    public sealed class VersionedSaveService<T> where T : class
    {
        private const int EnvelopeVersion = 1;
        private const int HashSize = 32;
        private static readonly byte[] Magic = { (byte)'A', (byte)'W', (byte)'G', (byte)'S' };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly ISerializer serializer;
        private readonly Dictionary<int, SaveMigration> migrations = new Dictionary<int, SaveMigration>();

        public string RootDirectory { get; }
        public int CurrentVersion { get; }
        public int MaxSlots { get; }

        public VersionedSaveService(string rootDirectory, ISerializer serializer, int currentVersion,
            IEnumerable<SaveMigration> migrations = null, int maxSlots = 20)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("An explicit save directory is required.", nameof(rootDirectory));
            if (currentVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentVersion));
            if (maxSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxSlots));
            RootDirectory = Path.GetFullPath(rootDirectory);
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            CurrentVersion = currentVersion;
            MaxSlots = maxSlots;
            if (migrations == null) return;
            foreach (var migration in migrations)
            {
                if (migration == null) throw new ArgumentException("A migration cannot be null.", nameof(migrations));
                if (migration.ToVersion > currentVersion) throw new ArgumentException("A migration exceeds the current schema version.", nameof(migrations));
                if (this.migrations.ContainsKey(migration.FromVersion)) throw new ArgumentException("Duplicate migration source version.", nameof(migrations));
                this.migrations.Add(migration.FromVersion, migration);
            }
        }

        /// <summary>The host can use this path to export a slot. Its previous revision is at path + '.bak'.</summary>
        public string GetFilePath(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxSlots) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return Path.Combine(RootDirectory, "slot-" + slotIndex + ".save");
        }

        public async Task<SaveMetadata> SaveAsync(int slotIndex, T data, string description = "", CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var path = GetFilePath(slotIndex);
            var gate = SaveFileGates.ForPath(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool preserveBackup = false;
                if (File.Exists(path))
                {
                    try
                    {
                        var existing = Decode(ReadBytes(path), slotIndex);
                        RejectFutureVersion(existing.Metadata.SchemaVersion);
                    }
                    catch (InvalidDataException) { preserveBackup = true; }
                }
                var payload = await serializer.SerializeAsync(data).ConfigureAwait(false);
                if (payload == null) throw new InvalidOperationException("The serializer returned no save data.");
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = new SaveMetadata(slotIndex, CurrentVersion, DateTime.UtcNow, description ?? "");
                await CommitAsync(path, Encode(metadata, payload), preserveBackup, cancellationToken).ConfigureAwait(false);
                return metadata;
            }
            finally { gate.Release(); }
        }

        /// <summary>Missing slots return NotFound. Unrecoverable corruption and unsupported versions throw.</summary>
        public async Task<SaveLoadResult<T>> LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
        {
            var path = GetFilePath(slotIndex);
            var gate = SaveFileGates.ForPath(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Snapshot snapshot = null;
                InvalidDataException primaryError = null;
                if (File.Exists(path))
                {
                    try { snapshot = await ReadSnapshotAsync(path, slotIndex, cancellationToken).ConfigureAwait(false); }
                    catch (InvalidDataException error) { primaryError = error; }
                }
                if (snapshot != null)
                {
                    if (snapshot.WasMigrated)
                        await CommitAsync(path, Encode(snapshot.Metadata, snapshot.Payload), false, cancellationToken).ConfigureAwait(false);
                    return new SaveLoadResult<T>(SaveLoadStatus.Loaded, snapshot.Data, snapshot.Metadata, snapshot.WasMigrated);
                }

                var backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                {
                    if (primaryError != null) throw new SaveCorruptedException(slotIndex, primaryError);
                    return new SaveLoadResult<T>(SaveLoadStatus.NotFound);
                }
                try { snapshot = await ReadSnapshotAsync(backupPath, slotIndex, cancellationToken).ConfigureAwait(false); }
                catch (InvalidDataException backupError)
                {
                    throw new SaveCorruptedException(slotIndex, primaryError == null ? (Exception)backupError : new AggregateException(primaryError, backupError));
                }
                // Never replace a healthy backup with the corrupt primary during recovery.
                await CommitAsync(path, Encode(snapshot.Metadata, snapshot.Payload), true, cancellationToken).ConfigureAwait(false);
                return new SaveLoadResult<T>(SaveLoadStatus.RecoveredFromBackup, snapshot.Data, snapshot.Metadata, snapshot.WasMigrated);
            }
            finally { gate.Release(); }
        }

        public async Task DeleteAsync(int slotIndex, CancellationToken cancellationToken = default)
        {
            var path = GetFilePath(slotIndex);
            var gate = SaveFileGates.ForPath(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Remove the backup first so an interrupted delete cannot resurrect a deleted primary.
                File.Delete(path + ".bak");
                File.Delete(path);
            }
            finally { gate.Release(); }
        }

        /// <summary>Validates, recovers and upgrades each occupied slot before returning its metadata.</summary>
        public async Task<IReadOnlyList<SaveMetadata>> ListSlotsAsync(CancellationToken cancellationToken = default)
        {
            var slots = new List<SaveMetadata>();
            for (int slotIndex = 0; slotIndex < MaxSlots; slotIndex++)
            {
                var result = await LoadAsync(slotIndex, cancellationToken).ConfigureAwait(false);
                if (result.Status != SaveLoadStatus.NotFound) slots.Add(result.Metadata);
            }
            return slots.AsReadOnly();
        }

        private async Task<Snapshot> ReadSnapshotAsync(string path, int slotIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = Decode(ReadBytes(path), slotIndex);
            int storedVersion = envelope.Metadata.SchemaVersion;
            RejectFutureVersion(storedVersion);
            byte[] payload = envelope.Payload;
            for (int version = storedVersion; version < CurrentVersion; version++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!migrations.TryGetValue(version, out var migration))
                    throw new SaveVersionException(storedVersion, CurrentVersion, $"Missing save migration from schema {version} to {version + 1}.");
                try
                {
                    payload = await migration.ApplyAsync(payload, cancellationToken).ConfigureAwait(false);
                    if (payload == null) throw new InvalidOperationException("The migration returned no data.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    throw new SaveVersionException(storedVersion, CurrentVersion, $"Save migration {version} to {version + 1} failed.", error);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            T data;
            try
            {
                data = await serializer.DeserializeAsync<T>(payload).ConfigureAwait(false);
                if (data == null) throw new InvalidDataException("The save payload is null.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (storedVersion != CurrentVersion)
                    throw new SaveVersionException(storedVersion, CurrentVersion, "The migrated save cannot be deserialized.", error);
                throw new InvalidDataException("The save payload cannot be deserialized.", error);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = new SaveMetadata(slotIndex, CurrentVersion, envelope.Metadata.SavedAtUtc, envelope.Metadata.Description);
            return new Snapshot { Data = data, Metadata = metadata, Payload = payload, WasMigrated = storedVersion != CurrentVersion };
        }

        private void RejectFutureVersion(int version)
        {
            if (version > CurrentVersion)
                throw new SaveVersionException(version, CurrentVersion, $"Save schema {version} is newer than supported schema {CurrentVersion}.");
        }

        private static byte[] ReadBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var output = new MemoryStream())
            {
                stream.CopyTo(output);
                return output.ToArray();
            }
        }

        private static byte[] Encode(SaveMetadata metadata, byte[] payload)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = new BinaryWriter(output, Utf8, leaveOpen: true))
                {
                    writer.Write(Magic);
                    writer.Write(EnvelopeVersion);
                    writer.Write(metadata.SchemaVersion);
                    writer.Write(metadata.SlotIndex);
                    writer.Write(metadata.SavedAtUtc.Ticks);
                    writer.Write(metadata.Description);
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }
                var content = output.ToArray();
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(content);
                    output.Write(hash, 0, hash.Length);
                }
                return output.ToArray();
            }
        }

        private static Envelope Decode(byte[] bytes, int expectedSlot)
        {
            if (bytes.Length < Magic.Length + 4 * sizeof(int) + sizeof(long) + 1 + HashSize)
                throw new InvalidDataException("The save envelope is truncated.");
            int contentLength = bytes.Length - HashSize;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes, 0, contentLength);
                for (int i = 0; i < hash.Length; i++)
                    if (hash[i] != bytes[contentLength + i]) throw new InvalidDataException("The save checksum is invalid.");
            }
            try
            {
                using (var input = new MemoryStream(bytes, 0, contentLength, writable: false))
                using (var reader = new BinaryReader(input, Utf8))
                {
                    foreach (byte value in Magic)
                        if (reader.ReadByte() != value) throw new InvalidDataException("The save signature is invalid.");
                    if (reader.ReadInt32() != EnvelopeVersion) throw new InvalidDataException("The save envelope version is unsupported.");
                    int schemaVersion = reader.ReadInt32();
                    int slotIndex = reader.ReadInt32();
                    if (schemaVersion < 1 || slotIndex != expectedSlot) throw new InvalidDataException("The save schema or slot identifier is invalid.");
                    var savedAtUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                    var description = reader.ReadString();
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength < 0 || payloadLength != input.Length - input.Position) throw new InvalidDataException("The save payload length is invalid.");
                    return new Envelope { Metadata = new SaveMetadata(slotIndex, schemaVersion, savedAtUtc, description), Payload = reader.ReadBytes(payloadLength) };
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("The save envelope is truncated.", error); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("The save description is not valid UTF-8.", error); }
            catch (ArgumentException error) { throw new InvalidDataException("The save metadata is invalid.", error); }
            catch (FormatException error) { throw new InvalidDataException("The save metadata is malformed.", error); }
        }

        private static async Task CommitAsync(string path, byte[] bytes, bool preserveBackup, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = Path.Combine(Path.GetDirectoryName(path), ".awith-slot-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporaryPath, path, preserveBackup ? null : path + ".bak");
                else File.Move(temporaryPath, path);
                // The replacement is the commit point. Cancellation after it must not report an unsaved operation.
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private sealed class Envelope
        {
            public SaveMetadata Metadata;
            public byte[] Payload;
        }

        private sealed class Snapshot
        {
            public T Data;
            public SaveMetadata Metadata;
            public byte[] Payload;
            public bool WasMigrated;
        }
    }

    // Non-generic so services using different DTO types still coordinate a shared file.
    internal static class SaveFileGates
    {
        private static readonly SemaphoreSlim[] Gates = CreateGates();
        internal static SemaphoreSlim ForPath(string path) => Gates[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(path) % Gates.Length];
        private static SemaphoreSlim[] CreateGates()
        {
            var gates = new SemaphoreSlim[64];
            for (int i = 0; i < gates.Length; i++) gates[i] = new SemaphoreSlim(1, 1);
            return gates;
        }
    }
}
