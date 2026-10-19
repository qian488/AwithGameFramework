using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Scenes;
using UnityEngine;

namespace AwithGameFrame.Samples
{
    public static class AssetBundleSmoke
    {
        public static async Task RunAsync(GameHost host)
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, "-awith-content-catalog");
            var catalogPath = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Path.Combine(Application.streamingAssetsPath, "AwithContent/bundle-catalog.json");
            var catalog = UnityBundleCatalog.FromJson(File.ReadAllText(catalogPath));
            var loader = new UnityAssetBundleLoader(catalog, name => Path.Combine(Path.GetDirectoryName(catalogPath), name));
            var resources = new ResourceStore(loader, host.Dispatcher);
            var scenes = new SceneService(host.Context, new UnityBundleSceneBackend(), resources);
            try
            {
                await using (var scope = new ResourceScope(resources))
                {
                    var prefab = await scope.LoadAsync<GameObject>("content/prefab");
                    var material = await scope.LoadAsync<Material>("content/material");
                    if (prefab.Value.GetComponent<MeshRenderer>().sharedMaterial != material.Value || loader.LoadedBundleCount < 2)
                        throw new InvalidOperationException("Bundle dependency was not shared between addresses.");
                    var scene = await scenes.LoadAsync("content/scene");
                    if (!scene.IsLoaded) throw new InvalidOperationException("Bundled scene was not loaded.");
                    await scene.UnloadAsync();
                }
                if (loader.LoadedBundleCount != 0) throw new InvalidOperationException("Bundle dependencies remained leased.");
                // Independent stores share native bundles, which Unity forbids loading twice.
                await using (var second = new ResourceStore(new UnityAssetBundleLoader(catalog, name => Path.Combine(Path.GetDirectoryName(catalogPath), name)), host.Dispatcher))
                await using (var scope = new ResourceScope(second))
                await using (var other = new ResourceScope(resources))
                {
                    var a = await scope.LoadAsync<Material>("content/material");
                    var b = await other.LoadAsync<Material>("content/material");
                    if (!ReferenceEquals(a.Value, b.Value)) throw new InvalidOperationException("Stores did not share a native bundle asset.");
                }
                if (loader.LoadedBundleCount != 0) throw new InvalidOperationException("Second-store bundle ownership leaked.");
                Debug.Log("UNITY_ASSET_BUNDLE_PASSED");
            }
            finally
            {
                try { await scenes.StopAsync(); }
                finally { await resources.DisposeAsync(); }
            }
        }
    }
}
