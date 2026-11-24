using System;
using System.IO;
using AwithGameFrame.Unity.HybridUpdate.Editor;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class HybridBuild
{
    public static void Prepare()
    {
        var settings = HybridCLRSettings.Instance;
        settings.enable = true;
        settings.useGlobalIl2cpp = false;
        settings.hotUpdateAssemblies = new[] { "AwithHybridFixture.Hot" };
        settings.patchAOTAssemblies = new[] { "mscorlib" };
        HybridCLRSettings.Save();
        var installer = new InstallerController();
        if (!installer.HasInstalledHybridCLR())
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-awith-hybrid-il2cpp");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Supply -awith-hybrid-il2cpp.");
            installer.InstallFromLocal(Path.GetFullPath(args[index + 1]));
        }
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Hybrid Fixture").AddComponent(Type.GetType("HybridSmoke, AwithHybridFixture.Bootstrap", true));
        const string path = "Assets/Fixture.unity";
        EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
    }

    public static void Generate() => HybridClrExport.Generate();

    public static void Build()
    {
        var result = BuildPipeline.BuildPlayer(new[] { "Assets/Fixture.unity" }, "Out/HybridSmoke.exe",
            BuildTarget.StandaloneWindows64, BuildOptions.Development);
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new BuildFailedException(result.summary.result.ToString());
        HybridClrExport.Export(BuildTarget.StandaloneWindows64, "fixture-player", "Out/HybridSmoke_Data/StreamingAssets/update");
        Debug.Log("AWITH_HYBRIDCLR_BUILD_PASSED");
    }
}
