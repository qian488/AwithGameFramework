[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')][string]$Name,
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [string]$BuildScene,
    [ValidateSet('Windows64', 'WebGL', 'WeChatMiniGame')][string]$PlayerTarget = 'Windows64',
    [string]$WeChatSdkPath,
    [switch]$RunTests,
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 600,
    [string]$VisualSpec,
    [switch]$KeepProject,
    [string[]]$AdditionalBuildScenes = @(),
    [switch]$ReleasePlayer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'verify-game-unity-metadata.ps1')
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
[IO.Directory]::CreateDirectory($isolated) | Out-Null
Write-Host "Unity verification evidence: $evidence"
Copy-Item -LiteralPath $gameFile -Destination $isolatedGame
foreach ($entry in @('Assets', 'Packages', 'ProjectSettings')) {
    $source = Join-Path $project $entry
    $linked = Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
    if ($linked) { throw "Linked project entry is not supported: $($linked.FullName)" }
    [IO.Directory]::CreateDirectory((Join-Path $isolated $entry)) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $source -Force) {
        if ($entry -eq 'Packages' -and $item.Name -eq 'com.awithgameframe.workspace') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $isolated $entry) -Recurse
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
$identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $identityPath -Encoding UTF8

& (Join-Path $PSScriptRoot 'game.ps1') build -Name $Name
if ($LASTEXITCODE -ne 0) { throw "Game build failed: $Name" }
& (Join-Path $PSScriptRoot 'workspace-client.ps1') -RepositoryRoot $repository -GameRoot $isolatedGame -ClientDirectory $isolatedClientDirectory -SourceGameRoot $root

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

function Remove-DisposableProject([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
    $evidencePath = [IO.Path]::GetFullPath($evidence).TrimEnd([char[]]@('\', '/'))
    if (-not $fullPath.StartsWith($evidencePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the verification evidence directory: $Path"
    }
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            [IO.Directory]::Delete($fullPath, $true)
            return $true
        }
        catch {
            if ($attempt -eq 4) {
                $projectMarker = Join-Path $Path 'ProjectSettings/ProjectVersion.txt'
                if (-not (Test-Path -LiteralPath $projectMarker -PathType Leaf)) {
                    Write-Warning "Unity cache remnants remain after cleanup, but the verification project marker was removed: $Path"
                    return $true
                }
                Write-Warning "Could not remove disposable Unity verification project after $attempt attempts: $Path. The project was retained for diagnosis."
                return $false
            }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
    return $false
}

$summary = $null
try {
    $marker = Join-Path $evidence 'import.ok'
    Invoke-Unity 'import' @('-quit', '-executeMethod', 'AwithGameWorkspaceProbe.Verify', '-awith-game-import-marker', $marker)
    if (-not (Test-Path -LiteralPath $marker)) { throw "Unity did not complete its import checks. Inspect $evidence/import.log" }
    $actualVersion = [IO.File]::ReadAllText($marker)
    if ($actualVersion -ne $expectedVersion) { Write-Warning "Workspace records $expectedVersion and used $actualVersion." }
    $testReports = [ordered]@{}
    if ($RunTests) {
        foreach ($mode in @('EditMode', 'PlayMode')) {
            $results = Join-Path $evidence ($mode + '.xml')
            Invoke-Unity $mode @('-runTests', '-testPlatform', $mode, '-testResults', $results)
            if (-not (Test-Path -LiteralPath $results)) { throw "Unity did not produce $results" }
            $testRun = ([xml][IO.File]::ReadAllText($results)).'test-run'
            if ([int]$testRun.total -eq 0 -or $testRun.result -ne 'Passed') { throw "$mode tests did not pass: $results" }
            $testReports[$mode] = @{ passed = [int]$testRun.passed; total = [int]$testRun.total; results = $results }
        }
    }
    $player = $null
    $buildLog = $null
    if ($BuildScene) {
        $isWebGL = $PlayerTarget -eq 'WebGL'
        $isWeChat = $PlayerTarget -eq 'WeChatMiniGame'
        $nativeTarget = if ($isWebGL) { 'WebGL' } else { 'StandaloneWindows64' }
        $editorTarget = if ($isWebGL -or $isWeChat) { 'WebGL' } else { 'Win64' }
        $buildOutput = switch ($PlayerTarget) {
            'WebGL' { Join-Path $evidence 'Player/WebGL' }
            'WeChatMiniGame' { Join-Path $evidence 'Player/WeChatMiniGame' }
            default { Join-Path $evidence "Player/$Name.exe" }
        }
        $buildLog = Join-Path $evidence 'Player-build.log'
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
    }
    $summary = [ordered]@{
        game = $Name; unity = $actualVersion; recordedUnity = $expectedVersion; evidence = $evidence
        mode = 'headless'; importVerified = $true; importLog = (Join-Path $evidence 'import.log')
        testsRun = [bool]$RunTests; tests = $testReports
        buildScene = $BuildScene; buildScenes = @($buildScenes | Where-Object { $_ }); playerTarget = $(if ($BuildScene) { $PlayerTarget })
        buildVerified = [bool]$BuildScene; releasePlayer = [bool]$ReleasePlayer
        buildLog = $buildLog; player = $player
        verificationProject = $isolated; verificationProjectRetained = [bool]$KeepProject; verificationIdentity = $identityPath
        runtimeVerified = $false; manualReview = 'not-run'
        visual = @{ status = 'not-run' }
    }
    if ($VisualSpec) {
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
}
finally {
    $projectRemoved = $true
    if (-not $KeepProject) { $projectRemoved = Remove-DisposableProject $isolated }
    $identity.retained = [bool]$KeepProject -or -not $projectRemoved
    $identity.cacheRemnant = Test-Path -LiteralPath $isolated
    $identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $identityPath -Encoding UTF8
    if ($summary -ne $null) {
        $summary.verificationProjectRetained = $identity.retained
        $summary.verificationCacheRemnant = $identity.cacheRemnant
        $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding UTF8
    }
    if ($KeepProject) { Write-Host "Retained Unity verification project: $isolated" }
    elseif ($projectRemoved) { Write-Host "Removed disposable Unity verification project. Use -KeepProject to retain it." }
}
