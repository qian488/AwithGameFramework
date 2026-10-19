using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AwithGameFrame.Samples.Editor
{
    /// <summary>Creates the ordinary prefabs, WAV and native scene used by the complete sample.</summary>
    public static class DemoExperienceAssets
    {
        public static void Prepare()
        {
            EditorSceneManager.OpenScene("Assets/SharedDemo.unity", OpenSceneMode.Single);
            Directory.CreateDirectory("Assets/Resources/AwithDemo");
            AssetDatabase.Refresh();
            foreach (var name in new[] { "Menu", "Playing", "Settings" })
            {
                string path = "Assets/Resources/AwithDemo/" + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                var root = new GameObject(name, typeof(RectTransform), typeof(DemoUnityView));
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            if (!File.Exists("Assets/Resources/AwithDemo/Tone.wav"))
            {
                const int samples = 11025;
                using var file = File.Create("Assets/Resources/AwithDemo/Tone.wav");
                using var writer = new BinaryWriter(file);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 22050) * 800));
            }
            const string scenePath = "Assets/AwithDemoLevel.unity";
            if (!File.Exists(scenePath))
            {
                var original = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = "Shared adventure level";
                    marker.transform.position = new Vector3(0, 0, 5);
                    SceneManager.MoveGameObjectToScene(marker, scene);
                    EditorSceneManager.SaveScene(scene, scenePath);
                }
                finally { EditorSceneManager.CloseScene(scene, true); if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            }
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(item => item.path == "Assets/SharedDemo.unity")) scenes.Insert(0, new EditorBuildSettingsScene("Assets/SharedDemo.unity", true));
            if (!scenes.Exists(item => item.path == scenePath)) scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.Refresh(); AssetDatabase.SaveAssets();
        }
    }
}
