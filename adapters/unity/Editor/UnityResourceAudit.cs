using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    [Serializable]
    public sealed class UnityResourceAuditSpec
    {
        public int schemaVersion;
        public UnityResourceAuditEntry[] resources;
    }

    [Serializable]
    public sealed class UnityResourceAuditEntry
    {
        public string key;
        public string type;
        public string origin;
        public bool optional;
        public bool allowMultiple;
        public string[] fallbackKeys;
    }

    [Serializable]
    public sealed class UnityResourceAuditReport
    {
        public int schemaVersion = 1;
        public string unityVersion;
        public string projectPath;
        public bool passed;
        public int checkedResources;
        public UnityResourceAuditFinding[] findings = Array.Empty<UnityResourceAuditFinding>();
    }

    [Serializable]
    public sealed class UnityResourceAuditFinding
    {
        public string key;
        public string resolvedKey;
        public string expectedType;
        public string origin;
        public string status;
        public int matchCount;
        public string[] assetPaths;
        public string[] actualTypes;
        public string message;
    }

    public static class UnityResourceAudit
    {
        public static void RunBatch()
        {
            string specPath = ReadArgument("-awith-resource-spec");
            string outputPath = ReadArgument("-awith-resource-output");
            if (string.IsNullOrWhiteSpace(specPath) || string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidOperationException("Resource audit requires -awith-resource-spec and -awith-resource-output.");

            UnityResourceAuditSpec spec = JsonUtility.FromJson<UnityResourceAuditSpec>(File.ReadAllText(specPath));
            if (spec == null || spec.schemaVersion != 1 || spec.resources == null || spec.resources.Length == 0)
                throw new InvalidDataException("Resource audit spec must use schemaVersion 1 and contain at least one resource.");

            var resources = AssetDatabase.GetAllAssetPaths().Where(path => !AssetDatabase.IsValidFolder(path))
                .Select(path => (path, root: path.LastIndexOf("/Resources/", StringComparison.Ordinal)))
                .Where(asset => asset.root >= 0)
                .ToLookup(asset => Path.ChangeExtension(asset.path.Substring(asset.root + "/Resources/".Length), null),
                    asset => asset.path, StringComparer.OrdinalIgnoreCase);
            var findings = spec.resources.Select(entry => Check(entry, resources)).ToArray();

            var report = new UnityResourceAuditReport
            {
                unityVersion = Application.unityVersion,
                projectPath = Path.GetDirectoryName(Application.dataPath),
                passed = findings.All(finding => finding.status != "error"),
                checkedResources = spec.resources.Length,
                findings = findings
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            if (!report.passed)
                throw new InvalidDataException("Unity resource audit failed. See " + outputPath);
        }

        private static UnityResourceAuditFinding Check(UnityResourceAuditEntry entry, ILookup<string, string> resources)
        {
            var finding = new UnityResourceAuditFinding
            {
                key = entry?.key,
                expectedType = entry?.type,
                origin = entry?.origin,
                status = "error"
            };
            if (entry == null || !IsValidKey(entry.key) || (entry.fallbackKeys?.Any(key => !IsValidKey(key)) ?? false))
            {
                finding.message = "Expected a nonempty Resources-relative key with forward slashes.";
                return finding;
            }

            Type resourceType = ResolveType(entry.type);
            if (resourceType == null || !typeof(UnityEngine.Object).IsAssignableFrom(resourceType))
            {
                finding.message = "Expected Unity object type could not be resolved.";
                return finding;
            }

            var keys = new List<string> { entry.key };
            keys.AddRange(entry.fallbackKeys ?? Array.Empty<string>());

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                var paths = resources[key].OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var candidates = Resources.LoadAll(key);
                if (paths.Length == 0 && candidates.Length == 0)
                    continue;

                var matches = Resources.LoadAll(key, resourceType);
                finding.resolvedKey = key;
                finding.assetPaths = paths.Length == 0
                    ? candidates.Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray()
                    : paths;
                finding.actualTypes = candidates.Select(asset => asset.GetType().FullName)
                    .Distinct().OrderBy(type => type, StringComparer.Ordinal).ToArray();
                finding.matchCount = matches.Length;
                if (paths.Length > 1)
                    finding.message = "Multiple source assets declare the same Resources key.";
                else if (matches.Length == 0)
                    finding.message = "Resources key exists but does not load the expected type.";
                else if (!entry.allowMultiple && matches.Length != 1)
                    finding.message = "Multiple matching objects resolved for a single-object key.";
                else
                {
                    finding.status = i == 0 ? "ok" : "fallback";
                    finding.message = i == 0 ? "Resource resolved." : "Primary key was missing and a fallback resolved.";
                }
                return finding;
            }

            finding.status = entry.optional ? "missing-optional" : "error";
            finding.message = "No resource matched the key or its fallbacks.";
            return finding;
        }

        private static bool IsValidKey(string key) =>
            !string.IsNullOrWhiteSpace(key) && key == key.Trim() && !key.Contains("\\") &&
            key.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var type = Type.GetType(name, false);
            if (type != null) return type;
            var candidates = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false))
                .Where(candidate => candidate != null).Distinct().ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
