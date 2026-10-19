[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EditorPath,
    [switch]$RunSmoke,
    [switch]$BuildPlayer,
    [ValidateSet('Mono', 'IL2CPP')][string]$ScriptingBackend = 'Mono',
    [ValidateSet('Windows64', 'Android', 'WebGL')][string]$PlayerTarget = 'Windows64'
)

$ErrorActionPreference = 'Stop'
if ($RunSmoke -and $PlayerTarget -ne 'Windows64') {
    throw 'PlayMode assertions load Windows bundles. Run -RunSmoke with Windows64 separately from Android/WebGL builds.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Get-Item -LiteralPath $EditorPath
$validationRoot = Join-Path $repositoryRoot ('artifacts/UnityValidation/' + [Guid]::NewGuid().ToString('N'))
$sampleRoot = Join-Path $repositoryRoot 'samples/Unity'
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null

# Import a disposable copy, leaving the sample's recorded editor version intact.
foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $sampleRoot $directory) -Destination $validationRoot -Recurse
}
$manifestPath = Join-Path $validationRoot 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
    if (-not $dependency.Value.StartsWith('file:')) { continue }
    $target = [IO.Path]::GetFullPath((Join-Path (Join-Path $sampleRoot 'Packages') $dependency.Value.Substring(5)))
    $dependency.Value = 'file:' + $target.Replace([char]92, [char]47)
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$editorVersion = $editor.VersionInfo.ProductVersion.Split('_')[0]
if ($editorVersion -match '^\d{4}\.\d+\.\d+[abfp]\d+') {
    "m_EditorVersion: $editorVersion" | Set-Content -LiteralPath (Join-Path $validationRoot 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8
}
$logPath = Join-Path $validationRoot 'Editor.log'
$processOptions = @{
    FilePath = $editor.FullName
    ArgumentList = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $validationRoot + '"'), '-logFile', ('"' + $logPath + '"'))
    PassThru = $true
}
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $processOptions.WindowStyle = 'Hidden' }
Write-Host "Validating with Unity $editorVersion; log: $logPath"
$unityProcess = Start-Process @processOptions
if (-not $unityProcess.WaitForExit(300000)) { $unityProcess.Kill(); throw "Unity import timed out. See $logPath" }
$unityProcess.Refresh()
if ($unityProcess.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 60 }
    throw "Unity validation failed (exit $($unityProcess.ExitCode)). See $logPath"
}
if (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet) {
    throw "Unity reported compiler errors. See $logPath"
}
foreach ($assembly in @('Core', 'Data', 'Gameplay', 'Networking', 'Unity', 'Unity.Editor', 'Foundation', 'Extensions', 'Samples', 'Test')) {
    $assemblyPath = Join-Path $validationRoot "Library/ScriptAssemblies/AwithGameFrame.$assembly.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Unity did not compile AwithGameFrame.$assembly. See $logPath"
    }
}
Write-Host "Unity import and compilation passed. Log: $logPath"

function Invoke-UnityStep {
    param([string]$Name, [string]$Method, [string[]]$ExtraArguments, [switch]$Quit)
    $stepLog = Join-Path $validationRoot ($Name + '.log')
    $stepArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $validationRoot + '"'), '-logFile', ('"' + $stepLog + '"'), '-executeMethod', $Method)
    if ($Quit) { $stepArguments += '-quit' }
    if ($ExtraArguments) { $stepArguments += $ExtraArguments }
    $stepOptions = @{ FilePath = $editor.FullName; ArgumentList = $stepArguments; PassThru = $true }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $stepOptions.WindowStyle = 'Hidden' }
    $stepProcess = Start-Process @stepOptions
    if (-not $stepProcess.WaitForExit(300000)) { $stepProcess.Kill(); throw "Unity $Name timed out. See $stepLog" }
    $stepProcess.Refresh()
    if ($stepProcess.ExitCode -ne 0 -or (Select-String -LiteralPath $stepLog -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet)) {
        throw "Unity $Name failed ($($stepProcess.ExitCode)). See $stepLog"
    }
    return $stepLog
}

