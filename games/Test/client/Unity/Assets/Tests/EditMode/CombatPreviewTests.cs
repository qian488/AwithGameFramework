using System.IO;
using NUnit.Framework;
using Test.Client.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Test.Client.Tests
{
    public sealed class CombatPreviewTests
    {
        [Test]
        public void PreviewIsRepeatableAndCanBeCleared()
        {
            var previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var level = new GameObject("Combat preview fixture");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(level, previewScene);
                var showcase = level.AddComponent<CombatShowcase>();
                var settings = new SerializedObject(showcase);
                string[] shaders = AssetDatabase.FindAssets("PassSurface t:Shader");
                Assert.That(shaders, Has.Length.EqualTo(1));
                var surface = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(shaders[0]));
                settings.FindProperty("surfaceShader").objectReferenceValue = surface;
                settings.ApplyModifiedPropertiesWithoutUndo();
                string digest;
                using (var generated = showcase.GeneratePreview())
                {
                    Assert.That(generated.Root.hideFlags.HasFlag(HideFlags.DontSaveInEditor), Is.True);
                    Assert.That(generated.ArchitectureCount, Is.GreaterThan(300));
                    digest = generated.LayoutDigest;
                }
                Assert.That(level.transform.childCount, Is.Zero);
                using (var generated = showcase.GeneratePreview()) Assert.That(generated.LayoutDigest, Is.EqualTo(digest));
                settings.FindProperty("seed").intValue = 99;
                settings.ApplyModifiedPropertiesWithoutUndo();
                using (var generated = showcase.GeneratePreview()) Assert.That(generated.LayoutDigest, Is.Not.EqualTo(digest));
                Assert.That(level.transform.childCount, Is.Zero);
            }
            finally { EditorSceneManager.ClosePreviewScene(previewScene); }
        }

        [Test]
        public void BakedEnvironmentProfileReferencesCurrentGeneratedAssets()
        {
            const string profilePath = "Assets/Content/RainPass/RainPassEnvironment.asset";
            var profile = AssetDatabase.LoadAssetAtPath<RainPassEnvironmentProfile>(profilePath);
            Assert.That(profile, Is.Not.Null, "The checked-in Rain Pass environment profile is missing.");
            Assert.That(profile.BakedPrefab, Is.Not.Null);
            Assert.That(profile.CurrentInputHash, Has.Length.EqualTo(64));
            Assert.That(profile.HasCurrentBakedAsset, Is.True);
            Assert.That(profile.ManifestPath, Is.Not.Empty);
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(profile.ManifestPath), Is.Not.Null);

            foreach (var path in profile.GeneratedAssetPaths)
            {
                if (path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                    Assert.That(File.Exists(Path.GetFullPath(path)), Is.True, path);
                else
                    Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.Not.Null, path);
            }

            var marker = profile.BakedPrefab.GetComponentInChildren<RainPassGeneratedAssetMarker>(true);
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.GeneratorId, Is.EqualTo(RainPassEnvironmentProfile.GeneratorId));
            Assert.That(marker.GeneratorVersion, Is.EqualTo(RainPassEnvironmentProfile.GeneratorVersion));
            Assert.That(marker.InputHash, Is.EqualTo(profile.CurrentInputHash));
        }

        [Test]
        public void BakedEnvironmentContainsLightingAndCombatGates()
        {
            const string prefabPath = "Assets/Content/RainPass/Generated/RainPassEnvironment.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.transform.Find("Moon key"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Sky rim"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Practice gate"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Court gate"), Is.Not.Null);
        }
    }
}
