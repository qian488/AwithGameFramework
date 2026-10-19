using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;

namespace AwithGameFrame.Gameplay.Settings
{
    [Serializable]
    public sealed class UserInputBinding
    {
        public string ContextId { get; set; }
        public string Action { get; set; }
        public string Control { get; set; }
        public float Scale { get; set; } = 1;
        public float DeadZone { get; set; } = 0.1f;
    }

    /// <summary>Serializable desired settings. Service snapshots are independent copies.</summary>
    [Serializable]
    public sealed class UserSettingsData
    {
        public string Language { get; set; } = "en";
        public float MasterVolume { get; set; } = 1;
        public bool MasterMuted { get; set; }
        public Dictionary<string, float> BusVolumes { get; set; } = new Dictionary<string, float>(StringComparer.Ordinal);
        public Dictionary<string, bool> BusMuted { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
        public List<UserInputBinding> InputBindings { get; set; } = new List<UserInputBinding>();
    }

    public interface IUserSettingsStore
    {
        /// <summary>Null means no saved settings. Corruption and incompatible versions must throw.</summary>
        Task<UserSettingsData> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(UserSettingsData settings, CancellationToken cancellationToken);
    }

    /// <summary>Atomic local settings, checksums, backup recovery and explicit schema migrations.</summary>
    public sealed class VersionedUserSettingsStore : IUserSettingsStore
    {
        private readonly VersionedSaveService<UserSettingsData> saves;
        private readonly int slot;
        public VersionedUserSettingsStore(VersionedSaveService<UserSettingsData> saves, int slot = 0)
        {
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            saves.GetFilePath(slot);
            this.slot = slot;
        }
        public SaveLoadStatus LastLoadStatus { get; private set; }
        public bool WasMigrated { get; private set; }
        public async Task<UserSettingsData> LoadAsync(CancellationToken cancellationToken)
        {
            var loaded = await saves.LoadAsync(slot, cancellationToken).ConfigureAwait(false);
            LastLoadStatus = loaded.Status;
            WasMigrated = loaded.WasMigrated;
            return loaded.Status == SaveLoadStatus.NotFound ? null : loaded.Data;
        }
        public async Task SaveAsync(UserSettingsData settings, CancellationToken cancellationToken)
        { await saves.SaveAsync(slot, settings, "User settings", cancellationToken).ConfigureAwait(false); }
    }

    public sealed class UserSettingsChanged
    {
        public UserSettingsService Service { get; }
        public long Revision { get; }
        public UserSettingsData Settings { get; }
        internal UserSettingsChanged(UserSettingsService service, long revision, UserSettingsData settings)
        { Service = service; Revision = revision; Settings = settings; }
    }
}
