[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$EditorPath, [switch]$DiagnosticsOnly, [switch]$RunLiveEditor)

$ErrorActionPreference = 'Stop'
if ($DiagnosticsOnly -and -not $RunLiveEditor) {
    throw '-DiagnosticsOnly requires -RunLiveEditor because it opens an interactive editor session.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Get-Item -LiteralPath $EditorPath
$isolatedProject = Join-Path $repository ('artifacts/UnityAutomationValidation/' + [Guid]::NewGuid().ToString('N'))
foreach ($directory in @('Assets/Editor', 'Assets/Scenes', 'Packages', 'ProjectSettings')) {
    [IO.Directory]::CreateDirectory((Join-Path $isolatedProject $directory)) | Out-Null
}
$dependencies = [ordered]@{}
foreach ($package in @('src/AwithGameFrame.Core', 'src/AwithGameFrame.Data', 'src/AwithGameFrame.Gameplay', 'adapters/unity')) {
    $packageRoot = Join-Path $repository $package
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
    $dependencies[$manifest.name] = 'file:' + $packageRoot.Replace([char]92, [char]47)
}
$modules = (Get-Content -LiteralPath (Join-Path $repository 'samples/Unity/Packages/manifest.json') -Raw | ConvertFrom-Json).dependencies
foreach ($module in $modules.PSObject.Properties | Where-Object { $_.Name.StartsWith('com.unity.modules.') }) {
    $dependencies[$module.Name] = $module.Value
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $isolatedProject 'Packages/manifest.json') -Encoding UTF8
$version = $editor.VersionInfo.ProductVersion.Split('_')[0]
"m_EditorVersion: $version" | Set-Content -LiteralPath (Join-Path $isolatedProject 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8

@'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AutomationValidation
{
    public sealed class FixtureValues : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public float weight = 1.5f;
        }

        public int revision = 10;
        public Entry[] entries = { new Entry() };
        public GameObject linkedObject;

        public static void CreateFixture()
        {
            var root = new GameObject("Automation Fixture", typeof(FixtureValues));
            try
            {
                if (PrefabUtility.SaveAsPrefabAsset(root, "Assets/Automation Fixture.prefab") == null)
                    throw new InvalidOperationException("Could not create the fixture prefab.");
            }
            finally { DestroyImmediate(root); }
            CreateHealthFixture();
        }

        public static void StartLive()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/AutomationSmoke.unity");
            if (!EditorApplication.ExecuteMenuItem("Tools/AwithGameFrame/Enable Editor Automation"))
                throw new InvalidOperationException("Could not enable Unity automation.");
            EditorApplication.update += PollProbe;
        }

        private static void PollProbe()
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "fixture-command");
            if (!File.Exists(path)) return;
            var command = File.ReadAllText(path);
            File.Delete(path);
            if (command == "undo") Undo.PerformUndo();
            if (command == "quit") EditorApplication.Exit(0);
            File.WriteAllText(path + ".done", command);
        }

        public static void EditPrefab()
        {
            var prefabPath = "Assets/Automation Fixture.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                root.GetComponent<FixtureValues>().revision = 73;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateHealthFixture()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera));
            var fixture = new GameObject("Health Fixture", typeof(FixtureValues));
            fixture.GetComponent<FixtureValues>().linkedObject = camera;
            const string path = "Assets/Scenes/HealthSmoke.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save the health fixture.");

            // Keep the script reference to a non-existent scene object in the saved fixture.
            var text = File.ReadAllText(path);
            var modified = System.Text.RegularExpressions.Regex.Replace(text, @"linkedObject: \{fileID: \d+\}", "linkedObject: {fileID: 9000000000000}");
            if (text == modified) throw new InvalidOperationException("Unity did not serialize the health fixture reference.");
            File.WriteAllText(path, modified);
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $isolatedProject 'Assets/FixtureValues.cs') -Encoding UTF8

$setupLog = Join-Path $isolatedProject 'Setup.log'
$options = @{
    FilePath = $editor.FullName
    ArgumentList = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $isolatedProject + '"'),
        '-executeMethod', 'AutomationValidation.FixtureValues.CreateFixture', '-logFile', ('"' + $setupLog + '"'))
    PassThru = $true
}
$previousProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE')
try {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $options.WindowStyle = 'Hidden'
        if (!$previousProfile) { $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData') }
    }
    $setup = Start-Process @options
}
finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }
try {
    if (!$setup.WaitForExit(180000)) { $setup.Kill(); throw "Unity fixture setup timed out. See $setupLog" }
    if ($setup.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $isolatedProject 'Assets/Automation Fixture.prefab'))) {
        throw "Unity fixture setup failed. See $setupLog"
    }
}
finally { $setup.Dispose() }

