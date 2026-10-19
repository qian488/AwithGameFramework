using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AwithGameFrame.Samples.Editor
{
    public static class SharedHostSmoke
    {
        public static void Run()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-awith-shared-smoke") < 0)
                throw new ArgumentException("Pass -awith-shared-smoke; this entry enters PlayMode and exits after assertions.");
            EditorSceneManager.OpenScene("Assets/SharedDemo.unity");
            EditorApplication.isPlaying = true;
        }

        public static void BuildPlayer()
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, "-awith-player-output");
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Pass -awith-player-output <exe path>.");
            var previous = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            try
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/SharedDemo.unity" },
                    locationPathName = arguments[index + 1],
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build failed: " + report.summary.result);
                Debug.Log("UNITY_SHARED_PLAYER_BUILT bytes=" + report.summary.totalSize);
            }
            finally { PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previous); }
        }
    }
}
