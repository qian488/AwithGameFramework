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
                }
            }
            if (preview != null)
                EditorGUILayout.HelpBox($"{preview.ArchitectureCount} shapes · {preview.LayoutDigest.Substring(0, 12)}. Scene View shows the layout and fighters. Play mode adds weather, sound and combat.", MessageType.Info);
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
