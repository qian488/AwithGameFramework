[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EditorPath
)

$ErrorActionPreference = 'Stop'
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
    $dependency.Value = 'file:' + $target.Replace('\', '/')
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
$unityProcess.WaitForExit()
$unityProcess.Refresh()
if ($unityProcess.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 60 }
    throw "Unity validation failed (exit $($unityProcess.ExitCode)). See $logPath"
}
if (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet) {
    throw "Unity reported compiler errors. See $logPath"
}
foreach ($assembly in @('Core', 'Data', 'Gameplay', 'Unity', 'Foundation', 'Extensions', 'Samples', 'Test')) {
    $assemblyPath = Join-Path $validationRoot "Library/ScriptAssemblies/AwithGameFrame.$assembly.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Unity did not compile AwithGameFrame.$assembly. See $logPath"
    }
}
Write-Host "Unity import and compilation passed. Log: $logPath"
