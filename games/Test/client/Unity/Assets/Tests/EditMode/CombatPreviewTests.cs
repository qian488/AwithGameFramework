using System.IO;
using System.Linq;
using NUnit.Framework;
using Test.Client.Combat;
using Test.Client.Combat.Editor;
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
            Assert.That(RainPassEnvironmentBaker.Validate(profile), Is.Null);

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

        [Test]
        public void BakePreservesAssetIdentityAndKeepsProfileOutputsIndependent()
        {
            const string checkedInPrefab = "Assets/Content/RainPass/Generated/RainPassEnvironment.prefab";
            var originalBytes = File.ReadAllBytes(checkedInPrefab);
            var fixturePath = AssetDatabase.GenerateUniqueAssetPath("Assets/RainPassBakeFixture.asset");
            var fixture = ScriptableObject.CreateInstance<RainPassEnvironmentProfile>();
            AssetDatabase.CreateAsset(fixture, fixturePath);
            var outputFolder = "Assets/Content/RainPass/Generated/" + AssetDatabase.AssetPathToGUID(fixturePath);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var level = new GameObject("Bake fixture");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(level, scene);
                var showcase = level.AddComponent<CombatShowcase>();
                var settings = new SerializedObject(showcase);
                settings.FindProperty("environmentProfile").objectReferenceValue = fixture;
                var shaderGuid = AssetDatabase.FindAssets("PassSurface t:Shader")[0];
                settings.FindProperty("surfaceShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(shaderGuid));
                settings.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.CreateFolder("Assets/Content/RainPass/Generated", AssetDatabase.AssetPathToGUID(fixturePath));
                var unregisteredPath = outputFolder + "/RainPass_Black reed.mat";
                AssetDatabase.CreateAsset(new Material((Shader)settings.FindProperty("surfaceShader").objectReferenceValue), unregisteredPath);
                var unregisteredBytes = File.ReadAllBytes(unregisteredPath);
                Assert.That(() => RainPassEnvironmentBaker.Bake(showcase), Throws.TypeOf<IOException>().With.Message.Contains(unregisteredPath));
                var unregisteredName = Path.GetFileName(unregisteredPath);
                Assert.That(Directory.GetFiles(outputFolder).Select(Path.GetFileName),
                    Is.EquivalentTo(new[] { unregisteredName, unregisteredName + ".meta" }));
                Assert.That(File.ReadAllBytes(unregisteredPath), Is.EqualTo(unregisteredBytes));
                Assert.That(fixture.BakedPrefab, Is.Null);
                Assert.That(AssetDatabase.DeleteAsset(unregisteredPath), Is.True);

                RainPassEnvironmentBaker.Bake(showcase);
                var prefabPath = AssetDatabase.GetAssetPath(fixture.BakedPrefab);
                Assert.That(prefabPath, Is.Not.EqualTo(checkedInPrefab));
                Assert.That(File.ReadAllText(fixture.ManifestPath), Does.Contain("\"sha256\""));
                Assert.That(RainPassEnvironmentBaker.Validate(fixture), Is.Null);
                var guid = AssetDatabase.AssetPathToGUID(prefabPath);
                var materialPath = System.Array.Find(fixture.GeneratedAssetPaths, path => path.EndsWith(".mat"));
                var materialGuid = AssetDatabase.AssetPathToGUID(materialPath);
                Assert.That(RainPassEnvironmentBaker.Validate(fixture, true, out var edited), Is.Null);
                Assert.That(edited, Is.Empty);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                material.renderQueue = 2929;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                Assert.That(RainPassEnvironmentBaker.Validate(fixture, true, out edited), Is.Null);
                Assert.That(edited, Is.EqualTo(new[] { materialPath }));

                fixture.Parameters.seed++;
                Assert.That(RainPassEnvironmentBaker.Validate(fixture), Does.Contain("stale"));
                RainPassEnvironmentBaker.Bake(showcase);
                Assert.That(AssetDatabase.AssetPathToGUID(prefabPath), Is.EqualTo(guid));
                Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
                Assert.That(RainPassEnvironmentBaker.Validate(fixture), Is.Null);
                RainPassEnvironmentBaker.Clear(fixture);
                Assert.That(File.Exists(prefabPath), Is.False);
                Assert.That(File.ReadAllBytes(checkedInPrefab), Is.EqualTo(originalBytes));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (AssetDatabase.IsValidFolder(outputFolder)) AssetDatabase.DeleteAsset(outputFolder);
                AssetDatabase.DeleteAsset(fixturePath);
            }
        }
    }
}
