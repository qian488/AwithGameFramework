using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AwithGameFrame.Unity.Hosting
{
    [Serializable]
    public sealed class UnityBundleInfo
    {
        public string name;
        public string[] dependencies = Array.Empty<string>();
    }

    [Serializable]
    public sealed class UnityBundleAsset
    {
        public string key;
        public string bundle;
        public string assetPath;
        public bool scene;
    }

    /// <summary>Build-generated logical addresses and transitive bundle dependencies.</summary>
    [Serializable]
    public sealed class UnityBundleCatalog
    {
        public string target;
        public string unityVersion;
        public UnityBundleInfo[] bundles = Array.Empty<UnityBundleInfo>();
        public UnityBundleAsset[] assets = Array.Empty<UnityBundleAsset>();

        public static UnityBundleCatalog FromJson(string json)
        {
            var catalog = JsonUtility.FromJson<UnityBundleCatalog>(json) ?? throw new InvalidDataException("Invalid bundle catalog.");
            catalog.Validate();
            return catalog;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(target) || bundles == null || assets == null) throw new InvalidDataException("Incomplete bundle catalog.");
            var names = new Dictionary<string, UnityBundleInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in bundles)
            {
                if (bundle == null || !SafePath(bundle.name) || bundle.dependencies == null || names.ContainsKey(bundle.name))
                    throw new InvalidDataException("Invalid or duplicate bundle name.");
                names.Add(bundle.name, bundle);
            }
            foreach (var bundle in bundles)
                foreach (var dependency in bundle.dependencies)
                    if (string.IsNullOrWhiteSpace(dependency) || !names.ContainsKey(dependency)) throw new InvalidDataException("Missing bundle dependency: " + dependency);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in bundles) Visit(bundle.name, names, visiting, visited);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets)
                if (asset == null || string.IsNullOrWhiteSpace(asset.key) || !keys.Add(asset.key) ||
                    !names.ContainsKey(asset.bundle ?? "") || !SafePath(asset.assetPath) || !asset.assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    asset.scene != asset.assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid or duplicate asset address.");
        }

        private static void Visit(string name, Dictionary<string, UnityBundleInfo> names, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) throw new InvalidDataException("Cyclic bundle dependencies.");
            foreach (var dependency in names[name].dependencies) Visit(dependency, names, visiting, visited);
            visiting.Remove(name); visited.Add(name);
        }

        private static bool SafePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal) || value.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0) return false;
            foreach (var part in value.Split('/')) if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }
    }

    public sealed class UnityBundleScene
    {
        internal UnityBundleScene(string path) => Path = path;
        public string Path { get; }
    }
}