$commandPath = Join-Path $PSScriptRoot 'unity-editor.ps1'
$defaults = @{ ProjectPath = $isolatedProject; EditorPath = $editor.FullName; TimeoutSeconds = 180 }
$scene = 'Assets/Scenes/AutomationSmoke.unity'
$prefab = 'Assets/Automation Fixture.prefab'
Write-Host "Unity automation validation project: $isolatedProject"

if ($DiagnosticsOnly) {
    @'
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class DiagnosticsProbe
{
    private const string ReloadKey = "AwithGameFrame.DiagnosticsProbe.ReloadRequested";
    private static string ProjectFile(string name) => Path.Combine(Path.GetDirectoryName(Application.dataPath), name);

    static DiagnosticsProbe()
    {
        if (Application.isBatchMode) return;
        EditorApplication.update += CheckCommand;
        if (SessionState.GetBool(ReloadKey, false))
        {
            SessionState.SetBool(ReloadKey, false);
            EditorApplication.delayCall += () => File.WriteAllText(ProjectFile("diagnostics-probe-reloaded"), "done");
        }
    }

    public static void Run()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/AwithGameFrame/Enable Editor Automation"))
                throw new System.InvalidOperationException("Could not enable Unity automation.");
            Debug.LogWarning("Diagnostic probe original warning");
            Debug.LogWarning("Diagnostic probe latest warning");
            CompilationPipeline.compilationStarted += HoldFirstCompilation;
        };
    }

    private static void HoldFirstCompilation(object context)
    {
        CompilationPipeline.compilationStarted -= HoldFirstCompilation;
        Thread.Sleep(2000);
    }

    private static void CheckCommand()
    {
        var commandPath = ProjectFile("diagnostics-probe-command");
        if (!File.Exists(commandPath)) return;
        var command = File.ReadAllText(commandPath);
        File.Delete(commandPath);
        if (command == "refresh") AssetDatabase.Refresh();
        if (command == "reload")
        {
            SessionState.SetBool(ReloadKey, true);
            EditorUtility.RequestScriptReload();
        }
        if (command == "quit") EditorApplication.Exit(0);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $isolatedProject 'Assets/Editor/DiagnosticsProbe.cs') -Encoding UTF8
    $live = @{ ProjectPath = $isolatedProject; Action = 'editor.diagnostics' }
    $probePath = Join-Path $isolatedProject 'diagnostics-probe-command'
    $reloadMarker = Join-Path $isolatedProject 'diagnostics-probe-reloaded'
    $probeLog = Join-Path $isolatedProject 'LiveDiagnostics.log'
    $brokenSource = Join-Path $isolatedProject 'Assets/DiagnosticBroken.cs'
    $before = & $commandPath @live | ConvertFrom-Json
    if ($before.state -ne 'editor-unavailable') { throw 'A closed project reported a live diagnostics session.' }
    $liveOptions = @{
        FilePath = $editor.FullName
        ArgumentList = @('-automated', '-projectPath', ('"' + $isolatedProject + '"'), '-executeMethod', 'DiagnosticsProbe.Run', '-logFile', ('"' + $probeLog + '"'))
        PassThru = $true
    }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $liveOptions.WindowStyle = 'Hidden' }
    try {
        if (!$previousProfile -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData')
        }
        $liveEditor = Start-Process @liveOptions
    }
    finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }

    function Wait-Diagnostics([scriptblock]$condition, [string]$description) {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
        while (!$liveEditor.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
            $snapshot = & $commandPath @live | ConvertFrom-Json
            if (& $condition $snapshot) { return $snapshot }
            Start-Sleep -Milliseconds 100
        }
        throw "Diagnostics did not report $description. See $probeLog"
    }

    function Send-ProbeCommand([string]$command) {
        $pendingCommand = $probePath + '.tmp'
        [IO.File]::WriteAllText($pendingCommand, $command)
        [IO.File]::Move($pendingCommand, $probePath)
    }

    function Write-BrokenSource {
        [IO.File]::WriteAllText($brokenSource, 'public class DiagnosticBroken { MissingDiagnosticType field; }')
    }

    try {
        $null = Wait-Diagnostics { param($result) $result.logs.message -contains 'Diagnostic probe latest warning' } 'the runtime warning'
        $limited = & $commandPath @live -Limit 1 | ConvertFrom-Json
        if (!$limited.logsTruncated -or $limited.logs.Count -ne 1 -or $limited.logs[0].message -ne 'Diagnostic probe latest warning') {
            throw 'Live diagnostic limit did not retain the latest warning.'
        }

        Write-BrokenSource
        Send-ProbeCommand 'refresh'
        $null = Wait-Diagnostics { param($result) $result.compilationState -eq 'compiling' } 'an active compilation'
        $failed = Wait-Diagnostics { param($result) $result.compilationState -eq 'failed' } 'a compilation failure'
        $errorAtSource = $failed.compilerMessages | Where-Object { $_.file -like '*DiagnosticBroken.cs' -and $_.severity -eq 'error' }
        if (!$errorAtSource -or $errorAtSource.line -ne 1 -or $errorAtSource.column -lt 1) { throw 'Live diagnostic omitted the source location.' }

        [IO.File]::WriteAllText($brokenSource, 'public class DiagnosticBroken { }')
        Send-ProbeCommand 'refresh'
        $success = Wait-Diagnostics { param($result) $result.compilationState -eq 'succeeded' } 'successful recompilation'
        $probeInfo = & $commandPath -ProjectPath $isolatedProject -Action project.info -TimeoutSeconds 30 | ConvertFrom-Json
        if (!$probeInfo.ok) { throw 'The bridge did not resume after recompilation.' }
        Send-ProbeCommand 'reload'
        $null = Wait-Diagnostics { param($result) (Test-Path -LiteralPath $reloadMarker) -and $result.updatedUtc -gt $success.updatedUtc -and
            $result.compilationState -eq 'succeeded' -and $result.logs.message -contains 'Diagnostic probe original warning' } 'the retained session after domain reload'
        Send-ProbeCommand 'quit'
        if (!$liveEditor.WaitForExit(30000)) { throw "The live editor did not exit. See $probeLog" }
    }
    finally {
        if (!$liveEditor.HasExited) {
            if (!(Test-Path -LiteralPath $probePath)) { Send-ProbeCommand 'quit' }
            if (!$liveEditor.WaitForExit(15000)) { $liveEditor.Kill(); $liveEditor.WaitForExit(5000) | Out-Null }
        }
        $liveEditor.Dispose()
    }

    $closed = & $commandPath @live | ConvertFrom-Json
    if ($closed.state -ne 'editor-unavailable' -or (Test-Path -LiteralPath (Join-Path $isolatedProject 'Library/AwithGameFrameAutomation/diagnostics.json'))) {
        throw 'The live session published diagnostics after exit.'
    }
    Write-BrokenSource
    $batch = & $commandPath @defaults -Action editor.diagnostics | ConvertFrom-Json
    $batchError = $batch.compilerMessages | Where-Object { $_.file -like '*DiagnosticBroken.cs' -and $_.severity -eq 'error' }
    if ($batch.state -ne 'compilation-failed' -or !$batchError -or $batchError[0].line -ne 1 -or !(Test-Path -LiteralPath $batch.logPath)) {
        throw "Batch diagnosis did not identify the compiler failure. See $($batch.logPath)"
    }
    try {
        $null = & $commandPath @defaults -Action project.info
        throw 'A broken project unexpectedly executed a bridge command.'
    }
    catch {
        if ($_.Exception.Message -notlike '*Unity compilation failed*DiagnosticBroken.cs:1:*') { throw }
    }
    Write-Host 'UNITY_EDITOR_DIAGNOSTICS_PASSED'
    return
}

