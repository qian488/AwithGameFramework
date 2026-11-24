using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AwithGameFrame.Data.Generation;
using UnityEditor;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Test.Client.EditMode.Tests")]

namespace Test.Client.Combat.Editor
{
    internal static class RainPassEnvironmentBaker
    {
        private const string GeneratedFolder = "Assets/Content/RainPass/Generated";
        private const string DefaultProfileGuid = "83c2f3d4e5f66a778899aabbccddeeff";
        private const string PrefabName = "RainPassEnvironment.prefab";
        private const string ManifestName = "RainPassEnvironment.manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public int formatVersion;
            public string generatorId;
            public string generatorVersion;
            public string inputHash;
            public int seed;
            public int detailDensity;
            public string fingerprint;
            public OutputFile[] outputFiles;
        }

        [Serializable]
        private sealed class LegacyManifest
        {
            public string[] outputs;
        }

        [Serializable]
        private sealed class OutputFile
        {
            public string path;
            public long size;
            public string sha256;

            public GeneratedAssetOutput ToOutput() => new GeneratedAssetOutput(path, size, sha256);
        }

        public static void Bake(CombatShowcase showcase)
        {
            var profile = showcase.EnvironmentProfile;
            if (profile == null) throw new InvalidOperationException("Create a Rain Pass environment profile first.");
            var folder = GetOutputFolder(profile);
            var prefabPath = folder + "/" + PrefabName;
            var manifestPath = folder + "/" + ManifestName;
            var previous = ReadOwnedPaths(profile, folder);
            var parameters = profile.Parameters;
            var inputHash = profile.CurrentInputHash;
            var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var newPaths = new List<string>();
            var profilePath = AssetDatabase.GetAssetPath(profile);
            PassWorld world = null;
            try
            {
                world = showcase.GenerateEnvironmentForBake();
                var root = world.Root;
                root.transform.SetParent(null, false);
                var markerTarget = root.transform.Find("Pass architecture")?.gameObject ?? root;
                var marker = markerTarget.GetComponent<RainPassGeneratedAssetMarker>() ?? markerTarget.AddComponent<RainPassGeneratedAssetMarker>();
                marker.Set(RainPassEnvironmentProfile.GeneratorId, RainPassEnvironmentProfile.GeneratorVersion, inputHash);

                EnsureFolder(folder);
                Reserve(prefabPath);
                Reserve(manifestPath);
                var materialAssets = new Dictionary<int, Material>();
                var textureAssets = new Dictionary<int, Texture2D>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (material == null || materialAssets.ContainsKey(material.GetInstanceID())) continue;
                        var texture = material.mainTexture as Texture2D;
                        Texture2D savedTexture = null;
                        if (texture != null)
                        {
                            if (!textureAssets.ContainsKey(texture.GetInstanceID()))
                            {
                                var textureCopy = UnityEngine.Object.Instantiate(texture);
                                textureCopy.name = "RainPass_" + Sanitize(texture.name);
                                textureAssets.Add(texture.GetInstanceID(), SaveAsset(textureCopy, ".asset"));
                            }
                            savedTexture = textureAssets[texture.GetInstanceID()];
                        }
                        var copy = UnityEngine.Object.Instantiate(material);
                        copy.name = "RainPass_" + Sanitize(material.name);
                        if (savedTexture != null) copy.mainTexture = savedTexture;
                        materialAssets.Add(material.GetInstanceID(), SaveAsset(copy, ".mat"));
                    }

                    var replacements = new Material[materials.Length];
                    for (int index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        replacements[index] = material == null ? null : materialAssets[material.GetInstanceID()];
                    }
                    renderer.sharedMaterials = replacements;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var saved);
                if (!saved) throw new InvalidOperationException("Unity could not save the generated Rain Pass prefab: " + prefabPath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) throw new IOException("Unity could not load the baked Rain Pass prefab: " + prefabPath);
                outputs.Add(prefabPath);
                // Keep prior outputs registered if a later generator stops referencing them.
                // Existing scenes or authored variants may still reference their GUIDs.
                foreach (var path in previous.Where(path => path != manifestPath && File.Exists(path))) outputs.Add(path);
                var signature = new GeneratedAssetManifest(RainPassEnvironmentProfile.GeneratorId,
                    RainPassEnvironmentProfile.GeneratorVersion, inputHash, parameters.seed, outputs.Select(ReadOutput));
                var manifest = new Manifest
                {
                    formatVersion = 2,
                    generatorId = signature.GeneratorId,
                    generatorVersion = signature.GeneratorVersion,
                    inputHash = signature.InputHash,
                    seed = parameters.seed,
                    detailDensity = parameters.detailDensity,
                    fingerprint = signature.Fingerprint,
                    outputFiles = signature.Outputs.Select(output => new OutputFile
                    {
                        path = output.Path,
                        size = output.Size,
                        sha256 = output.Sha256
                    }).ToArray()
                };
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
                AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
                backups.Add(profilePath, File.ReadAllBytes(profilePath));
                profile.SetBakeResult(prefab, inputHash, manifestPath, signature.Outputs.Select(output => output.Path).Append(manifestPath).ToArray());
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            catch
            {
                foreach (var path in newPaths.AsEnumerable().Reverse()) DeleteOutput(path);
                foreach (var backup in backups) File.WriteAllBytes(backup.Key, backup.Value);
                foreach (var path in backups.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
            finally
            {
                world?.Dispose();
            }

            void Reserve(string path)
            {
                if (File.Exists(path) || File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    if (!previous.Contains(path)) throw new IOException("A Rain Pass output would overwrite an unregistered asset: " + path);
                    backups.Add(path, File.ReadAllBytes(path));
                }
                else newPaths.Add(path);
            }

            T SaveAsset<T>(T asset, string extension) where T : UnityEngine.Object
            {
                var stem = folder + "/" + asset.name;
                var path = stem + extension;
                for (var ordinal = 1; outputs.Contains(path); ordinal++) path = stem + " " + ordinal + extension;
                try
                {
                    Reserve(path);
                    var existing = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (existing == null)
                    {
                        if (File.Exists(path)) throw new IOException("Unexpected type for Rain Pass output: " + path);
                        AssetDatabase.CreateAsset(asset, path);
                        existing = asset;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(asset, existing);
                        EditorUtility.SetDirty(existing);
                        AssetDatabase.SaveAssetIfDirty(existing);
                    }
                    outputs.Add(path);
                    return existing;
                }
                finally
                {
                    if (!AssetDatabase.Contains(asset)) UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        public static string Validate(RainPassEnvironmentProfile profile) => Validate(profile, false, out _);

        public static string Validate(RainPassEnvironmentProfile profile, bool checkFileHashes, out string[] editedOutputs)
        {
            editedOutputs = Array.Empty<string>();
            if (profile == null) return "No Rain Pass environment profile is assigned.";
            if (profile.BakedPrefab == null) return "The profile has no baked prefab.";
            if (!profile.HasCurrentBakedAsset) return "The baked prefab is stale. Bake it again after changing the profile.";
            var marker = profile.BakedPrefab.GetComponentInChildren<RainPassGeneratedAssetMarker>(true);
            if (marker == null) return "The baked prefab has no Rain Pass generated asset marker.";
            if (marker.GeneratorId != RainPassEnvironmentProfile.GeneratorId ||
                marker.GeneratorVersion != RainPassEnvironmentProfile.GeneratorVersion ||
                marker.InputHash != profile.CurrentInputHash)
                return "The baked prefab source marker does not match the profile.";
            try
            {
                var paths = ReadOwnedPaths(profile, GetOutputFolder(profile), out var manifest, out var signature);
                if (manifest.inputHash != profile.CurrentInputHash || manifest.generatorVersion != RainPassEnvironmentProfile.GeneratorVersion ||
                    manifest.seed != profile.Parameters.seed || manifest.detailDensity != profile.Parameters.detailDensity)
                    return "The generated manifest does not match the profile parameters.";
                foreach (var path in paths)
                    if (!File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) == null)
                        return "Generated asset is missing: " + path;
                if (checkFileHashes && signature != null)
                    editedOutputs = signature.Outputs.Where(output => ReadOutput(output.Path).Sha256 != output.Sha256)
                        .Select(output => output.Path).ToArray();
                return null;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException)
            {
                return error.Message;
            }
        }

        public static void Clear(RainPassEnvironmentProfile profile)
        {
            if (profile == null) return;
            var paths = ReadOwnedPaths(profile, GetOutputFolder(profile));
            foreach (var path in paths.Where(path => path != profile.ManifestPath)) DeleteOutput(path);
            if (paths.Count > 0) DeleteOutput(profile.ManifestPath);
            profile.ClearBakeResult();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private static string GetOutputFolder(RainPassEnvironmentProfile profile)
        {
            var path = AssetDatabase.GetAssetPath(profile);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Save the Rain Pass profile as a project asset before baking.");
            // The original profile already owns these paths. Other profiles own separate folders.
            if (guid == DefaultProfileGuid)
                return GeneratedFolder;
            return GeneratedFolder + "/" + guid;
        }

        private static HashSet<string> ReadOwnedPaths(RainPassEnvironmentProfile profile, string folder) =>
            ReadOwnedPaths(profile, folder, out _, out _);

        private static HashSet<string> ReadOwnedPaths(RainPassEnvironmentProfile profile, string folder, out Manifest manifest,
            out GeneratedAssetManifest signature)
        {
            manifest = null;
            signature = null;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(profile.ManifestPath) && profile.GeneratedAssetPaths.Length == 0) return paths;

            var manifestPath = folder + "/" + ManifestName;
            if (profile.ManifestPath != manifestPath || !File.Exists(manifestPath))
                throw new IOException("The Rain Pass profile's generated manifest is missing from " + manifestPath);
            var json = File.ReadAllText(manifestPath);
            manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.generatorId != RainPassEnvironmentProfile.GeneratorId || manifest.inputHash != profile.BakedInputHash)
                throw new IOException("The generated manifest does not belong to the Rain Pass bake: " + manifestPath);

            IEnumerable<string> outputs;
            if (manifest.formatVersion == 2)
            {
                if (manifest.outputFiles == null) throw new IOException("The generated manifest has no output files: " + manifestPath);
                signature = new GeneratedAssetManifest(manifest.generatorId, manifest.generatorVersion, manifest.inputHash,
                    manifest.seed, manifest.outputFiles.Select(output => output?.ToOutput()));
                if (signature.Fingerprint != manifest.fingerprint)
                    throw new IOException("The generated manifest fingerprint is invalid: " + manifestPath);
                outputs = signature.Outputs.Select(output => output.Path);
            }
            else if (manifest.formatVersion == 0) outputs = JsonUtility.FromJson<LegacyManifest>(json)?.outputs ?? Array.Empty<string>();
            else throw new IOException("Unsupported Rain Pass manifest format: " + manifest.formatVersion);

            var prefabPath = folder + "/" + PrefabName;
            foreach (var path in outputs)
            {
                if (path == null || path != folder + "/" + Path.GetFileName(path) ||
                    (path != prefabPath && !path.EndsWith(".asset", StringComparison.Ordinal) && !path.EndsWith(".mat", StringComparison.Ordinal)) ||
                    !paths.Add(path) || AssetDatabase.IsValidFolder(path))
                    throw new IOException("The generated manifest contains an invalid Rain Pass output: " + path);
            }
            if (profile.BakedPrefab != null && AssetDatabase.GetAssetPath(profile.BakedPrefab) != prefabPath)
                throw new IOException("The Rain Pass profile references an unexpected prefab: " + AssetDatabase.GetAssetPath(profile.BakedPrefab));
            if (!paths.Contains(prefabPath) ||
                !paths.Add(manifestPath) || !paths.SetEquals(profile.GeneratedAssetPaths))
                throw new IOException("The generated manifest output list does not match the Rain Pass profile: " + manifestPath);
            return paths;
        }

        private static GeneratedAssetOutput ReadOutput(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return new GeneratedAssetOutput(path, stream.Length, BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""));
        }

        private static void DeleteOutput(string path)
        {
            if (!File.Exists(path) && !File.Exists(path + ".meta")) return;
            if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not delete Rain Pass output: " + path);
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        }
    }
}
