using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AwithGameFrame.Samples.Editor
{
    /// <summary>Creates ordinary Unity assets before the editor/player smoke is launched.</summary>
    public static class PresentationSmokeAssets
    {
        public static void Prepare()
        {
            // A batch editor starts with an untitled scene; Unity forbids creating another scene
            // additively until the bootstrap has a saved path. Smoke runs own this isolated project.
            EditorSceneManager.OpenScene("Assets/SharedDemo.unity", OpenSceneMode.Single);
            Directory.CreateDirectory("Assets/Resources/AwithSmoke");
            AssetDatabase.Refresh();
            CreateView("Background");
            CreateView("Modal");
            CreateScene("A");
            CreateScene("B");
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var path in new[] { "Assets/SharedDemo.unity", "Assets/AwithSmokeSceneA.unity", "Assets/AwithSmokeSceneB.unity" })
            {
                var index = scenes.FindIndex(scene => scene.path == path);
                if (index < 0) scenes.Add(new EditorBuildSettingsScene(path, true));
                else scenes[index] = new EditorBuildSettingsScene(path, true);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        private static void CreateView(string name)
        {
            var path = "Assets/Resources/AwithSmoke/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var root = new GameObject(name, typeof(RectTransform));
            try
            {
                var button = new GameObject("Focus target", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(root.transform, false);
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(240, 80);
                button.GetComponent<Button>().targetGraphic = button.GetComponent<Image>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CreateScene(string suffix)
        {
            var path = "Assets/AwithSmokeScene" + suffix + ".unity";
            if (File.Exists(path)) return;
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var marker = new GameObject("Smoke scene " + suffix);
                SceneManager.MoveGameObjectToScene(marker, scene);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
    }
}
