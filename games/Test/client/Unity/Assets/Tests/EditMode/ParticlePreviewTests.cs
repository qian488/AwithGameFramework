using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Test.Client.Tests
{
    public sealed class ParticlePreviewTests
    {
        [Test]
        public void FormulaPrefabPlacesAndPreviewsSpiralAndProjectile()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Particles/Presets/Formula Burst.prefab");
            Assert.That(asset, Is.Not.Null);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance), Is.SameAs(asset));
                var effect = instance.GetComponent<ProceduralParticleEffect>();
                var settings = new SerializedObject(effect);
                Assert.That(settings.FindProperty("playOnEnable").boolValue, Is.True);
                var layers = settings.FindProperty("layers");
                var spiralLayer = layers.GetArrayElementAtIndex(0);
                var projectileLayer = layers.GetArrayElementAtIndex(1);
                Assert.That(spiralLayer.FindPropertyRelative("launchProfile").objectReferenceValue, Is.Not.Null);
                Assert.That(projectileLayer.FindPropertyRelative("launchProfile").objectReferenceValue, Is.Not.Null);
                effect.BeginPreview();
                effect.StepPreview(0.05f);
                var spiral = instance.transform.Find("Generated particles/Archimedean spiral").GetComponent<ParticleSystem>();
                var projectile = instance.transform.Find("Generated particles/Projectile fan").GetComponent<ParticleSystem>();
                Assert.That(spiral.particleCount, Is.EqualTo(120));
                Assert.That(projectile.particleCount, Is.Zero);

                var particles = new ParticleSystem.Particle[120];
                spiral.GetParticles(particles);
                Assert.That(System.Array.Exists(particles, particle => particle.position.y > 0.45f), Is.True);
                Assert.That(System.Array.Exists(particles, particle => particle.position.y < -0.45f), Is.True);
                effect.StepPreview(0.20f);
                Assert.That(projectile.particleCount, Is.EqualTo(54));
                projectile.GetParticles(particles);
                var initialVerticalVelocity = particles[0].velocity.y;
                Assert.That(initialVerticalVelocity, Is.GreaterThan(0f));
                effect.StepPreview(0.2f);
                projectile.GetParticles(particles);
                Assert.That(particles[0].velocity.y, Is.LessThan(initialVerticalVelocity));

                effect.EndPreview();
                Assert.That(instance.transform.childCount, Is.Zero);
                Assert.That(asset.transform.childCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LegacyFormulaShapeStillUsesItsSerializedParameters()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Particles/Presets/Formula Burst.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                var effect = instance.GetComponent<ProceduralParticleEffect>();
                var settings = new SerializedObject(effect);
                var spiralLayer = settings.FindProperty("layers").GetArrayElementAtIndex(0);
                spiralLayer.FindPropertyRelative("launchProfile").objectReferenceValue = null;
                spiralLayer.FindPropertyRelative("shape").intValue = 4;
                spiralLayer.FindPropertyRelative("radius").floatValue = 2f;
                spiralLayer.FindPropertyRelative("spiralTurns").floatValue = 1f;
                settings.ApplyModifiedPropertiesWithoutUndo();

                effect.BeginPreview();
                effect.StepPreview(0.05f);
                var emitter = instance.transform.Find("Generated particles/Archimedean spiral").GetComponent<ParticleSystem>();
                var particles = new ParticleSystem.Particle[120];
                Assert.That(emitter.GetParticles(particles), Is.EqualTo(120));
                Assert.That(System.Array.Exists(particles, particle => particle.position.y < -1.4f), Is.True);
                effect.EndPreview();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void InspectorLayersRegeneratePreviewWithoutLeavingObjects()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            try
            {
                var placed = System.Array.Find(scene.GetRootGameObjects(), item => item.name == "Star Ring Burst");
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(placed),
                    Is.EqualTo("Assets/Particles/Presets/Star Ring Burst.prefab"));
                var effect = placed.GetComponent<ProceduralParticleEffect>();
                Assert.That(new SerializedObject(effect).FindProperty("playOnEnable").boolValue, Is.False);
                Assert.That(effect.BeginPreview(), Is.GreaterThan(2f));
                effect.StepPreview(0.06f);
                var initial = effect.GetComponentsInChildren<ParticleSystem>();
                Assert.That(initial.Length, Is.EqualTo(4));
                Assert.That(initial[0].particleCount, Is.EqualTo(2));
                Assert.That(initial[3].particleCount, Is.Zero);
                effect.StepPreview(0.25f);
                Assert.That(initial[3].particleCount, Is.EqualTo(48));

                var authoring = new SerializedObject(effect);
                var layers = authoring.FindProperty("layers");
                layers.GetArrayElementAtIndex(0).FindPropertyRelative("enabled").boolValue = false;
                layers.InsertArrayElementAtIndex(layers.arraySize - 1);
                var added = layers.GetArrayElementAtIndex(layers.arraySize - 2);
                added.FindPropertyRelative("enabled").boolValue = true;
                added.FindPropertyRelative("label").stringValue = "Extra layer";
                added.FindPropertyRelative("count").intValue = 17;
                authoring.ApplyModifiedPropertiesWithoutUndo();

                effect.BeginPreview();
                Assert.That(initial[0] == null, Is.True);
                var updated = effect.GetComponentsInChildren<ParticleSystem>();
                Assert.That(updated.Length, Is.EqualTo(4));
                var extra = System.Array.Find(updated, emitter => emitter.name == "Extra layer");
                Assert.That(extra.emission.GetBurst(0).count.constant, Is.EqualTo(17));

                effect.EndPreview();
                Assert.That(effect.transform.childCount, Is.Zero);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
