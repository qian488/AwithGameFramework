[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')][string]$Name,
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [string]$BuildScene,
    [switch]$RunTests,
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 600,
    [string]$VisualSpec,
    [switch]$KeepProject
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
if ($BuildScene -and ($BuildScene -notmatch '^Assets/[A-Za-z0-9 /_.-]+\.unity$' -or $BuildScene -match '(^|/)\.\.(/|$)' -or
    -not (Test-Path -LiteralPath (Join-Path $project $BuildScene) -PathType Leaf))) { throw "Expected a scene under Assets: $BuildScene" }

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
& (Join-Path $PSScriptRoot 'workspace-client.ps1') -RepositoryRoot $repository -GameRoot $isolatedGame -ClientDirectory $isolatedClientDirectory

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
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { Read("-awith-game-scene") }, locationPathName = Read("-awith-game-player"),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Windows player build: " + report.summary.result);
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
        $player = Join-Path $evidence "Player/$Name.exe"
        $buildLog = Join-Path $evidence 'Player-build.log'
        Invoke-Unity 'Player-build' @('-quit', '-buildTarget', 'Win64', '-executeMethod', 'AwithGameWorkspaceProbe.Build',
            '-awith-game-scene', $BuildScene, '-awith-game-player', $player)
        if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw "Unity did not produce the Player. Inspect $buildLog" }
        Write-Host "Windows Player built: $player"
    }
    $summary = [ordered]@{
        game = $Name; unity = $actualVersion; recordedUnity = $expectedVersion; evidence = $evidence
        mode = 'headless'; importVerified = $true; importLog = (Join-Path $evidence 'import.log')
        testsRun = [bool]$RunTests; tests = $testReports
        buildScene = $BuildScene; buildVerified = [bool]$BuildScene; buildLog = $buildLog; player = $player
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