$info = & $commandPath @defaults -Action project.info | ConvertFrom-Json
if (!$info.unityVersion -or $info.projectPath -ne $isolatedProject) { throw 'Wrong project or missing version.' }
$assets = & $commandPath @defaults -Action asset.find -Query 't:Prefab Automation Fixture' | ConvertFrom-Json
if ($assets.assets -notcontains $prefab) { throw 'The prefab search did not return the fixture asset.' }

$healthScene = 'Assets/Scenes/HealthSmoke.unity'
$healthPath = Join-Path $isolatedProject $healthScene
$healthBefore = (Get-FileHash -LiteralPath $healthPath).Hash
$scan = & $commandPath @defaults -Action scene.health -Scene $healthScene | ConvertFrom-Json
if ($scan.health.enabledCameraCount -ne 1 -or $scan.health.objectCount -ne 2 -or
    $scan.health.issueCount -ne 1 -or $scan.health.issues[0].path -ne '/Health Fixture' -or
    $scan.health.issues[0].kind -ne 'missing-reference' -or $scan.health.issues[0].property -ne 'linkedObject' -or
    (Get-FileHash -LiteralPath $healthPath).Hash -ne $healthBefore) {
    throw 'Scene health scan did not report the expected broken reference and camera count without writing.'
}

$null = & $commandPath @defaults -Action scene.create -Scene $scene
$null = & $commandPath @defaults -Action object.create -Scene $scene -Save -ObjectName Anchor -Position 1,2,0
$prefabInspection = & $commandPath @defaults -Action prefab.inspect -Asset $prefab | ConvertFrom-Json
if ($prefabInspection.asset -cne $prefab -or $prefabInspection.assetGuid -notmatch '^[a-f0-9]{32}$' -or
    $prefabInspection.prefabKind -ne 'Regular' -or '/Automation Fixture' -cne $prefabInspection.objects[0].path -or
    'AutomationValidation.FixtureValues' -notin $prefabInspection.objects[0].components -or
    'Assets/FixtureValues.cs' -notin $prefabInspection.assets) {
    throw 'Prefab inspection did not read the asset identity, hierarchy and script dependency.'
}
$null = & $commandPath @defaults -Action prefab.place -Scene $scene -Save -Asset $prefab -ObjectName Preview -Parent '/Anchor'
$null = & $commandPath @defaults -Action component.add -Scene $scene -Save -Target '/Anchor' -Kind Light
$null = & $commandPath @defaults -Action object.transform -Scene $scene -Save -Target '/Anchor/Preview' -Position 2,0,0

