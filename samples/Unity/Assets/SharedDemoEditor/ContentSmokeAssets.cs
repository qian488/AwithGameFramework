using System;
using System.IO;
using AwithGameFrame.Unity.Editor;
using AwithGameFrame.Unity.Hosting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwithGameFrame.Samples.Editor
{
    public static class ContentSmokeAssets
    {
        public static void Prepare()
        {
            // Batch validation owns this project copy; start with a saved bootstrap so
            // Unity permits creating the bundled scene additively.
            EditorSceneManager.OpenScene("Assets/SharedDemo.unity", OpenSceneMode.Single);
            const string source = "Assets/AwithContentSource";
            Directory.CreateDirectory(source);
            AssetDatabase.Refresh();
            var material = AssetDatabase.LoadAssetAtPath<Material>(source + "/Material.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Color"));
                material.color = new Color(0.3f, 0.6f, 0.9f);
                AssetDatabase.CreateAsset(material, source + "/Material.mat");
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(source + "/Prefab.prefab") == null)
            {
                var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    root.GetComponent<MeshRenderer>().sharedMaterial = material;
                    PrefabUtility.SaveAsPrefabAsset(root, source + "/Prefab.prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            if (!File.Exists(source + "/Level.unity"))
            {
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    var marker = new GameObject("Bundled level");
                    SceneManager.MoveGameObjectToScene(marker, scene);
                    EditorSceneManager.SaveScene(scene, source + "/Level.unity");
                }
                finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
            }
            var specification = new UnityContentBuildSpecification
            {
                bundles = new[]
                {
                    new UnityContentBuildBundle { name = "shared", assetPaths = new[] { source + "/Material.mat" } },
                    new UnityContentBuildBundle { name = "prefabs", assetPaths = new[] { source + "/Prefab.prefab" } },
                    new UnityContentBuildBundle { name = "scenes", assetPaths = new[] { source + "/Level.unity" } }
                },
                assets = new[]
                {
                    new UnityBundleAsset { key = "content/material", bundle = "shared", assetPath = source + "/Material.mat" },
                    new UnityBundleAsset { key = "content/prefab", bundle = "prefabs", assetPath = source + "/Prefab.prefab" },
                    new UnityBundleAsset { key = "content/scene", bundle = "scenes", assetPath = source + "/Level.unity", scene = true }
                }
            };
            var specificationPath = Path.Combine(Application.dataPath, "../content-build.json");
            File.WriteAllText(specificationPath, JsonUtility.ToJson(specification, true));
            var output = Path.Combine(Application.streamingAssetsPath, "AwithContent");
            var catalogPath = Path.Combine(output, "bundle-catalog.json");
            if (File.Exists(catalogPath))
            {
                var existing = UnityBundleCatalog.FromJson(File.ReadAllText(catalogPath));
                if (!string.Equals(existing.target, EditorUserBuildSettings.activeBuildTarget.ToString(), StringComparison.Ordinal))
                    throw new InvalidDataException("Sample content was built for " + existing.target + "; use matching content for " + EditorUserBuildSettings.activeBuildTarget + ". Existing output was preserved: " + output);
                return;
            }
            UnityContentBuilder.Build(specificationPath, output, EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }
    }
}
