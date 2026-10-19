using System;

namespace AwithGameFrame.Core.Resources
{
    /// <summary>Limits idle retention only; live leases are never evicted to satisfy a cache budget.</summary>
    public sealed class ResourceCacheOptions
    {
        public ResourceCacheOptions(long maxBytes, int maxEntries = 128)
        {
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (maxEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            MaxBytes = maxBytes;
            MaxEntries = maxEntries;
        }

        public long MaxBytes { get; }
        public int MaxEntries { get; }
    }

    public readonly struct ResourceCacheSnapshot
    {
        internal ResourceCacheSnapshot(int entries, long bytes) { Entries = entries; EstimatedBytes = bytes; }
        public int Entries { get; }
        public long EstimatedBytes { get; }
    }
}
