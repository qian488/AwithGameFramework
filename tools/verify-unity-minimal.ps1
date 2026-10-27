[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$EditorPath)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Get-Item -LiteralPath $EditorPath
$validationRoot = Join-Path $repositoryRoot ('artifacts/UnityMinimalValidation/' + [Guid]::NewGuid().ToString('N'))
foreach ($directory in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $validationRoot $directory) | Out-Null
}
$dependencies = [ordered]@{}
foreach ($entry in @(
    @('core', 'src/AwithGameFrame.Core'), @('data', 'src/AwithGameFrame.Data'),
    @('gameplay', 'src/AwithGameFrame.Gameplay'), @('unity', 'adapters/unity')
)) {
    $dependencies['com.awithgameframe.' + $entry[0]] = 'file:' + (Join-Path $repositoryRoot $entry[1]).Replace([char]92, [char]47)
}
$sampleManifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'samples/Unity/Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($property in $sampleManifest.dependencies.PSObject.Properties) {
    if ($property.Name.StartsWith('com.unity.modules.') -or $property.Name -in @('com.unity.ugui', 'com.unity.nuget.newtonsoft-json')) {
        $dependencies[$property.Name] = $property.Value
    }
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $validationRoot 'Packages/manifest.json') -Encoding UTF8
$version = $editor.VersionInfo.ProductVersion.Split('_')[0]
"m_EditorVersion: $version" | Set-Content -LiteralPath (Join-Path $validationRoot 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8
@'
{
  "name": "AwithGameFrame.Minimal.Editor",
  "references": ["AwithGameFrame.Core", "AwithGameFrame.Unity", "AwithGameFrame.Gameplay", "AwithGameFrame.Extensions", "UnityEngine.UI"],
  "includePlatforms": ["Editor"]
}
'@ | Set-Content -LiteralPath (Join-Path $validationRoot 'Assets/Editor/Minimal.asmdef') -Encoding UTF8
@'
using System;
using System.Linq;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Unity.Audio;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Input;
using AwithGameFrame.Unity.Presentation;
using AwithGameFrame.Unity.Scenes;
using UnityEditor;
using UnityEngine;

public static class MinimalSmoke
{
    public static void Run()
    {
        GameObject root = null;
        GameHost host = null;
        try
        {
            var forbidden = new[] { "AwithGameFrame.Foundation", "AwithGameFrame.Unity.Legacy", "AwithGameFrame.Unity.Compatibility", "UniTask", "DOTween", "Mono.Data.Sqlite" };
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name).ToArray();
            if (loaded.Any(x => forbidden.Contains(x))) throw new Exception("Optional/compatibility assembly loaded in minimal project.");
            root = new GameObject("Minimal host", typeof(RectTransform));
            host = new GameHost();
            var resources = new ResourceStore(new UnityResourceLoader(), host.Dispatcher);
            var views = new ViewService(host.Context, new UnityViewFactory((RectTransform)root.transform), resources);
            var scenes = new SceneService(host.Context, new UnitySceneBackend(), resources);
            var input = new InputService(host.Events, new ViewInputRouter(views));
            var audio = new AudioService(resources, new UnityAudioBackend(root.transform));
            host.AddModule(new ResourceModule(resources));
            host.AddModule(new PresentationModule(views));
            host.AddModule(new SceneModule(scenes));
            host.AddModule(new InputModule(input, new UnityInputBackend()));
            host.AddModule(new AudioModule(audio));
            host.StartAsync().GetAwaiter().GetResult();
            host.Tick(0.016);
            host.PauseAsync().GetAwaiter().GetResult();
            host.ResumeAsync().GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();
            Debug.Log("UNITY_MINIMAL_ADAPTER_PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $validationRoot 'Assets/Editor/MinimalSmoke.cs') -Encoding UTF8

$logPath = Join-Path $validationRoot 'Editor.log'
$options = @{
    FilePath = $editor.FullName
    ArgumentList = @('-batchmode', '-nographics', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'MinimalSmoke.Run', '-logFile', ('"' + $logPath + '"'))
    PassThru = $true
}
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $options.WindowStyle = 'Hidden' }
Write-Host "Validating plugin-free Unity $version project: $validationRoot"
$process = Start-Process @options
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw "Minimal Unity validation timed out. See $logPath" }
$process.Refresh()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $logPath) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'UNITY_MINIMAL_ADAPTER_PASSED' -Quiet) -or
    (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet)) {
    throw "Minimal Unity validation failed ($($process.ExitCode)). See $logPath"
}
foreach ($name in @('Foundation', 'Unity.Legacy', 'Unity.Compatibility', 'Unity.DOTween', 'Unity.SQLite')) {
    if (Test-Path -LiteralPath (Join-Path $validationRoot "Library/ScriptAssemblies/AwithGameFrame.$name.dll")) {
        throw "Optional assembly compiled in minimal project: $name"
    }
}
Write-Host "Plugin-free Unity import and module lifecycle passed. Log: $logPath"
