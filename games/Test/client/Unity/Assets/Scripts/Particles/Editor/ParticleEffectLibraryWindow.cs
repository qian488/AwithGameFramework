using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Test.Client.Editor
{
    internal sealed class ParticleEffectLibraryWindow : EditorWindow
    {
        private const string PresetFolder = "Assets/Particles/Presets";
        private const string DefaultMaterial = "Assets/Particles/SoftAdditive.mat";
        private GameObject[] prefabs = Array.Empty<GameObject>();
        private Vector2 scroll;
        private string search = string.Empty;

        [MenuItem("Tools/Test/Particle Effect Library")]
        private static void Open() => GetWindow<ParticleEffectLibraryWindow>("Particle Effects");

        private void OnEnable() => Refresh();
        private void OnProjectChange() => Refresh();
        private void OnSelectionChange() => Repaint();

        private void Refresh()
        {
            prefabs = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null && prefab.GetComponent<ProceduralParticleEffect>() != null)
                .OrderBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Repaint();
        }

        private void OnGUI()
        {
            var inEditMode = !EditorApplication.isPlayingOrWillChangePlaymode;
            var scene = SceneManager.GetActiveScene();
            var canPlace = inEditMode && PrefabStageUtility.GetCurrentPrefabStage() == null && scene.IsValid() && scene.isLoaded;
            var selected = Selection.activeGameObject;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canPlace))
                {
                    if (GUILayout.Button("New Scene Effect")) CreateSceneEffect();
                }
                using (new EditorGUI.DisabledScope(!canPlace || !CanSave(selected, scene)))
                {
                    if (GUILayout.Button("Save Selected as Prefab")) SaveSelected(selected);
                }
            }

            search = EditorGUILayout.TextField("Find Prefab", search);
            EditorGUILayout.HelpBox("Edit opens Prefab Mode. Select the root to edit Layers and preview in the Inspector. " +
                                    "Use Add to Scene or drag the prefab from Project to place it.", MessageType.Info);

            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                foreach (var prefab in prefabs)
                {
                    if (prefab == null || prefab.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(prefab.name, EditorStyles.miniButton)) EditorGUIUtility.PingObject(prefab);
                        using (new EditorGUI.DisabledScope(!inEditMode))
                        {
                            if (GUILayout.Button("Edit", GUILayout.Width(55)))
                            {
                                PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(prefab));
                                GUIUtility.ExitGUI();
                            }
                        }
                        using (new EditorGUI.DisabledScope(!canPlace))
                        {
                            if (GUILayout.Button("Add to Scene", GUILayout.Width(105))) AddToScene(prefab, scene);
                        }
                    }
                }
            }
        }

        private static bool CanSave(GameObject selected, Scene scene)
        {
            return selected != null && selected.scene == scene && selected.GetComponent<ProceduralParticleEffect>() != null &&
                   (!PrefabUtility.IsPartOfPrefabInstance(selected) || PrefabUtility.IsAnyPrefabInstanceRoot(selected));
        }

        private static void CreateSceneEffect()
        {
            var node = new GameObject("Particle Effect");
            var effect = node.AddComponent<ProceduralParticleEffect>();
            var settings = new SerializedObject(effect);
            settings.FindProperty("playOnEnable").boolValue = true;
            settings.FindProperty("material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterial);
            var layers = settings.FindProperty("layers");
            layers.arraySize = 1;
            var layer = layers.GetArrayElementAtIndex(0);
            layer.FindPropertyRelative("label").stringValue = "Ring";
            layer.FindPropertyRelative("shape").enumValueIndex = 1;
            layer.FindPropertyRelative("enabled").boolValue = true;
            layer.FindPropertyRelative("radius").floatValue = 0.4f;
            layer.FindPropertyRelative("count").intValue = 48;
            layer.FindPropertyRelative("lifetime").floatValue = 1.3f;
            layer.FindPropertyRelative("size").floatValue = 0.28f;
            layer.FindPropertyRelative("speed").floatValue = 1.8f;
            layer.FindPropertyRelative("color").colorValue = new Color(0.25f, 0.85f, 1f);
            layer.FindPropertyRelative("fadeColor").colorValue = new Color(0.1f, 0.35f, 1f, 0f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            Undo.RegisterCreatedObjectUndo(node, "Create particle effect");
            Selection.activeGameObject = node;
        }

        private static void SaveSelected(GameObject selected)
        {
            if (!AssetDatabase.IsValidFolder(PresetFolder)) AssetDatabase.CreateFolder("Assets/Particles", "Presets");
            var path = EditorUtility.SaveFilePanelInProject("Save Particle Prefab", selected.name, "prefab",
                "Choose a Project location for this effect.", PresetFolder);
            if (string.IsNullOrEmpty(path)) return;

            selected.GetComponent<ProceduralParticleEffect>().EndPreview();
            var prefab = PrefabUtility.SaveAsPrefabAsset(selected, AssetDatabase.GenerateUniqueAssetPath(path));
            if (prefab != null) EditorGUIUtility.PingObject(prefab);
        }

        private static void AddToScene(GameObject prefab, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Place particle effect");
            Selection.activeGameObject = instance;
        }
    }
}
