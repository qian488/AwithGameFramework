using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using WeChatWASM;

internal static class AwithWeChatMiniGameBuild
{
    public static void Run()
    {
        var output = Argument("-awith-game-player");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Mini Game output already exists: " + output);
        var scene = Argument("-awith-game-scene");
        if (!File.Exists(scene)) throw new FileNotFoundException("Missing Mini Game entry scene.", scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene, true) };
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);

        var config = WXConvertCore.config;
        config.ProjectConf.DST = output;
        config.ProjectConf.relativeDST = FileUtil.GetProjectRelativePath(output);
        if (string.IsNullOrEmpty(config.ProjectConf.relativeDST))
            config.ProjectConf.relativeDST = output;
        config.CompileOptions.DevelopBuild = true;
        config.CompileOptions.AutoProfile = false;
        config.CompileOptions.fbslim = false;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(output);

        var result = WXConvertCore.DoExport(true);
        if (result != WXConvertCore.WXExportError.SUCCEED ||
            !File.Exists(Path.Combine(output, WXConvertCore.miniGameDir, "game.js")))
            throw new BuildFailedException("WeChat Mini Game export failed: " + result);
        Debug.Log("AWITH_WECHAT_EXPORT_PASSED");
    }

    private static string Argument(string option)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Supply " + option);
        return args[index + 1];
    }
}
