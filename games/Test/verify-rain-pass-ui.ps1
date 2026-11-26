[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VerificationSummary,
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$BrowserPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$summaryFile = Get-Item -LiteralPath $VerificationSummary
$summary = [IO.File]::ReadAllText($summaryFile.FullName) | ConvertFrom-Json
$evidence = $summaryFile.Directory.FullName
$evidenceRoot = Join-Path $repository 'artifacts/game-unity/Test'
$project = Join-Path $evidence 'client/Test-Unity-Verification'
if ($summary.game -cne 'Test' -or !$summary.importVerified -or !$summary.verificationProjectRetained -or
    $summary.verificationProject -ne $project -or
    !$evidence.StartsWith($evidenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use summary.json from verify-game-unity.ps1 -Name Test -KeepProject.'
}
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$browser = (Get-Item -LiteralPath $BrowserPath).FullName
$run = Join-Path $evidence ('Overlay-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($run) | Out-Null
$build = Join-Path $run 'WebGL'
$log = Join-Path $run 'build.log'
$arguments = @('-batchmode', '-nographics', '-buildTarget', 'WebGL', '-projectPath', $project,
    '-runTests', '-testPlatform', 'WebGL', '-testFilter', 'Test.Client.Tests.RainPassContractOverlayTests',
    '-build-rain-pass-ui-test-player', '-doNotReportTestResultsBackToEditor', '-buildPlayerPath', $build, '-logFile', $log)
$quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
Write-Host "Rain Pass Overlay evidence: $run"
$process = Start-Process -FilePath $editor -ArgumentList $quoted -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(900000)) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "WebGL build timed out. Inspect $log"
}
$process.Refresh()
$player = Join-Path $build 'PlayerWithTests/PlayerWithTests'
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $player 'index.html') -PathType Leaf)) {
    throw "WebGL test build failed. Inspect $log"
}

$output = Join-Path $run 'Browser'
$result = 'RAIN_PASS_UI_RESULT Passed 1 0'
& node (Join-Path $repository 'tools/verify-unity-webgl.mjs') --player $player --browser $browser --output $output --ready-log $result
$reportPath = Join-Path $output 'report.json'
if ($LASTEXITCODE -ne 0) { throw "WebGL Overlay test failed. Inspect $reportPath" }
$report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
$expected = @('01-camp', '02-loading', '03-cancelling', '04-paused', '05-defeated', '06-save-failed', '07-victory',
    '08-reopened', '09-download-offer', '10-downloading', '11-update-failure',
    '12-settings-en', '13-settings-zh', '14-settings-save-failure', '15-score-pending', '16-score-synced',
    '17-leaderboard-loading', '18-leaderboard-loaded', '19-leaderboard-error', '20-leaderboard-cached', '21-leaderboard-empty',
    '22-gamepad-paused', '23-replay-versions', '24-replay-playing', '25-replay-missing')
$frames = @{}
$completed = @()
foreach ($message in $report.messages) {
    $line = $message.text.Trim()
    if ($line -match '^RAIN_PASS_FRAME ([\w-]+) ([A-Za-z0-9+/=]+)$') {
        $state = $Matches[1]
        if ($state -notin $expected -or $state -in $completed) { throw "Unexpected screenshot state $state. Inspect $reportPath" }
        if (!$frames.ContainsKey($state)) { $frames[$state] = [Text.StringBuilder]::new() }
        [void]$frames[$state].Append($Matches[2])
    }
    elseif ($line -match '^RAIN_PASS_FRAME_END ([\w-]+)$') {
        $state = $Matches[1]
        if (!$frames.ContainsKey($state) -or $state -in $completed) { throw "Unexpected screenshot end $state. Inspect $reportPath" }
        $bytes = [Convert]::FromBase64String($frames[$state].ToString())
        if ($bytes.Length -lt 33 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A' -or
            [BitConverter]::ToString($bytes, $bytes.Length - 12, 12) -ne '00-00-00-00-49-45-4E-44-AE-42-60-82') {
            throw "Invalid screenshot $state. Inspect $reportPath"
        }
        [IO.File]::WriteAllBytes((Join-Path $output "$state.png"), $bytes)
        $completed += $state
    }
}
if (($completed -join ',') -cne ($expected -join ',')) { throw "Missing or out-of-order screenshots. Inspect $reportPath" }
Write-Host "Captured $($completed.Count) Overlay states: $output"
