[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$SpecificationPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('Win64', 'Android', 'WebGL')][string]$BuildTarget = 'Win64'
)
$ErrorActionPreference = 'Stop'
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$project = (Get-Item -LiteralPath $ProjectPath).FullName
$specification = (Get-Item -LiteralPath $SpecificationPath).FullName
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory for each content build.' }
$logDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) 'artifacts/ContentBuild'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$log = Join-Path $logDirectory ('Unity-' + [Guid]::NewGuid().ToString('N') + '.log')
$options = @{
    FilePath = $editor
    ArgumentList = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-buildTarget', $BuildTarget,
        '-executeMethod', 'AwithGameFrame.Unity.Editor.UnityContentBuilder.Run', '-awith-content-spec', ('"' + $specification + '"'),
        '-awith-content-output', ('"' + $output + '"'), '-logFile', ('"' + $log + '"'))
    PassThru = $true
}
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $options.WindowStyle = 'Hidden' }
$process = Start-Process @options
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw "Unity content build timed out. See $log" }
$process.Refresh()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $output 'bundle-catalog.json')) -or
    -not (Select-String -LiteralPath $log -SimpleMatch 'UNITY_CONTENT_BUILT' -Quiet) -or
    (Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet)) { throw "Unity content build failed. See $log" }
Write-Host "Unity bundles and catalog: $output; log: $log"
