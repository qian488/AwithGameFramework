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
            PresentationSmokeAssets.Prepare();
            ContentSmokeAssets.Prepare();
            DemoExperienceAssets.Prepare();
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
            PresentationSmokeAssets.Prepare();
            ContentSmokeAssets.Prepare();
            DemoExperienceAssets.Prepare();
            var targetIndex = Array.IndexOf(arguments, "-awith-build-target");
            var target = targetIndex < 0 ? BuildTarget.StandaloneWindows64 : (BuildTarget)Enum.Parse(typeof(BuildTarget), arguments[targetIndex + 1]);
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var backendIndex = Array.IndexOf(arguments, "-awith-scripting-backend");
            var backend = backendIndex < 0 ? ScriptingImplementation.Mono2x : (ScriptingImplementation)Enum.Parse(typeof(ScriptingImplementation), arguments[backendIndex + 1]);
            var previous = PlayerSettings.GetScriptingBackend(group);
            try
            {
                PlayerSettings.SetScriptingBackend(group, backend);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/SharedDemo.unity", "Assets/AwithSmokeSceneA.unity", "Assets/AwithSmokeSceneB.unity", "Assets/AwithDemoLevel.unity" },
                    locationPathName = arguments[index + 1],
                    target = target,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build failed: " + report.summary.result);
                Debug.Log("UNITY_SHARED_PLAYER_BUILT bytes=" + report.summary.totalSize);
            }
            finally { PlayerSettings.SetScriptingBackend(group, previous); }
        }
    }
}