$contentArguments = @()
if ($RunSmoke -or ($BuildPlayer -and $PlayerTarget -eq 'Windows64')) {
    $null = Invoke-UnityStep -Name 'Content-prepare' -Method 'AwithGameFrame.Samples.Editor.ContentSmokeAssets.Prepare' -Quit
    $contentOutput = Join-Path $validationRoot 'Content'
    & (Join-Path $PSScriptRoot 'build-unity-content.ps1') -EditorPath $editor.FullName -ProjectPath $validationRoot -SpecificationPath (Join-Path $validationRoot 'content-build.json') -OutputDirectory $contentOutput -BuildTarget 'Win64'
    $contentInstall = Join-Path $validationRoot 'ContentInstall'
    & (Join-Path $PSScriptRoot 'verify-content.ps1') -SourceDirectory $contentOutput -Version 'unity-smoke-v1' -OutputDirectory $contentInstall
    $installation = Get-Content -LiteralPath (Join-Path $contentInstall 'Install-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $contentCatalog = Join-Path $installation.directory 'bundle-catalog.json'
    $contentArguments = @('-awith-content-catalog', ('"' + $contentCatalog + '"'))
}

if ($RunSmoke) {
    $adapterLog = Invoke-UnityStep -Name 'Adapter-smoke' -Method 'AwithGameFrame.Samples.Editor.UnityAdapterSmoke.Run'
    if (-not (Select-String -LiteralPath $adapterLog -SimpleMatch 'Unity adapter smoke passed:' -Quiet)) { throw "Unity adapter assertions missing. See $adapterLog" }
    $sharedLog = Invoke-UnityStep -Name 'Shared-host-smoke' -Method 'AwithGameFrame.Samples.Editor.SharedHostSmoke.Run' -ExtraArguments (@('-awith-shared-smoke') + $contentArguments)
    if (-not (Select-String -LiteralPath $sharedLog -SimpleMatch 'UNITY_SHARED_HOST_PASSED' -Quiet)) { throw "Unity shared host assertions missing. See $sharedLog" }
    foreach ($marker in @('FOUNDATION_CORE_PASSED', 'UNITY_PRESENTATION_SCENES_PASSED', 'UNITY_INPUT_AUDIO_PASSED', 'COMPOSED_APPLICATION_PASSED', 'UNITY_ASSET_BUNDLE_PASSED')) {
        if (-not (Select-String -LiteralPath $sharedLog -SimpleMatch $marker -Quiet)) { throw "Unity marker $marker missing. See $sharedLog" }
    }
    Write-Host "Unity adapter and shared host PlayMode assertions passed. Logs: $validationRoot"
}

if ($BuildPlayer) {
    $nativeTarget = @{ Windows64 = 'StandaloneWindows64'; Android = 'Android'; WebGL = 'WebGL' }[$PlayerTarget]
    $editorTarget = @{ Windows64 = 'Win64'; Android = 'Android'; WebGL = 'WebGL' }[$PlayerTarget]
    $nativeBackend = if ($ScriptingBackend -eq 'IL2CPP') { 'IL2CPP' } else { 'Mono2x' }
    if ($PlayerTarget -eq 'WebGL' -and $ScriptingBackend -ne 'IL2CPP') { throw 'WebGL requires -ScriptingBackend IL2CPP.' }
    $playerRelative = @{ Windows64 = 'Player/AwithSharedDemo.exe'; Android = 'Player/AwithSharedDemo.apk'; WebGL = 'Player/WebGL' }[$PlayerTarget]
    $playerPath = Join-Path $validationRoot $playerRelative
    $buildLog = Invoke-UnityStep -Name 'Player-build' -Method 'AwithGameFrame.Samples.Editor.SharedHostSmoke.BuildPlayer' -ExtraArguments @('-buildTarget', $editorTarget, '-awith-player-output', ('"' + $playerPath + '"'), '-awith-build-target', $nativeTarget, '-awith-scripting-backend', $nativeBackend) -Quit
    if (-not (Test-Path -LiteralPath $playerPath)) { throw "Unity did not produce the Player. See $buildLog" }
    if ($PlayerTarget -eq 'Windows64') {
    $playerLog = Join-Path $validationRoot 'Player-runtime.log'
    $playerOptions = @{ FilePath = $playerPath; ArgumentList = (@('-batchmode', '-nographics', '-awith-shared-smoke', '-logFile', ('"' + $playerLog + '"')) + $contentArguments); PassThru = $true }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $playerOptions.WindowStyle = 'Hidden' }
    $playerProcess = Start-Process @playerOptions
    if (-not $playerProcess.WaitForExit(120000)) { $playerProcess.Kill(); throw "Unity Player timed out. See $playerLog" }
    $playerProcess.Refresh()
    if ($playerProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $playerLog -SimpleMatch 'UNITY_SHARED_HOST_PASSED' -Quiet)) {
        throw "Unity Player assertions failed. See $playerLog"
    }
    foreach ($marker in @('FOUNDATION_CORE_PASSED', 'UNITY_PRESENTATION_SCENES_PASSED', 'UNITY_INPUT_AUDIO_PASSED', 'COMPOSED_APPLICATION_PASSED', 'UNITY_ASSET_BUNDLE_PASSED')) {
        if (-not (Select-String -LiteralPath $playerLog -SimpleMatch $marker -Quiet)) { throw "Unity Player marker $marker missing. See $playerLog" }
    }
    Write-Host "Unity standalone Player build and runtime passed. Log: $playerLog"
    } else { Write-Host "Unity $PlayerTarget build passed; runtime/device validation was not performed. Log: $buildLog" }
    $playerDirectory = Join-Path $validationRoot 'Player'
    [ordered]@{
        engine = 'Unity'; version = $editorVersion; target = "$PlayerTarget $ScriptingBackend development";
        runtimeVerified = ($PlayerTarget -eq 'Windows64');
        commit = (& git -C $repositoryRoot rev-parse HEAD | Out-String).Trim();
        workingTreeModified = [bool](& git -C $repositoryRoot status --porcelain | Out-String).Trim();
        files = @(Get-ChildItem -LiteralPath $playerDirectory -Recurse -File | ForEach-Object {
            [ordered]@{ path = $_.FullName.Substring($playerDirectory.Length + 1); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $validationRoot 'Build-report.json')
}
