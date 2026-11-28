[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')][string]$Name,
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [string]$BuildScene,
    [ValidateSet('Windows64', 'Android', 'WebGL', 'WeChatMiniGame')][string]$PlayerTarget = 'Windows64',
    [string]$WeChatSdkPath,
    [switch]$RunTests,
    [ValidateSet('All', 'EditMode', 'PlayMode')][string]$TestMode = 'All',
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_.+]*$')][string]$TestFilter,
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 600,
    [string]$VisualSpec,
    [string]$ResourceSpec,
    [string]$AtlasSpec,
    [switch]$KeepProject,
    [string[]]$AdditionalBuildScenes = @(),
    [switch]$ReleasePlayer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (($TestMode -ne 'All' -or $TestFilter) -and -not $RunTests) { throw 'Test selection requires -RunTests.' }
if ($TestFilter -and $TestMode -eq 'All') { throw '-TestFilter requires -TestMode EditMode or PlayMode.' }
. (Join-Path $PSScriptRoot 'verify-game-unity-metadata.ps1')
. (Join-Path $PSScriptRoot 'unity-verification-cleanup.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repository "games/$Name"
$gameFile = Join-Path $root 'game.json'
$game = [IO.File]::ReadAllText($gameFile) | ConvertFrom-Json
if ($game.schemaVersion -ne 1 -or $game.name -cne $Name -or $game.engine -cne 'unity') { throw "Expected a Unity game workspace: $gameFile" }
$project = Join-Path $root 'client/Unity'
$editor = Get-Item -LiteralPath $EditorPath
if ($editor.PSIsContainer) { throw "Expected the Unity executable: $EditorPath" }
$assets = Join-Path $project 'Assets'
Assert-GameUnityMetadata -AssetsPath $assets
$versionText = [IO.File]::ReadAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'))
if ($versionText -notmatch '(?m)^m_EditorVersion: (\S+)') { throw "No Unity version recorded in $project" }
$expectedVersion = $Matches[1]
if ($ResourceSpec) {
    $resourceSpecification = Get-Item -LiteralPath $ResourceSpec
    if ($resourceSpecification.PSIsContainer) { throw 'ResourceSpec must be a JSON file.' }
    $ResourceSpec = $resourceSpecification.FullName
}
if ($AtlasSpec) {
    $atlasSpecification = Get-Item -LiteralPath $AtlasSpec
    if ($atlasSpecification.PSIsContainer) { throw 'AtlasSpec must be a JSON file.' }
    $AtlasSpec = $atlasSpecification.FullName
}
$visualWithGraphics = $false
if ($VisualSpec) {
    $specification = Get-Item -LiteralPath $VisualSpec
    if ($specification.PSIsContainer) { throw "Expected a visual verification JSON file: $VisualSpec" }
    $VisualSpec = $specification.FullName
    $visualCases = ([IO.File]::ReadAllText($VisualSpec) | ConvertFrom-Json).cases
    $visualWithGraphics = @($visualCases | Where-Object { $_ -and $_.PSObject.Properties['scene'] -and $_.scene }).Count -gt 0
}
if ($AdditionalBuildScenes.Count -and (!$BuildScene -or $PlayerTarget -eq 'WeChatMiniGame')) {
    throw '-AdditionalBuildScenes requires -BuildScene and a Unity Player target.'
}
if ($ReleasePlayer -and (!$BuildScene -or $PlayerTarget -eq 'WeChatMiniGame')) {
    throw '-ReleasePlayer requires -BuildScene and a Unity Player target.'
}
$buildScenes = @($BuildScene) + $AdditionalBuildScenes
foreach ($scene in $buildScenes) {
    if ($scene -and ($scene -notmatch '^Assets/[A-Za-z0-9 /_.-]+\.unity$' -or $scene -match '(^|/)\.\.(/|$)' -or
        -not (Test-Path -LiteralPath (Join-Path $project $scene) -PathType Leaf))) { throw "Expected a scene under Assets: $scene" }
}
if ($WeChatSdkPath -and $PlayerTarget -ne 'WeChatMiniGame') { throw '-WeChatSdkPath applies to WeChatMiniGame.' }
if ($PlayerTarget -eq 'WeChatMiniGame' -and !$BuildScene) { throw 'WeChatMiniGame requires -BuildScene.' }

$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$evidence = Join-Path $repository "artifacts/game-unity/$Name/$runId"
$isolatedGame = $evidence
$isolatedClientDirectory = "$Name-Unity-Verification"
$isolated = Join-Path $isolatedGame ('client/' + $isolatedClientDirectory)
Write-Host "Unity verification evidence: $evidence"
$identityPath = Join-Path $evidence 'verification.json'
$identity = [ordered]@{
    schemaVersion = 1
    kind = 'unity-verification'
    game = $Name
    runId = $runId
    sourceProject = $project
    verificationProject = $isolated
    clientDirectory = $isolatedClientDirectory
    displayName = "$Name - Verification"
    retained = [bool]$KeepProject
    cacheRemnant = $false
}

$portableBuildManifest = Join-Path $evidence 'portable-build.json'

function Initialize-VerificationProbe {
$probe = Join-Path $isolated 'Assets/AwithGameWorkspaceProbe/Editor'
[IO.Directory]::CreateDirectory($probe) | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Probe.cs'), @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class AwithGameWorkspaceProbe
{
    public static void Verify()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects()) Check(root, path);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) Check(prefab, path);
        }
        File.WriteAllText(Read("-awith-game-import-marker"), Application.unityVersion);
    }

    public static void Build()
    {
        var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), Read("-awith-game-build-target"));
        if (target == BuildTarget.WebGL)
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Read("-awith-game-scene").Split('|'), locationPathName = Read("-awith-game-player"),
            target = target, options = Array.IndexOf(Environment.GetCommandLineArgs(), "-awith-game-release") >= 0
                ? BuildOptions.None : BuildOptions.Development
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException(target + " player build: " + report.summary.result);
    }

    private static string Read(string key)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, key);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static void Check(GameObject root, string path)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) == 0) continue;
            string hierarchy = node.name;
            for (var parent = node.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
            throw new InvalidOperationException("Missing Script in " + path + " at " + hierarchy);
        }
    }
}
'@)
if ($PlayerTarget -eq 'WeChatMiniGame') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityFixtures/WeChatMiniGameBuild.cs') -Destination $probe
}

}

