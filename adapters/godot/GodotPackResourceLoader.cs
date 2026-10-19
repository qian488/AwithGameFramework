using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using Godot;

namespace AwithGameFrame.Godot
{
    public sealed class GodotPackCatalog
    {
        public string ResourceRoot { get; set; }
        public string PackKey { get; set; }
        public string PackSha256 { get; set; }
        public Dictionary<string, string> Resources { get; set; }
        public Dictionary<string, string> UidPaths { get; set; } = new Dictionary<string, string>();

        public static GodotPackCatalog FromJson(string json)
        {
            var catalog = JsonSerializer.Deserialize<GodotPackCatalog>(json) ?? throw new InvalidDataException("Invalid pack catalog.");
            catalog.Validate(); return catalog;
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(ResourceRoot) || !ResourceRoot.StartsWith("res://awith-content/", StringComparison.Ordinal) ||
                !ResourceRoot.EndsWith("/", StringComparison.Ordinal) || ResourceRoot.Length <= "res://awith-content/".Length ||
                !SafeRelativePath(ResourceRoot.Substring("res://".Length, ResourceRoot.Length - "res://".Length - 1)) ||
                !SafeRelativePath(PackKey) || PackSha256 == null || PackSha256.Length != 64 || Resources == null || Resources.Count == 0 || UidPaths == null)
                throw new InvalidDataException("Incomplete or unsafe pack catalog.");
            foreach (var digit in PackSha256) if (!Uri.IsHexDigit(digit)) throw new InvalidDataException("Invalid pack hash.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Resources)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || !SafeResourcePath(entry.Value) || !paths.Add(entry.Value))
                    throw new InvalidDataException("Unsafe or duplicate pack address.");
            }
            var ids = new HashSet<long>();
            foreach (var entry in UidPaths)
            {
                long id = ResourceUid.TextToId(entry.Key);
                if (id == ResourceUid.InvalidId || !ids.Add(id) || !SafeResourcePath(entry.Value))
                    throw new InvalidDataException("Unsafe resource UID mapping.");
            }
        }

        private bool SafeResourcePath(string path) => path != null && path.StartsWith(ResourceRoot, StringComparison.Ordinal)
            && SafeRelativePath(path.Substring("res://".Length));
        private static bool SafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0) return false;
            foreach (var segment in path.Split('/')) if (segment.Length == 0 || segment == "." || segment == "..") return false;
            return true;
        }
    }

    /// <summary>Mount immutable namespaces, then lease native resources. Godot cannot unmount PCKs before process exit.</summary>
    public sealed class GodotPackResourceLoader : IResourceLoader
    {
        private static readonly Dictionary<string, string> Mounted = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string packPath;
        private readonly string resourceRoot;
        private readonly string expectedHash;
        private readonly Dictionary<string, string> addresses;
        private readonly Dictionary<string, string> uids;
        private readonly GodotThreadedResourceLoader inner;
        private readonly int thread = System.Environment.CurrentManagedThreadId;

        public GodotPackResourceLoader(GodotPackCatalog catalog, string packPath, Func<Resource, long> estimateBytes = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalog.Validate();
            this.packPath = Path.GetFullPath(packPath ?? throw new ArgumentNullException(nameof(packPath)));
            resourceRoot = catalog.ResourceRoot;
            expectedHash = catalog.PackSha256;
            addresses = new Dictionary<string, string>(catalog.Resources, StringComparer.Ordinal);
            uids = new Dictionary<string, string>(catalog.UidPaths, StringComparer.Ordinal);
            inner = new GodotThreadedResourceLoader(estimateBytes);
        }

        public static int MountedPackCount => Mounted.Count;

        public void Mount()
        {
            Verify();
            if (Mounted.TryGetValue(resourceRoot, out var installed))
            {
                if (!installed.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A different pack already owns this namespace; use a new version namespace or restart.");
                ValidateMountedCatalog();
                return;
            }
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(packPath))
            {
                var actual = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
                if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PCK SHA-256 mismatch.");
            }
            foreach (var path in addresses.Values)
                if (ResourceLoader.Exists(path)) throw new InvalidOperationException("Pack address already exists in the virtual filesystem: " + path);
            ValidateUidOwners();
            if (!ProjectSettings.LoadResourcePack(packPath, replaceFiles: false)) throw new InvalidDataException("Godot refused the resource pack.");
            // The mount itself is process-owned, including after a later resource load fails.
            Mounted.Add(resourceRoot, expectedHash);
            ValidateMountedCatalog();
        }

        private void ValidateMountedCatalog()
        {
            foreach (var path in addresses.Values)
                if (!ResourceLoader.Exists(path)) throw new InvalidDataException("Mounted pack is missing an address: " + path);
            foreach (var path in uids.Values)
                if (!ResourceLoader.Exists(path)) throw new InvalidDataException("Mounted pack is missing a UID target: " + path);
            ValidateUidOwners();
            foreach (var entry in uids)
            {
                long id = ResourceUid.TextToId(entry.Key);
                if (!ResourceUid.HasId(id)) ResourceUid.AddId(id, entry.Value);
            }
        }

        private void ValidateUidOwners()
        {
            foreach (var entry in uids)
            {
                long id = ResourceUid.TextToId(entry.Key);
                if (ResourceUid.HasId(id) && ResourceUid.GetIdPath(id) != entry.Value)
                    throw new InvalidOperationException("A different resource already owns pack UID: " + entry.Key);
            }
        }

        public Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            Verify(); cancellationToken.ThrowIfCancellationRequested();
            if (!addresses.TryGetValue(key, out var path)) throw new FileNotFoundException("Unknown pack asset: " + key);
            Mount();
            return inner.LoadAsync(path, resourceType, cancellationToken);
        }

        private void Verify()
        { if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Godot pack operations require the creating engine thread."); }
    }
}
