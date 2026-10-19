using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwithGameFrame.Unity.Hosting;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    [Serializable]
    public sealed class UnityContentBuildBundle
    {
        public string name;
        public string[] assetPaths;
    }

    [Serializable]
    public sealed class UnityContentBuildSpecification
    {
        public UnityContentBuildBundle[] bundles;
        public UnityBundleAsset[] assets;
    }

    public static class UnityContentBuilder
    {
        private const string CatalogFileName = "bundle-catalog.json";
        public static void Run()
        {
            var arguments = Environment.GetCommandLineArgs();
            Build(Argument(arguments, "-awith-content-spec"), Argument(arguments, "-awith-content-output"), EditorUserBuildSettings.activeBuildTarget);
        }

        public static void Build(string specificationPath, string outputDirectory, BuildTarget target)
        {
            outputDirectory = Path.GetFullPath(outputDirectory);
            if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
                throw new IOException("Build content into a new or empty directory.");
            var specification = JsonUtility.FromJson<UnityContentBuildSpecification>(File.ReadAllText(specificationPath));
            if (specification?.bundles == null || specification.assets == null || specification.bundles.Any(bundle => bundle == null))
                throw new InvalidDataException("Invalid content build specification.");
            var provisional = new UnityBundleCatalog
            {
                target = target.ToString(), unityVersion = Application.unityVersion, assets = specification.assets,
                bundles = specification.bundles.Select(bundle => new UnityBundleInfo { name = bundle.name }).ToArray()
            };
            provisional.Validate();
            ValidateOutputPaths(provisional.bundles, new DirectoryInfo(outputDirectory).Name);
            var builds = new List<AssetBundleBuild>();
            foreach (var bundle in specification.bundles)
            {
                if (bundle.assetPaths == null || bundle.assetPaths.Length == 0) throw new InvalidDataException("Empty bundle: " + bundle.name);
                foreach (var path in bundle.assetPaths)
                    if (path == null || !path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.LoadMainAssetAtPath(path) == null)
                        throw new FileNotFoundException("Bundle source asset not found: " + path);
                builds.Add(new AssetBundleBuild { assetBundleName = bundle.name, assetNames = bundle.assetPaths });
            }
            foreach (var asset in specification.assets)
            {
                var source = specification.bundles.Single(bundle => bundle.name.Equals(asset.bundle, StringComparison.OrdinalIgnoreCase));
                if (!source.assetPaths.Contains(asset.assetPath, StringComparer.Ordinal)) throw new InvalidDataException("Address is not explicitly assigned to its bundle: " + asset.key);
                if (asset.scene != asset.assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Scene address kind mismatch: " + asset.key);
            }
            Directory.CreateDirectory(outputDirectory);
            var manifest = BuildPipeline.BuildAssetBundles(outputDirectory, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) throw new InvalidOperationException("AssetBundle build failed.");
            provisional.bundles = manifest.GetAllAssetBundles().OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new UnityBundleInfo { name = name, dependencies = manifest.GetDirectDependencies(name).OrderBy(value => value, StringComparer.Ordinal).ToArray() }).ToArray();
            provisional.Validate();
            File.WriteAllText(Path.Combine(outputDirectory, CatalogFileName), JsonUtility.ToJson(provisional, true), new System.Text.UTF8Encoding(false));
            Debug.Log("UNITY_CONTENT_BUILT target=" + target + " bundles=" + provisional.bundles.Length);
        }

        private static void ValidateOutputPaths(IEnumerable<UnityBundleInfo> bundles, string manifestName)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path)
            {
                if (!paths.Add(path)) throw new InvalidDataException("Content output paths collide: " + path);
            }
            Add(CatalogFileName); Add(manifestName); Add(manifestName + ".manifest");
            foreach (var bundle in bundles) { Add(bundle.name); Add(bundle.name + ".manifest"); }
            foreach (var path in paths)
                for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                    if (paths.Contains(path.Substring(0, slash))) throw new InvalidDataException("Content output is both a file and directory: " + path);
        }

        private static string Argument(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : throw new ArgumentException("Missing " + name);
        }
    }
}
