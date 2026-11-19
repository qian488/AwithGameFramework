using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Test.Client.Editor
{
    public static class EffectPresets
    {
        internal const string Folder = "Assets/Particles/Presets";
        internal const string CustomTemplate = "Custom Layered Effect";
        internal static readonly string[] Templates = { "Rain", "Snow", "Flame", "Star Sparkle", "Lightning Chain", "Spiral", "Projectile Fan" };

        public static void InstallSamples()
        {
            foreach (var template in Templates)
            {
                var path = Folder + "/" + template + ".prefab";
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) Create(template, path);
            }
        }

        public static void CreateShowcase()
        {
            const string scenePath = "Assets/Scenes/EffectShowcase.unity";
            if (AssetDatabase.LoadMainAssetAtPath(scenePath) != null) throw new IOException("A showcase scene already exists.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Showcase Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.085f, 0.12f);
            camera.orthographic = true;
            camera.orthographicSize = 3.25f;
            camera.transform.position = new Vector3(-0.65f, -0.15f, -12f);
            var showcase = new GameObject("Effect Showcase").AddComponent<EffectShowcase>();
            using var settings = new SerializedObject(showcase);
            var presets = settings.FindProperty("presets");
            var labels = new[] { "Rain", "Snow", "Flame", "Star Sparkle", "Lightning Chain", "Spiral", "Projectile Fan", "Star Ring Burst", "Formula Burst" };
            presets.arraySize = labels.Length;
            for (var index = 0; index < labels.Length; index++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + labels[index] + ".prefab");
                if (asset == null) throw new IOException("Missing showcase effect " + labels[index]);
                presets.GetArrayElementAtIndex(index).objectReferenceValue = asset.GetComponent<TestEffectBehaviour>();
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save the showcase scene.");
        }

        internal static GameObject Create(string template, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) throw new IOException("An asset already exists at " + path);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Particles/SoftAdditive.mat");
            if (material == null) throw new InvalidOperationException("The preset particle material is missing.");
            var stage = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(path));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, stage);
                var metadata = root.AddComponent<ParticleEffectMetadata>();
                metadata.displayName = root.name;
                if (template == CustomTemplate)
                {
                    var effect = root.AddComponent<ProceduralParticleEffect>();
                    effect.ConfigureStarter(material);
                    metadata.description = "An editable starter ring. Add layers in the prefab Inspector.";
                }
                else if (template == "Spiral" || template == "Projectile Fan")
                {
                    CreateFormula(root, template, metadata);
                }
                else if (template == "Lightning Chain")
                {
                    CreateLightning(root);
                    metadata.category = ParticleEffectCategory.Magic;
                    metadata.description = "A flashing bolt jumps through three editable anchors.";
                }
                else
                {
                    root.AddComponent<NativeParticleEffect>();
                    ConfigureParticles(template, root.transform, material, metadata);
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path) ?? throw new IOException("Could not save " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(stage); }
        }

        private static void CreateFormula(GameObject root, string template, ParticleEffectMetadata metadata)
        {
            var composite = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Formula Burst.prefab");
            var source = composite == null ? null : composite.GetComponent<ProceduralParticleEffect>();
            if (source == null) throw new InvalidOperationException("The Formula Burst preset is missing.");
            var effect = root.AddComponent<ProceduralParticleEffect>();
            EditorUtility.CopySerialized(source, effect);
            using var settings = new SerializedObject(effect);
            var layers = settings.FindProperty("layers");
            if (layers.arraySize != 2) throw new InvalidOperationException("The original formula preset needs both source layers.");
            layers.DeleteArrayElementAtIndex(template == "Spiral" ? 1 : 0);
            layers.GetArrayElementAtIndex(0).FindPropertyRelative("offset").vector3Value = Vector3.zero;
            settings.ApplyModifiedPropertiesWithoutUndo();
            metadata.category = ParticleEffectCategory.Burst;
            metadata.description = template == "Spiral" ? "A single cyan Archimedean spiral." : "A single golden fan of ballistic sparks.";
        }

        private static void ConfigureParticles(string template, Transform root, Material material, ParticleEffectMetadata metadata)
        {
            metadata.category = ParticleEffectCategory.Environment;
            switch (template)
            {
                case "Rain":
                    metadata.description = "Continuous fast blue rain across a wide area.";
                    var rain = Emit(root, material, "Rain drops", new Vector3(0, 2.5f, 0), new Vector3(6, 0.1f, 1.5f), 100, 1.2f, 6.5f, 0.11f,
                        new Color(0.36f, 0.68f, 1f, 0.8f));
                    rain.transform.localRotation = Quaternion.Euler(82, 0, 0);
                    rain.velocityOverLifetime.SetWind(0.45f);
                    var rainRenderer = rain.GetComponent<ParticleSystemRenderer>();
                    rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                    rainRenderer.lengthScale = 3.4f;
                    rainRenderer.velocityScale = 0.12f;
                    break;
                case "Snow":
                    metadata.description = "Slow, softly drifting snow with varied flake sizes.";
                    var snow = Emit(root, material, "Snow flakes", new Vector3(0, 2.3f, 0), new Vector3(6, 0.1f, 2f), 36, 5f, 0.95f, 0.16f,
                        new Color(0.85f, 0.93f, 1f, 0.95f));
                    snow.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    snow.velocityOverLifetime.SetWind(0.28f);
                    SetNoise(snow, 0.24f);
                    break;
                case "Flame":
                    metadata.category = ParticleEffectCategory.Fire;
                    metadata.description = "A steady golden core, orange outer flame and rising embers.";
                    SetNoise(Rising(root, material, "Golden core", 48, 0.7f, 1.5f, 0.6f, new Color(1f, 0.66f, 0.17f, 0.85f)), 0.33f);
                    SetNoise(Rising(root, material, "Outer flame", 37, 1.15f, 1.7f, 0.82f, new Color(1f, 0.18f, 0.025f, 0.48f)), 0.48f);
                    SetNoise(Rising(root, material, "Embers", 10, 1.7f, 2f, 0.095f, new Color(1f, 0.48f, 0.12f, 1f)), 0.55f);
                    break;
                case "Star Sparkle":
                    metadata.category = ParticleEffectCategory.Magic;
                    metadata.description = "Scattered stars fade in and out at different times.";
                    var starMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Particles/StarAdditive.mat");
                    if (starMaterial == null) throw new InvalidOperationException("The star material is missing.");
                    Emit(root, starMaterial, "Cool stars", Vector3.zero, new Vector3(5, 3, 0.75f), 14, 1.25f, 0f, 0.20f, new Color(0.64f, 0.86f, 1f, 1f), true);
                    Emit(root, starMaterial, "Warm stars", Vector3.zero, new Vector3(5, 3, 0.75f), 5, 0.95f, 0f, 0.32f, new Color(1f, 0.78f, 0.41f, 1f), true);
                    break;
                default: throw new ArgumentException("Unknown effect template: " + template, nameof(template));
            }
        }

        private static ParticleSystem Rising(Transform parent, Material material, string label, int rate, float lifetime, float speed, float size, Color color)
        {
            var emitter = Emit(parent, material, label, new Vector3(0, -0.85f, 0), new Vector3(0.7f, 0.45f, 0.08f), rate, lifetime, speed, size, color);
            emitter.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            return emitter;
        }

        private static ParticleSystem Emit(Transform parent, Material material, string label, Vector3 origin, Vector3 area, int rate,
            float lifetime, float speed, float size, Color color, bool sparkle = false)
        {
            var node = Child(parent, label, origin);
            var emitter = node.AddComponent<ParticleSystem>();
            var main = emitter.main;
            main.playOnAwake = false;
            main.loop = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.3f);
            main.startColor = color;
            var shape = emitter.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = area;
            var emission = emitter.emission;
            emission.rateOverTime = rate;
            var tint = emitter.colorOverLifetime;
            tint.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(sparkle ? 0f : 1f, 0), new GradientAlphaKey(1, 0.28f), new GradientAlphaKey(0, 1) });
            tint.color = gradient;
            var scale = emitter.sizeOverLifetime;
            scale.enabled = true;
            var sizeCurve = sparkle ? new AnimationCurve(new Keyframe(0, 0.2f), new Keyframe(0.35f, 1.4f), new Keyframe(1, 0.25f)) :
                AnimationCurve.Linear(0f, 1f, 1f, 0.5f);
            scale.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
            emitter.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return emitter;
        }

        private static void SetNoise(ParticleSystem emitter, float strength)
        {
            var noise = emitter.noise;
            noise.enabled = true;
            noise.strength = strength;
        }

        private static void SetWind(this ParticleSystem.VelocityOverLifetimeModule velocity, float speed)
        {
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = speed;
            velocity.y = 0f;
            velocity.z = 0f;
        }

        private static GameObject Child(Transform parent, string label, Vector3 position)
        {
            var node = new GameObject(label);
            node.transform.SetParent(parent, false);
            node.transform.localPosition = position;
            return node;
        }

        private static void CreateLightning(GameObject root)
        {
            var effect = root.AddComponent<LightningChainEffect>();
            var line = root.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Particles/Bolt.mat");
            if (line.sharedMaterial == null) throw new InvalidOperationException("The bolt material is missing.");
            line.widthMultiplier = 0.14f;
            line.numCornerVertices = 2;
            line.startColor = new Color(0.56f, 0.87f, 1f, 1f);
            line.endColor = new Color(0.42f, 0.51f, 1f, 1f);
            line.enabled = false;
            using var settings = new SerializedObject(effect);
            var anchors = settings.FindProperty("anchors");
            var positions = new[] { new Vector3(-2.35f, -0.55f), new Vector3(-0.3f, 0.8f), new Vector3(2.1f, -0.1f) };
            anchors.arraySize = positions.Length;
            for (var index = 0; index < positions.Length; index++)
                anchors.GetArrayElementAtIndex(index).objectReferenceValue = Child(root.transform, "Anchor " + (index + 1), positions[index]).transform;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
