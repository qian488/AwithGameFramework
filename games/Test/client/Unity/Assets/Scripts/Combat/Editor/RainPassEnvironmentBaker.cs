using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Test.Client.Combat.Editor
{
    internal static class RainPassEnvironmentBaker
    {
        private const string GeneratedFolder = "Assets/Content/RainPass/Generated";
        private const string PrefabPath = GeneratedFolder + "/RainPassEnvironment.prefab";
        private const string ManifestPath = GeneratedFolder + "/RainPassEnvironment.manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public string generatorId;
            public string generatorVersion;
            public string inputHash;
            public int seed;
            public int detailDensity;
            public string[] outputs;
        }

        public static void Bake(CombatShowcase showcase)
        {
            var profile = showcase.EnvironmentProfile;
            if (profile == null) throw new InvalidOperationException("Create a Rain Pass environment profile first.");
            ClearGeneratedOutputs(profile);
            var parameters = profile.Parameters;
            var inputHash = profile.CurrentInputHash;
            PassWorld world = null;
            try
            {
                world = showcase.GenerateEnvironmentForBake();
                var root = world.Root;
                root.transform.SetParent(null, false);
                var markerTarget = root.transform.Find("Pass architecture")?.gameObject ?? root;
                var marker = markerTarget.GetComponent<RainPassGeneratedAssetMarker>() ?? markerTarget.AddComponent<RainPassGeneratedAssetMarker>();
                marker.Set(RainPassEnvironmentProfile.GeneratorId, RainPassEnvironmentProfile.GeneratorVersion, inputHash);

                EnsureFolder(GeneratedFolder);
                var outputs = new List<string>();
                var materialAssets = new Dictionary<int, Material>();
                var textureAssets = new Dictionary<int, Texture2D>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (material == null || materialAssets.ContainsKey(material.GetInstanceID())) continue;
                        var copy = UnityEngine.Object.Instantiate(material);
                        copy.name = "RainPass_" + Sanitize(material.name);
                        var texture = copy.mainTexture as Texture2D;
                        if (texture != null)
                        {
                            if (!textureAssets.ContainsKey(texture.GetInstanceID()))
                            {
                                var textureCopy = UnityEngine.Object.Instantiate(texture);
                                textureCopy.name = "RainPass_" + Sanitize(texture.name);
                                var texturePath = AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/" + textureCopy.name + ".asset");
                                AssetDatabase.CreateAsset(textureCopy, texturePath);
                                textureAssets.Add(texture.GetInstanceID(), textureCopy);
                                outputs.Add(texturePath);
                            }
                            copy.mainTexture = textureAssets[texture.GetInstanceID()];
                        }
                        var materialPath = AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/" + copy.name + ".mat");
                        AssetDatabase.CreateAsset(copy, materialPath);
                        materialAssets.Add(material.GetInstanceID(), copy);
                        outputs.Add(materialPath);
                    }

                    var replacements = new Material[materials.Length];
                    for (int index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        replacements[index] = material == null ? null : materialAssets[material.GetInstanceID()];
                    }
                    renderer.sharedMaterials = replacements;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Unity could not save the generated Rain Pass prefab.");
                outputs.Add(PrefabPath);
                var manifest = new Manifest
                {
                    generatorId = RainPassEnvironmentProfile.GeneratorId,
                    generatorVersion = RainPassEnvironmentProfile.GeneratorVersion,
                    inputHash = inputHash,
                    seed = parameters.seed,
                    detailDensity = parameters.detailDensity,
                    outputs = outputs.ToArray()
                };
                File.WriteAllText(Path.GetFullPath(ManifestPath), JsonUtility.ToJson(manifest, true));
                AssetDatabase.ImportAsset(ManifestPath);
                outputs.Add(ManifestPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                profile.SetBakeResult(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), inputHash, ManifestPath, outputs.ToArray());
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                world?.Dispose();
            }
        }

        public static string Validate(RainPassEnvironmentProfile profile)
        {
            if (profile == null) return "No Rain Pass environment profile is assigned.";
            if (profile.BakedPrefab == null) return "The profile has no baked prefab.";
            if (!profile.HasCurrentBakedAsset) return "The baked prefab is stale. Bake it again after changing the profile.";
            var marker = profile.BakedPrefab.GetComponentInChildren<RainPassGeneratedAssetMarker>(true);
            if (marker == null) return "The baked prefab has no Rain Pass generated asset marker.";
            if (marker.GeneratorId != RainPassEnvironmentProfile.GeneratorId ||
                marker.GeneratorVersion != RainPassEnvironmentProfile.GeneratorVersion ||
                marker.InputHash != profile.CurrentInputHash)
                return "The baked prefab source marker does not match the profile.";
            foreach (var path in profile.GeneratedAssetPaths)
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(Path.GetFullPath(path))) return "Generated manifest is missing: " + path;
                }
                else if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    return "Generated asset is missing: " + path;
            }
            if (string.IsNullOrEmpty(profile.ManifestPath) || !File.Exists(Path.GetFullPath(profile.ManifestPath)))
                return "Generated manifest is missing.";
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.GetFullPath(profile.ManifestPath)));
            if (manifest == null || manifest.inputHash != profile.CurrentInputHash ||
                manifest.generatorId != RainPassEnvironmentProfile.GeneratorId ||
                manifest.generatorVersion != RainPassEnvironmentProfile.GeneratorVersion)
                return "The generated manifest does not match the profile.";
            if (manifest.seed != profile.Parameters.seed || manifest.detailDensity != profile.Parameters.detailDensity)
                return "The generated manifest parameters do not match the profile.";
            var expectedOutputs = profile.GeneratedAssetPaths
                .Where(path => !string.Equals(path, profile.ManifestPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (manifest.outputs == null || !manifest.outputs.SequenceEqual(expectedOutputs))
                return "The generated manifest output list does not match the profile.";
            return null;
        }

        public static void Clear(RainPassEnvironmentProfile profile)
        {
            if (profile == null) return;
            ClearGeneratedOutputs(profile);
            profile.ClearBakeResult();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ClearGeneratedOutputs(RainPassEnvironmentProfile profile)
        {
            foreach (var path in profile.GeneratedAssetPaths.Reverse())
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (AssetDatabase.LoadAssetAtPath<TextAsset>(path) != null) AssetDatabase.DeleteAsset(path);
                    else if (File.Exists(Path.GetFullPath(path))) File.Delete(Path.GetFullPath(path));
                }
                else if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
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
