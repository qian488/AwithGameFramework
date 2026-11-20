[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PlayerPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Rain Pass Player smoke requires Windows.' }
$player = Get-Item -LiteralPath $PlayerPath
if ($player.PSIsContainer -or $player.Extension -ne '.exe') { throw "Expected a Windows Player executable: $PlayerPath" }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = Join-Path $repository ('artifacts/game-unity/Test/PlayerSmoke-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$log = Join-Path $evidence 'Player-runtime.log'
$reportPath = Join-Path $evidence 'result.json'
Write-Host "Rain Pass headless Player evidence: $evidence"
$options = @{
    FilePath = $player.FullName
    WorkingDirectory = $player.DirectoryName
    ArgumentList = @('-batchmode', '-nographics', '-rain-pass-evidence', ('"' + $evidence + '"'), '-logFile', ('"' + $log + '"'))
    PassThru = $true
    WindowStyle = 'Hidden'
}
$process = Start-Process @options
try {
    if (-not $process.WaitForExit(130000)) {
        $process.Kill()
        throw "Rain Pass headless Player timed out. See $log"
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Rain Pass headless Player failed with exit $($process.ExitCode). See $log"
    }
    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($report.passed -ne $true -or $report.result -ne 'Victory' -or $report.graphicsEvidence -ne $false) {
        throw "Rain Pass headless assertions did not pass. See $reportPath"
    }
    Write-Host "Rain Pass headless Player passed. Report: $reportPath"
}
finally { $process.Dispose() }