function Invoke-Unity {
    param([string]$Step, [string[]]$Extra, [switch]$WithGraphics)
    $log = Join-Path $evidence ($Step + '.log')
    $arguments = @('-batchmode', '-projectPath', $isolated, '-logFile', $log) + $Extra
    if (-not $WithGraphics) { $arguments += '-nographics' }
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $processOptions = @{ FilePath = $editor.FullName; ArgumentList = $quoted; PassThru = $true }
    $restoreProfile = $false
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $processOptions.WindowStyle = 'Hidden'
        if (-not $env:ALLUSERSPROFILE) {
            $env:ALLUSERSPROFILE = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
            $restoreProfile = $true
        }
    }
    try { $process = Start-Process @processOptions }
    finally { if ($restoreProfile) { $env:ALLUSERSPROFILE = $null } }
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit(10000) | Out-Null
        throw "Unity $Step exceeded $TimeoutSeconds seconds. Inspect $log"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $log) {
            Select-String -LiteralPath $log -Pattern 'Missing Script|error CS|Exception|Build failed|Aborting' | Select-Object -Last 14 | ForEach-Object { Write-Host $_.Line }
        }
        throw "Unity $Step failed with exit $($process.ExitCode). Inspect $log"
    }
}

$testReports = [ordered]@{}
$summary = [ordered]@{
    game = $Name; unity = $null; recordedUnity = $expectedVersion; evidence = $evidence
    mode = 'headless'; status = 'incomplete'; failedStep = $null; error = $null
    importVerified = $false; importLog = (Join-Path $evidence 'import.log'); portableBuildManifest = $portableBuildManifest
    testsRequested = [bool]$RunTests; testsRun = $false; testMode = $TestMode; testFilter = $TestFilter; tests = $testReports
    buildScene = $BuildScene; buildScenes = @($buildScenes | Where-Object { $_ }); playerTarget = $(if ($BuildScene) { $PlayerTarget })
    buildVerified = $false; releasePlayer = [bool]$ReleasePlayer
    buildLog = $null; player = $null
    verificationProject = $isolated; verificationProjectRetained = [bool]$KeepProject; verificationIdentity = $identityPath
    runtimeVerified = $false; manualReview = 'not-run'
    visual = @{ status = 'not-run' }
    resources = @{ status = 'not-run' }
    atlases = @{ status = 'not-run' }
}
$step = 'stage'
try {
    [IO.Directory]::CreateDirectory($isolated) | Out-Null
    Copy-Item -LiteralPath $gameFile -Destination $isolatedGame
    foreach ($entry in @('Assets', 'Packages', 'ProjectSettings')) {
        $source = Join-Path $project $entry
        $linked = Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
        if ($linked) { throw "Linked project entry is not supported: $($linked.FullName)" }
        [IO.Directory]::CreateDirectory((Join-Path $isolated $entry)) | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $source -Force) {
            if ($entry -eq 'Packages' -and $item.Name -eq 'com.awithgameframe.workspace') { continue }
            $destination = Join-Path $isolated $entry
            if ([IO.Path]::DirectorySeparatorChar -eq '\' -and $item.PSIsContainer) {
                # Windows PowerShell's recursive Copy-Item fails on long Unity asset paths.
                $copyLog = & robocopy $item.FullName (Join-Path $destination $item.Name) /E /XJ /R:0 /W:0 /NJH /NJS /NFL /NDL /NP
                if ($LASTEXITCODE -ge 8) { throw "Could not copy $($item.FullName): $($copyLog -join [Environment]::NewLine)" }
            }
            else {
                Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse
            }
        }
    }
    $settingsPath = Join-Path $isolated 'ProjectSettings/ProjectSettings.asset'
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        $settings = [IO.File]::ReadAllText($settingsPath)
        $settings = [Text.RegularExpressions.Regex]::Replace($settings, '(?m)^(\s*productName:\s*).*$', ('$1' + $Name + ' - Verification'))
        [IO.File]::WriteAllText($settingsPath, $settings)
    }
    $manifest = Join-Path $isolated 'Packages/manifest.json'
    $packages = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
    foreach ($dependency in $packages.dependencies.PSObject.Properties) {
        if ($dependency.Value -notlike 'file:*') { continue }
        $relative = $dependency.Value.Substring(5)
        if ([IO.Path]::IsPathRooted($relative)) { continue }
        if (Test-Path -LiteralPath (Join-Path (Join-Path $isolated 'Packages') $relative)) { continue }
        $resolvedPackage = [IO.Path]::GetFullPath((Join-Path (Join-Path $project 'Packages') $relative))
        $dependency.Value = 'file:' + $resolvedPackage.Replace('\', '/')
    }
    [IO.File]::WriteAllText($manifest, ($packages | ConvertTo-Json -Depth 20))
    if ($WeChatSdkPath) {
        $sdk = Get-Item -LiteralPath $WeChatSdkPath
        $sdkDefinition = Get-Content -LiteralPath (Join-Path $sdk.FullName 'package.json') -Raw | ConvertFrom-Json
        if ($sdkDefinition.name -ne 'com.qq.weixin.minigame') { throw 'Expected the WeChat Mini Game SDK package directory.' }
        $embeddedSdk = Join-Path $isolated 'Packages/com.qq.weixin.minigame'
        if (Test-Path -LiteralPath $embeddedSdk) { throw 'The verification project already has an embedded WeChat SDK.' }
        Copy-Item -LiteralPath $sdk.FullName -Destination $embeddedSdk -Recurse
    }
    Initialize-VerificationProbe
    $step = 'sync'
    & (Join-Path $PSScriptRoot 'game.ps1') sync -Name $Name -ClientGameRoot $isolatedGame -ClientDirectory $isolatedClientDirectory
    Copy-Item -LiteralPath (Join-Path $isolated 'Packages/com.awithgameframe.workspace/build.json') -Destination $portableBuildManifest
    $step = 'import'
    $marker = Join-Path $evidence 'import.ok'
    Invoke-Unity 'import' @('-quit', '-executeMethod', 'AwithGameWorkspaceProbe.Verify', '-awith-game-import-marker', $marker)
    if (-not (Test-Path -LiteralPath $marker)) { throw "Unity did not complete its import checks. Inspect $evidence/import.log" }
    $actualVersion = [IO.File]::ReadAllText($marker)
    $summary.unity = $actualVersion
    $summary.importVerified = $true
    if ($actualVersion -ne $expectedVersion) { Write-Warning "Workspace records $expectedVersion and used $actualVersion." }
    if ($RunTests) {
        $summary.testsRun = $true
        $modes = if ($TestMode -eq 'All') { @('EditMode', 'PlayMode') } else { @($TestMode) }
        foreach ($mode in $modes) {
            $step = $mode
            $results = Join-Path $evidence ($mode + '.xml')
            $testArguments = @('-runTests', '-testPlatform', $mode, '-testResults', $results)
            if ($TestFilter) { $testArguments += @('-testFilter', $TestFilter) }
            $testReport = @{ status = 'incomplete'; log = (Join-Path $evidence ($mode + '.log')); results = $results }
            $testReports[$mode] = $testReport
            try { Invoke-Unity $mode $testArguments }
            finally {
                if (Test-Path -LiteralPath $results) {
                    $testRun = ([xml][IO.File]::ReadAllText($results)).'test-run'
                    if ($testRun) {
                        $testReport.passed = [int]$testRun.passed
                        $testReport.total = [int]$testRun.total
                        $testReport.status = 'failed'
                        if ($testReport.total -gt 0 -and $testRun.result -eq 'Passed') { $testReport.status = 'passed' }
                    }
                }
            }
            if ($testReport.status -ne 'passed') { throw "$mode tests did not pass. Inspect $results and $($testReport.log)." }
        }
    }
    if ($ResourceSpec) {
        $step = 'resources'
        $resourceReport = Join-Path $evidence 'resource-report.json'
        $summary.resources = @{ status = 'failed'; report = $resourceReport; log = (Join-Path $evidence 'resources.log') }
        Invoke-Unity 'resources' @('-quit', '-executeMethod', 'AwithGameFrame.Unity.Editor.UnityResourceAudit.RunBatch',
            '-awith-resource-spec', $ResourceSpec, '-awith-resource-output', $resourceReport)
        if (-not (Test-Path -LiteralPath $resourceReport -PathType Leaf) -or
            ([IO.File]::ReadAllText($resourceReport) | ConvertFrom-Json).passed -ne $true) {
            throw "Unity resource audit failed. Inspect $resourceReport"
        }
        $summary.resources.status = 'passed'
    }
    if ($AtlasSpec) {
        $step = 'atlases'
        $atlasReport = Join-Path $evidence 'atlas-report.json'
        $summary.atlases = @{ status = 'failed'; report = $atlasReport; log = (Join-Path $evidence 'atlases.log') }
        Invoke-Unity 'atlases' @('-quit', '-executeMethod', 'AwithGameFrame.Unity.Editor.Content.UnitySpriteAtlasAudit.RunBatch',
            '-awith-atlas-spec', $AtlasSpec, '-awith-atlas-output', $atlasReport)
        if (-not (Test-Path -LiteralPath $atlasReport -PathType Leaf) -or
            ([IO.File]::ReadAllText($atlasReport) | ConvertFrom-Json).passed -ne $true) {
            throw "Sprite Atlas audit failed. Inspect $atlasReport"
        }
        $summary.atlases.status = 'passed'
    }
    $buildLog = $null
    if ($BuildScene) {
        $step = 'Player-build'
        $isWebGL = $PlayerTarget -eq 'WebGL'
        $isWeChat = $PlayerTarget -eq 'WeChatMiniGame'
        $nativeTarget = switch ($PlayerTarget) {
            'Android' { 'Android' }
            'WebGL' { 'WebGL' }
            default { 'StandaloneWindows64' }
        }
        $editorTarget = switch ($PlayerTarget) {
            'Android' { 'Android' }
            'WebGL' { 'WebGL' }
            'WeChatMiniGame' { 'WebGL' }
            default { 'Win64' }
        }
        $buildOutput = switch ($PlayerTarget) {
            'Android' { Join-Path $evidence "Player/$Name.apk" }
            'WebGL' { Join-Path $evidence 'Player/WebGL' }
            'WeChatMiniGame' { Join-Path $evidence 'Player/WeChatMiniGame' }
            default { Join-Path $evidence "Player/$Name.exe" }
        }
        $buildLog = Join-Path $evidence 'Player-build.log'
        $summary.buildLog = $buildLog
        $buildMethod = if ($isWeChat) { 'AwithWeChatMiniGameBuild.Run' } else { 'AwithGameWorkspaceProbe.Build' }
        $buildArguments = @('-quit', '-buildTarget', $editorTarget, '-executeMethod', $buildMethod,
            '-awith-game-build-target', $nativeTarget,
            '-awith-game-scene', ($buildScenes -join '|'), '-awith-game-player', $buildOutput)
        if ($ReleasePlayer) { $buildArguments += '-awith-game-release' }
        Invoke-Unity 'Player-build' $buildArguments
        $player = if ($isWeChat) { Join-Path $buildOutput 'minigame' } else { $buildOutput }
        $entryPoint = if ($isWebGL) { Join-Path $player 'index.html' } else { $player }
        if ($isWeChat) { $entryPoint = Join-Path $player 'game.js' }
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Unity did not produce the $PlayerTarget Player entry point. Inspect $buildLog"
        }
        Write-Host "$PlayerTarget Player built: $player"
        $summary.buildVerified = $true
        $summary.player = $player
    }
    if ($VisualSpec) {
        $step = 'visual'
        $visualOutput = Join-Path $evidence 'Visual'
        $visualReport = Join-Path $visualOutput 'visual-report.json'
        $visualMode = if ($visualWithGraphics) { 'offscreen' } else { 'image-only' }
        $summary.visual = @{ status = 'failed'; mode = $visualMode; report = $visualReport; log = (Join-Path $evidence 'visual.log') }
        Invoke-Unity -Step 'visual' -WithGraphics:$visualWithGraphics -Extra @('-quit', '-executeMethod',
            'AwithGameFrame.Unity.Editor.UnityVisualVerification.RunBatch', '-awith-visual-spec', $VisualSpec, '-awith-visual-output', $visualOutput)
        if (-not (Test-Path -LiteralPath $visualReport -PathType Leaf) -or
            ([IO.File]::ReadAllText($visualReport) | ConvertFrom-Json).passed -ne $true) {
            throw "Unity visual verification did not pass. Inspect $visualReport"
        }
        $summary.visual.status = 'passed'
        Write-Host "Unity visual verification passed. Report: $visualReport"
    }
    $summary.status = 'passed'
}
catch {
    $summary.status = 'failed'
    $summary.failedStep = $step
    $summary.error = $_.Exception.Message
    throw
}
finally {
    $projectRemoved = $true
    if (-not $KeepProject) { $projectRemoved = Remove-UnityVerificationDirectory $isolated $evidence }
    $identity.retained = [bool]$KeepProject -or -not $projectRemoved
    $identity.cacheRemnant = Test-Path -LiteralPath $isolated
    $identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $identityPath -Encoding UTF8
    $summary.verificationProjectRetained = $identity.retained
    $summary.verificationCacheRemnant = $identity.cacheRemnant
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding UTF8
    if ($KeepProject) { Write-Host "Retained Unity verification project: $isolated" }
    elseif ($projectRemoved) { Write-Host "Removed disposable Unity verification project. Use -KeepProject to retain it." }
}
