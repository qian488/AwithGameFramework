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
    }
}
