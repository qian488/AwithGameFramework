using System;
using Test.Client.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Test.Client.Combat.Editor
{
    [CustomEditor(typeof(CombatShowcase))]
    internal sealed class CombatShowcaseInspector : UnityEditor.Editor
    {
        private PassWorld preview;

        public override void OnInspectorGUI()
        {
            if (DrawDefaultInspector() && preview != null) Clear();
            var showcase = (CombatShowcase)target;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Generate Preview"))
                    {
                        Clear();
                        try
                        {
                            preview = showcase.GeneratePreview();
                            SceneView.lastActiveSceneView?.LookAtDirect(new Vector3(0, 1.5f, 9), Quaternion.Euler(19, -12, 0), 17);
                            SceneView.RepaintAll();
                        }
                        catch (Exception error) { Debug.LogException(error, showcase); }
                    }
                    using (new EditorGUI.DisabledScope(preview == null))
                        if (GUILayout.Button("Clear Preview")) Clear();
                }
                if (GUILayout.Button("Restore Default Parameters"))
                {
                    Clear();
                    serializedObject.Update();
                    serializedObject.FindProperty("seed").intValue = 27041;
                    serializedObject.FindProperty("detailDensity").intValue = 65;
                    serializedObject.FindProperty("rainDensity").intValue = 485;
                    serializedObject.FindProperty("soundVolume").floatValue = .62f;
                    serializedObject.FindProperty("moonlight").colorValue = new Color(.66f, .79f, .94f);
                    serializedObject.FindProperty("fogColor").colorValue = new Color(.115f, .15f, .187f);
                    serializedObject.FindProperty("characterScale").floatValue = 1;
                    serializedObject.ApplyModifiedProperties();
                    if (showcase.EnvironmentProfile != null)
                    {
                        Undo.RecordObject(showcase.EnvironmentProfile, "Restore Rain Pass Environment Parameters");
                        showcase.EnvironmentProfile.CopyParametersFrom(new RainPassEnvironmentParameters());
                        EditorUtility.SetDirty(showcase.EnvironmentProfile);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
            DrawEnvironmentAssetControls(showcase);
            if (preview != null)
                EditorGUILayout.HelpBox($"{preview.ArchitectureCount} shapes · {preview.LayoutDigest.Substring(0, 12)}. Scene View shows the layout and fighters. Play mode adds weather, sound and combat.", MessageType.Info);
        }

        private void DrawEnvironmentAssetControls(CombatShowcase showcase)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Environment Asset", EditorStyles.boldLabel);
            if (showcase.EnvironmentProfile == null)
            {
                EditorGUILayout.HelpBox("The scene currently uses inline parameters. Create a profile to keep the generator inputs and baked outputs maintainable.", MessageType.Info);
                if (GUILayout.Button("Create Profile from Current Parameters")) CreateProfile(showcase);
                return;
            }

            var profile = showcase.EnvironmentProfile;
            EditorGUILayout.LabelField("Input Hash", profile.CurrentInputHash.Substring(0, 12));
            var validation = RainPassEnvironmentBaker.Validate(profile);
            EditorGUILayout.HelpBox(validation ?? "Baked asset is current and all generated outputs are present.", validation == null ? MessageType.Info : MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Component Values to Profile"))
                {
                    Undo.RecordObject(profile, "Copy Rain Pass Environment Parameters");
                    profile.CopyParametersFrom(showcase.GetEnvironmentParameters());
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Bake Environment Asset"))
                {
                    try
                    {
                        RainPassEnvironmentBaker.Bake(showcase);
                        Debug.Log("Rain Pass environment baked to Assets/Content/RainPass/Generated.", showcase);
                    }
                    catch (Exception error) { Debug.LogException(error, showcase); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate Generated Asset"))
                {
                    var result = RainPassEnvironmentBaker.Validate(profile);
                    if (result == null) Debug.Log("Rain Pass generated asset validation passed.", profile);
                    else Debug.LogError(result, profile);
                }
                if (GUILayout.Button("Clear Generated Asset"))
                {
                    if (EditorUtility.DisplayDialog("Clear Rain Pass Generated Asset", "Delete the generated prefab, materials, textures and manifest?", "Clear", "Cancel"))
                        RainPassEnvironmentBaker.Clear(profile);
                }
            }
        }

        private static void CreateProfile(CombatShowcase showcase)
        {
            EnsureFolder("Assets/Content/RainPass");
            var path = EditorUtility.SaveFilePanelInProject("Create Rain Pass Environment Profile", "RainPassEnvironment", "asset", "Choose a project path for the profile.", "Assets/Content/RainPass");
            if (string.IsNullOrEmpty(path)) return;
            var profile = ScriptableObject.CreateInstance<RainPassEnvironmentProfile>();
            profile.CopyParametersFrom(showcase.GetEnvironmentParameters());
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            var serialized = new SerializedObject(showcase);
            serialized.FindProperty("environmentProfile").objectReferenceValue = profile;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(showcase);
            Selection.activeObject = profile;
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

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            Clear();
        }

        private void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Clear();
        }

        private void Clear()
        {
            preview?.Dispose();
            preview = null;
        }
    }

    public static class CombatCheck
    {
        [MenuItem("Tools/Test/Rain Pass/Open and Play")]
        public static void OpenPlayMode()
        {
            EditorSceneManager.OpenScene(CombatShowcase.ScenePath);
            EditorApplication.delayCall += () =>
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                EditorApplication.isPlaying = true;
            };
        }

        [MenuItem("Tools/Test/Rain Pass/Build Windows Player")]
        public static void BuildWindows()
        {
            string path = EditorUtility.SaveFilePanel("Build Rain Pass", "", "RainPass", "exe");
            if (string.IsNullOrEmpty(path)) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { CombatShowcase.ScenePath }, locationPathName = path,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Rain Pass build: " + report.summary.result);
        }
    }
}
