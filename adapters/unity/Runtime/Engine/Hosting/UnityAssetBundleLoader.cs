using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using UnityEngine;

namespace AwithGameFrame.Unity.Hosting
{
    /// <summary>All native calls and releases require the Unity thread. Keep leases until instances are destroyed.</summary>
    public sealed class UnityAssetBundleLoader : IResourceLoader
    {
        private sealed class BundleLease
        {
            internal string Path;
            internal Task<AssetBundle> Loading;
            internal int References;
        }

        private static readonly Dictionary<string, BundleLease> Shared = new Dictionary<string, BundleLease>(
            Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly Dictionary<string, UnityBundleInfo> bundles = new Dictionary<string, UnityBundleInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UnityBundleAsset> assets = new Dictionary<string, UnityBundleAsset>(StringComparer.Ordinal);
        private readonly Func<string, string> resolveBundle;
        private readonly int thread = Environment.CurrentManagedThreadId;

        public UnityAssetBundleLoader(UnityBundleCatalog catalog, Func<string, string> resolveBundle)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalog.Validate();
            this.resolveBundle = resolveBundle ?? throw new ArgumentNullException(nameof(resolveBundle));
            foreach (var bundle in catalog.bundles) bundles.Add(bundle.name, new UnityBundleInfo { name = bundle.name, dependencies = (string[])bundle.dependencies.Clone() });
            foreach (var asset in catalog.assets) assets.Add(asset.key, new UnityBundleAsset { key = asset.key, bundle = asset.bundle, assetPath = asset.assetPath, scene = asset.scene });
        }

        public int LoadedBundleCount { get { Verify(); return Shared.Count; } }

        public async Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            Verify(); cancellationToken.ThrowIfCancellationRequested();
            if (!assets.TryGetValue(key, out var address)) throw new FileNotFoundException("Unknown bundle asset: " + key);
            if (address.scene != (resourceType == typeof(UnityBundleScene)) || (!address.scene && !typeof(UnityEngine.Object).IsAssignableFrom(resourceType)))
                throw new ArgumentException("Bundle asset and requested type do not match.", nameof(resourceType));
            var claims = new List<BundleLease>();
            try
            {
                var ordered = new List<string>();
                Collect(address.bundle, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ordered);
                AssetBundle root = null;
                foreach (var name in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.GetFullPath(resolveBundle(name));
                    if (!Shared.TryGetValue(path, out var claim))
                    {
                        claim = new BundleLease { Path = path };
                        Shared.Add(path, claim);
                        claim.Loading = LoadBundleAsync(path);
                    }
                    claim.References++; claims.Add(claim);
                    // Native bundle requests cannot be interrupted. Never abandon ownership of a late result.
                    var loaded = await claim.Loading;
                    Verify();
                    if (name.Equals(address.bundle, StringComparison.OrdinalIgnoreCase)) root = loaded;
                }
                cancellationToken.ThrowIfCancellationRequested();
                object value;
                long estimated = 0;
                if (address.scene)
                {
                    string scenePath = null;
                    foreach (var path in root.GetAllScenePaths()) if (path.Equals(address.assetPath, StringComparison.OrdinalIgnoreCase)) scenePath = path;
                    value = new UnityBundleScene(scenePath ?? throw new FileNotFoundException("Scene not found in bundle: " + address.assetPath));
                }
                else
                {
                    var request = root.LoadAssetAsync(address.assetPath, resourceType);
                    await CompleteAsync(request);
                    var asset = request.asset;
                    if (asset == null || !resourceType.IsInstanceOfType(asset)) throw new FileNotFoundException("Asset not found in bundle: " + address.assetPath);
                    value = asset;
                    estimated = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(asset);
                }
                return new LoadedResource(value, () => { Verify(); Release(claims); return Task.CompletedTask; }, estimated);
            }
            catch (Exception failure)
            {
                try { Verify(); Release(claims); }
                catch (Exception cleanup) { throw new AggregateException("Bundle loading and cleanup reported errors.", failure, cleanup); }
                throw;
            }
        }

        private void Collect(string name, HashSet<string> visited, List<string> ordered)
        {
            var bundle = bundles[name];
            if (!visited.Add(bundle.name)) return;
            foreach (var dependency in bundle.dependencies) Collect(dependency, visited, ordered);
            ordered.Add(bundle.name);
        }

        private void Verify() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Asset bundles require the creating Unity thread."); }
        private static async Task<AssetBundle> LoadBundleAsync(string path)
        {
            var request = AssetBundle.LoadFromFileAsync(path);
            await CompleteAsync(request);
            return request.assetBundle != null ? request.assetBundle : throw new InvalidDataException("Unable to load bundle: " + path);
        }
        private static Task CompleteAsync(AsyncOperation operation)
        {
            if (operation.isDone) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }
        private static void Release(List<BundleLease> claims)
        {
            List<Exception> errors = null;
            for (int index = claims.Count - 1; index >= 0; index--)
            {
                var claim = claims[index];
                if (--claim.References != 0) continue;
                Shared.Remove(claim.Path);
                try
                {
                    if (claim.Loading.Status == TaskStatus.RanToCompletion && claim.Loading.Result != null) claim.Loading.Result.Unload(true);
                }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            claims.Clear();
            if (errors != null) throw new AggregateException("Bundle release reported errors.", errors);
        }
    }
}
