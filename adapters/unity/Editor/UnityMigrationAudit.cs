using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AwithGameFrame.Unity.Editor
{
    public static class UnityMigrationAudit
    {
        private static readonly Regex ExternalPointer = new Regex(
            @"^\s*(?:-\s+)?(?:[\w.]+:\s*)?\{\s*fileID:\s*(?<id>-?\d+),\s*guid:\s*(?<guid>[0-9a-fA-F]{32}),\s*type:\s*\d+\s*\}",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> BuiltInGuids = new HashSet<string>(StringComparer.Ordinal)
        {
            "00000000000000000000000000000000",
            "0000000000000000e000000000000000", // unity default resources
            "0000000000000000f000000000000000"  // unity_builtin_extra
        };

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string editorVersion = Application.unityVersion;
            public PackageVersion[] packages;
            public int sceneCount;
            public int prefabCount;
            public Finding[] findings;
        }

        [Serializable]
        private sealed class PackageVersion
        {
            public string name, version, source;
        }

        [Serializable]
        private sealed class Finding
        {
            public string kind, asset, location, guid, fileId, dependency;
            public int line, count;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("Migration audit requires a batch editor in Edit Mode.");

            var args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "-awith-migration-audit-output");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length)
                throw new ArgumentException("Missing -awith-migration-audit-output.");

            var report = new Report
            {
                packages = PackageInfo.GetAllRegisteredPackages()
                    .OrderBy(package => package.name, StringComparer.Ordinal)
                    .Select(package => new PackageVersion
                    {
                        name = package.name, version = package.version, source = package.source.ToString()
                    }).ToArray()
            };
            var findings = new List<Finding>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            // Enumerate the source files as well as using the database, so a failed import cannot hide a root asset.
            foreach (string absolute in Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                .Where(path => HasExtension(path, ".unity") || HasExtension(path, ".prefab"))
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                string path = "Assets" + absolute.Substring(Application.dataPath.Length).Replace('\\', '/');
                bool isScene = HasExtension(path, ".unity");
                if (isScene) report.sceneCount++;
                else report.prefabCount++;
                ScanFile(path, absolute, visited, resolved, findings);
                InspectHierarchy(path, isScene, findings);
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (dependency == path || !IsProjectAsset(dependency)) continue;
                    var file = PhysicalAssetPath(dependency);
                    if (file == null || (!File.Exists(file) && !Directory.Exists(file)))
                        findings.Add(new Finding { kind = "missing-asset-dependency", asset = path, dependency = dependency });
                    else if (File.Exists(file))
                        ScanFile(dependency, file, visited, resolved, findings);
                }
            }

            report.findings = findings.OrderBy(item => item.asset, StringComparer.Ordinal).ThenBy(item => item.line)
                .ThenBy(item => item.kind, StringComparer.Ordinal).ThenBy(item => item.location, StringComparer.Ordinal)
                .ThenBy(item => item.guid, StringComparer.Ordinal).ThenBy(item => item.fileId, StringComparer.Ordinal)
                .ThenBy(item => item.dependency, StringComparer.Ordinal).ToArray();
            File.WriteAllText(args[outputIndex + 1], JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            Debug.Log($"Unity migration audit inspected {report.sceneCount} scenes and {report.prefabCount} prefabs, with {report.findings.Length} findings.");
        }

        private static void InspectHierarchy(string path, bool isScene, List<Finding> findings)
        {
            if (isScene)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    findings.Add(new Finding { kind = "asset-import-failed", asset = path });
                    return;
                }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (!scene.IsValid())
                {
                    findings.Add(new Finding { kind = "asset-import-failed", asset = path });
                    return;
                }
                try
                {
                    foreach (var root in scene.GetRootGameObjects()) InspectObjects(path, root, findings);
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) InspectObjects(path, prefab, findings);
            else findings.Add(new Finding { kind = "asset-import-failed", asset = path });
        }

        private static void InspectObjects(string path, GameObject root, List<Finding> findings)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
                if (count != 0)
                    findings.Add(new Finding { kind = "missing-script", asset = path, location = HierarchyPath(node), count = count });

                foreach (var component in node.GetComponents<Component>())
                {
                    if (component == null) continue;
                    using (var serialized = new SerializedObject(component))
                    using (var property = serialized.GetIterator())
                    {
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference ||
                                property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0) continue;
                            findings.Add(new Finding
                            {
                                kind = "missing-object-reference", asset = path,
                                location = HierarchyPath(node) + ":" + component.GetType().FullName + "." + property.propertyPath
                            });
                        }
                    }
                }
            }
        }

        private static string HierarchyPath(Transform node)
        {
            var segments = new Stack<string>();
            for (var item = node; item != null; item = item.parent)
                segments.Push(item.name + "[" + item.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
            return "/" + string.Join("/", segments);
        }

        private static void ScanFile(string path, string absolute, HashSet<string> visited,
            Dictionary<string, string> resolved, List<Finding> findings)
        {
            if (!visited.Add(path)) return;
            using (var stream = File.OpenRead(absolute))
            {
                // Check the Unity text header before decoding imported binaries such as textures or audio.
                using (var reader = new StreamReader(stream))
                {
                    var header = new char[6];
                    if (reader.ReadBlock(header, 0, header.Length) != header.Length || new string(header) != "%YAML ") return;
                    reader.ReadLine();
                    string line;
                    int number = 1;
                    while ((line = reader.ReadLine()) != null)
                    {
                        number++;
                        Match match = ExternalPointer.Match(line);
                        if (!match.Success) continue;
                        string id = match.Groups["id"].Value;
                        string guid = match.Groups["guid"].Value.ToLowerInvariant();
                        if (id == "0" || BuiltInGuids.Contains(guid)) continue;
                        if (!resolved.TryGetValue(guid, out string dependency))
                        {
                            dependency = AssetDatabase.GUIDToAssetPath(guid);
                            resolved.Add(guid, dependency);
                        }
                        if (string.IsNullOrEmpty(dependency))
                            findings.Add(new Finding
                            {
                                kind = "unresolved-serialized-guid", asset = path, line = number, guid = guid, fileId = id
                            });
                    }
                }
            }
        }

        private static string PhysicalAssetPath(string path)
        {
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
                return Path.Combine(Application.dataPath, path.Substring("Assets/".Length));

            var package = PackageInfo.FindForAssetPath(path);
            if (package == null) return null;
            if (path == package.assetPath) return package.resolvedPath;
            return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
        }

        private static bool IsProjectAsset(string path) =>
            path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);

        private static bool HasExtension(string path, string extension) =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