$inspection = & $commandPath @defaults -Action scene.inspect -Scene $scene | ConvertFrom-Json
$anchor = $inspection.objects | Where-Object path -eq '/Anchor'
$placed = $inspection.objects | Where-Object path -eq '/Anchor/Preview'
if ($inspection.dirty -or $anchor.components -notcontains 'UnityEngine.Light' -or $anchor.localPosition.y -ne 2 -or
    $placed.components -notcontains 'AutomationValidation.FixtureValues' -or $placed.localPosition.x -ne 2) {
    throw 'The saved scene does not contain the requested changes.'
}

$savedFile = Join-Path $isolatedProject $scene
$component = @{ Scene = $scene; Target = '/Anchor/Preview'; Kind = 'AutomationValidation.FixtureValues' }
$nestedPath = 'entries.Array.data[0].weight'
$weight = (& $commandPath @defaults @component -Action component.inspect -Query $nestedPath | ConvertFrom-Json).properties
if ($weight.Count -ne 1 -or !$weight[0].writable -or $weight[0].value -ne '1.5') { throw 'The nested fixture field was not discovered.' }
$beforePreview = (Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash
$changes = [ordered]@{ revision = 42; $nestedPath = 3.5 }
$preview = & $commandPath @defaults @component -Action component.set -Properties $changes -Preview | ConvertFrom-Json
$previewWeight = $preview.properties | Where-Object path -eq $nestedPath
if (!$preview.preview -or $preview.dirty -or $previewWeight.previousValue -ne $weight[0].value -or $previewWeight.value -ne '3.5' -or
    (Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash -cne $beforePreview) {
    throw 'The property preview did not predict the change without saving.'
}
try {
    $null = & $commandPath @defaults @component -Action component.set -Save -Properties ([ordered]@{ revision = 99; $nestedPath = 'invalid' })
    throw 'Invalid property update unexpectedly succeeded.'
}
catch {
    if ($_.Exception.Message -notlike '*Unsupported or invalid value for*') { throw }
}
if ((Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash -cne $beforePreview) { throw 'A rejected property edit changed the scene.' }

$null = & $commandPath @defaults @component -Action component.set -Save -Properties $changes
$edited = (& $commandPath @defaults @component -Action component.inspect | ConvertFrom-Json).properties
if (($edited | Where-Object path -eq $nestedPath).value -ne '3.5' -or ($edited | Where-Object path -eq revision).value -ne '42') {
    throw 'The saved fixture component did not contain both changes.'
}

$before = (Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash
try {
    $null = & $commandPath @defaults -Action object.create -Scene $scene -Save -ObjectName Anchor
    throw 'Duplicate creation unexpectedly succeeded.'
}
catch {
    if ($_.Exception.Message -notlike '*A sibling already has that name*') { throw }
}
if ((Get-FileHash -LiteralPath $savedFile -Algorithm SHA256).Hash -cne $before) { throw 'A rejected edit changed the saved scene.' }

$transactionPath = Join-Path $isolatedProject 'transaction.json'
$failurePath = Join-Path $isolatedProject 'transaction-failure.json'
$transactionSteps = @(
    @{ action = 'object.create'; name = 'Rig'; kind = 'Empty' },
    @{ action = 'prefab.place'; asset = $prefab; name = 'Marker'; parent = '/Rig' },
    @{ action = 'object.transform'; target = '/Anchor'; position = @(8, 9, 0) },
    @{ action = 'component.set'; kind = $component.Kind; target = '/Anchor/Preview'; properties = @(@{ path = 'revision'; value = '71' }) },
    @{ action = 'component.add'; kind = 'ParticleSystem'; target = '/Rig' },
    @{ action = 'component.set'; kind = 'UnityEngine.ParticleSystem'; target = '/Rig'; properties = @(@{ path = 'playOnAwake'; value = 'false' }) }
)
@{ version = 1; steps = $transactionSteps } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $transactionPath -Encoding UTF8
@{ version = 1; steps = $transactionSteps + @(@{ action = 'object.transform'; target = '/Missing'; position = @(1, 0, 0) }) } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $failurePath -Encoding UTF8

$policyFile = Join-Path $isolatedProject 'ProjectSettings/AwithEditorAutomation.json'
@{ version = 1; live = @(); batch = @('scene.transaction', 'scene.inspect', 'object.create', 'audit.list', 'performance.inspect') } |
    ConvertTo-Json | Set-Content -LiteralPath $policyFile -Encoding UTF8
$batchTransaction = @{ Action = 'scene.transaction'; Scene = $scene; StepsFile = $transactionPath }
try {
    $null = & $commandPath @defaults @batchTransaction -Save
    throw 'A transaction bypassed the project allowlist.'
}
catch { if ($_.Exception.Message -notlike '*policy denies action: prefab.place*') { throw } }
if ((Get-FileHash -LiteralPath $savedFile).Hash -cne $before) { throw 'Denied transaction wrote the scene.' }

$policy = @{ version = 1; live = @('project.info', 'scene.inspect', 'component.inspect', 'scene.transaction', 'object.create', 'object.transform', 'prefab.place', 'component.set', 'component.add');
    batch = @('scene.inspect', 'scene.transaction', 'object.create', 'object.transform', 'prefab.place', 'component.set', 'component.add', 'component.inspect', 'audit.list', 'performance.inspect',
        'checkpoint.capture', 'checkpoint.list', 'checkpoint.inspect', 'checkpoint.restore') }
$policy | ConvertTo-Json | Set-Content -LiteralPath $policyFile -Encoding UTF8

if ($RunLiveEditor) {
    $liveOptions = @{ FilePath = $editor.FullName; PassThru = $true; ArgumentList = @('-automated', '-projectPath', ('"' + $isolatedProject + '"'),
        '-executeMethod', 'AutomationValidation.FixtureValues.StartLive', '-logFile', ('"' + (Join-Path $isolatedProject 'LiveTransaction.log') + '"')) }
    $live = @{ ProjectPath = $isolatedProject; TimeoutSeconds = 30 }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $liveOptions.WindowStyle = 'Hidden' }
    try {
        if (!$previousProfile -and $liveOptions.WindowStyle) { $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData') }
        $liveEditor = Start-Process @liveOptions
    }
    finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }

    function Invoke-LiveProbe([string]$Command) {
        $probe = Join-Path $isolatedProject 'fixture-command'
        [IO.File]::WriteAllText($probe + '.tmp', $Command)
        [IO.File]::Move($probe + '.tmp', $probe)
        $probeDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        while (!$liveEditor.HasExited -and [DateTimeOffset]::UtcNow -lt $probeDeadline) {
            if ((Test-Path -LiteralPath ($probe + '.done')) -and (Get-Content -LiteralPath ($probe + '.done') -Raw) -eq $Command) { return }
            Start-Sleep -Milliseconds 150
        }
        if ($Command -ne 'quit' -or !$liveEditor.HasExited) { throw "Live fixture did not complete $Command." }
    }

    function Assert-Reverted([bool]$Dirty) {
        $state = & $commandPath @live -Action scene.inspect -Scene $scene | ConvertFrom-Json
        $field = & $commandPath @live @component -Action component.inspect -Query revision | ConvertFrom-Json
        if ($state.dirty -ne $Dirty -or $state.objects.path -contains '/Rig' -or ($state.objects | Where-Object path -eq '/Anchor').localPosition.x -ne 1 -or
            $field.properties[0].value -ne '42' -or (Get-FileHash -LiteralPath $savedFile).Hash -cne $before) { throw 'Reverted scene contents, dirty flag or saved file did not match.' }
    }

    try {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
        do {
            if ($liveEditor.HasExited -or [DateTimeOffset]::UtcNow -ge $deadline) { throw 'Live transaction fixture failed to start. See LiveTransaction.log.' }
            $status = & $commandPath @live -Action editor.diagnostics | ConvertFrom-Json
            if ($status.state -eq 'editor-unavailable') { Start-Sleep -Milliseconds 250 }
        } while ($status.state -eq 'editor-unavailable')

        $simulation = & $commandPath @live @batchTransaction -Preview | ConvertFrom-Json
        if ($simulation.outcome -ne 'previewed' -or $simulation.steps.Count -ne 6 -or $simulation.steps[1].objects[0].path -ne '/Rig/Marker' -or
            $simulation.steps[3].properties[0].previousValue -ne '42' -or !(Test-Path -LiteralPath (Join-Path $isolatedProject ('Library/AwithGameFrameAutomation/' + $simulation.id + '.result')))) {
            throw 'The preview omitted expected operation records.'
        }
        Assert-Reverted $false
        try {
            $null = & $commandPath @live -Action scene.transaction -Scene $scene -StepsFile $failurePath
            throw 'The broken transaction unexpectedly succeeded.'
        }
        catch {
            if ($_.Exception.Message -notmatch 'Transaction stopped after 6 completed step.* Record: (.+\.result)$') { throw }
            $audit = Get-Content -LiteralPath $Matches[1] -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($audit.ok -or $audit.outcome -ne 'reverted' -or $audit.steps[0].outcome -ne 'reverted' -or $audit.steps[6].outcome -ne 'failed') {
                throw 'The failure audit did not record reverted and failed steps.'
            }
        }
        Assert-Reverted $false

        $null = & $commandPath @live -Action object.create -Scene $scene -ObjectName KeepUnsaved
        $null = & $commandPath @live @batchTransaction -Preview
        Assert-Reverted $true
        $applied = & $commandPath @live @batchTransaction | ConvertFrom-Json
        if ($applied.outcome -ne 'applied' -or !$applied.dirty) { throw 'Live transaction did not leave a pending scene edit.' }
        Invoke-LiveProbe 'undo'
        Assert-Reverted $true
        $unrelated = & $commandPath @live -Action scene.inspect -Scene $scene -Target '/KeepUnsaved' | ConvertFrom-Json
        if ($unrelated.objects[0].path -ne '/KeepUnsaved') { throw 'Undo lost prior unsaved work.' }
        try {
            $null = & $commandPath @live -Action asset.find -Query 't:Prefab'
            throw 'An excluded live read unexpectedly succeeded.'
        }
        catch { if ($_.Exception.Message -notlike '*policy denies action: asset.find*') { throw } }
    }
    finally {
        try {
            if (!$liveEditor.HasExited) { Invoke-LiveProbe 'quit' }
        }
        finally {
            if (!$liveEditor.WaitForExit(12000)) { $liveEditor.Kill(); $liveEditor.WaitForExit() }
            $liveEditor.Dispose()
        }
    }
}

$sceneCheckpoint = (& $commandPath @defaults -Action checkpoint.capture -Asset $scene | ConvertFrom-Json).checkpoint
$prefabCheckpoint = (& $commandPath @defaults -Action checkpoint.capture -Asset $prefab | ConvertFrom-Json).checkpoint
$batchApplied = & $commandPath @defaults @batchTransaction -Save | ConvertFrom-Json
if ($batchApplied.outcome -ne 'applied' -or $batchApplied.dirty -or (Get-FileHash -LiteralPath $savedFile).Hash -ceq $before) { throw 'The batch transaction was not persisted.' }
$auditEntries = (& $commandPath @defaults -Action audit.list -Limit 20 | ConvertFrom-Json).audit
if (!$auditEntries -or !($auditEntries | Where-Object { $_.id -eq $batchApplied.id -and $_.action -eq 'scene.transaction' -and $_.ok -and $_.outcome -eq 'applied' })) {
    throw 'The persisted editor audit did not record the applied transaction.'
}
$performance = & $commandPath @defaults -Action performance.inspect | ConvertFrom-Json
if (!$performance.performance -or $performance.performance.unityVersion -ne $version -or
    $performance.performance.gameObjects -lt 1 -or $performance.performance.components -lt 1 -or
    $performance.performance.totalReservedMemory -lt $performance.performance.totalAllocatedMemory) {
    throw 'The editor performance snapshot did not report valid object, component or memory counters.'
}
$particle = & $commandPath @defaults -Action component.inspect -Scene $scene -Target '/Rig' -Kind UnityEngine.ParticleSystem -Query playOnAwake | ConvertFrom-Json
if ($particle.properties[0].value -ne 'false') { throw 'The native particle setting did not persist.' }
try {
    $null = & $commandPath @defaults -Action checkpoint.restore -Checkpoint $sceneCheckpoint.id -ExpectedHash $sceneCheckpoint.savedHash
    throw 'Restore accepted a stale hash.'
}
catch { if ($_.Exception.Message -notlike '*identity or hash differs*') { throw } }
$seen = (& $commandPath @defaults -Action checkpoint.inspect -Checkpoint $sceneCheckpoint.id | ConvertFrom-Json).checkpoint
if ($seen.currentHash -eq $seen.savedHash -or $seen.currentHash -ne (Get-FileHash -LiteralPath $savedFile).Hash) { throw 'Scene checkpoint did not see the newer edit.' }
$null = & $commandPath @defaults -Action checkpoint.restore -Checkpoint $seen.id -ExpectedHash $seen.currentHash
$restoredScene = & $commandPath @defaults -Action scene.inspect -Scene $scene | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $savedFile).Hash -cne $before -or $restoredScene.objects.path -contains '/Rig') { throw 'Scene bytes or contents differed after restore.' }

$options.ArgumentList = $options.ArgumentList -replace '^AutomationValidation.FixtureValues.CreateFixture$', 'AutomationValidation.FixtureValues.EditPrefab'
$prefabEdit = Start-Process @options
try { if (!$prefabEdit.WaitForExit(120000) -or $prefabEdit.ExitCode -ne 0) { throw 'Could not change the test Prefab through Unity.' } }
finally { $prefabEdit.Dispose() }
$list = & $commandPath @defaults -Action checkpoint.list | ConvertFrom-Json
if ($list.checkpoints.id -notcontains $sceneCheckpoint.id -or $list.checkpoints.id -notcontains $prefabCheckpoint.id) { throw 'Checkpoints were not discoverable after restart.' }
$seenPrefab = (& $commandPath @defaults -Action checkpoint.inspect -Checkpoint $prefabCheckpoint.id | ConvertFrom-Json).checkpoint
if ($seenPrefab.currentHash -eq $seenPrefab.savedHash) { throw 'The fixture prefab did not change.' }
$metaBefore = (Get-FileHash -LiteralPath (Join-Path $isolatedProject ($prefab + '.meta'))).Hash
$null = & $commandPath @defaults -Action checkpoint.restore -Checkpoint $seenPrefab.id -ExpectedHash $seenPrefab.currentHash
if ((Get-FileHash -LiteralPath (Join-Path $isolatedProject $prefab)).Hash -ne $seenPrefab.savedHash -or
    (Get-FileHash -LiteralPath (Join-Path $isolatedProject ($prefab + '.meta'))).Hash -cne $metaBefore) { throw 'Prefab restore changed its identity or failed to restore it.' }

Write-Host 'UNITY_EDITOR_AUTOMATION_BATCH_PASSED'
if ($RunLiveEditor) { Write-Host 'UNITY_EDITOR_AUTOMATION_PASSED' }
