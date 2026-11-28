[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,39}$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{0,47}$')][string]$Prototype,
    [Parameter(Mandatory)][string]$EditorPath,
    [string]$VerificationSummary,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'unity-verification-cleanup.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$gameRoot = Join-Path $repository "games/$Name"
$unity = Join-Path $gameRoot 'client/Unity'
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$design = Join-Path $gameRoot "ui-prototypes/$Prototype.json"
$profilePath = Join-Path $gameRoot "ui-prototypes/$Prototype.profile.json"
$profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($profile.output -cnotmatch '^Assets/(?:[A-Za-z0-9_-]+/)+[A-Za-z0-9_-]+\.prefab$') {
    throw 'A prototype output must be a prefab within Assets.'
}
$destination = Join-Path $unity $profile.output
Assert-UnityVerificationChild $destination $unity
Assert-UnityVerificationChild ($destination + '.meta') $unity
$designHash = (Get-FileHash -LiteralPath $design).Hash
$profileHash = (Get-FileHash -LiteralPath $profilePath).Hash
$existingHash = if (Test-Path -LiteralPath $destination) { (Get-FileHash -LiteralPath $destination).Hash } else { '' }
$existingMeta = if (Test-Path -LiteralPath ($destination + '.meta')) { (Get-FileHash -LiteralPath ($destination + '.meta')).Hash } else { '' }
$ownsProject = !$VerificationSummary
$evidenceRoot = Join-Path $repository "artifacts/game-unity/$Name"
$runPrefix = 'Unity verification evidence: '
if ($ownsProject) {
    & (Join-Path $PSScriptRoot 'verify-game-unity.ps1') -Name $Name -EditorPath $editor -KeepProject 6>&1 |
        ForEach-Object {
            if ("$_".StartsWith($runPrefix)) {
                $VerificationSummary = Join-Path "$_".Substring($runPrefix.Length) 'summary.json'
            }
            Write-Host $_
        }
}
if (!$VerificationSummary) { throw 'The background Unity import did not produce a summary.' }
$summaryFile = (Get-Item -LiteralPath $VerificationSummary).FullName
$summary = Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json
$base = Split-Path -Parent $summaryFile
Assert-UnityVerificationChild $base $evidenceRoot
$project = Join-Path $base "client/$Name-Unity-Verification"
if ($summary.game -cne $Name -or !$summary.importVerified -or $summary.verificationProject -ne $project -or
    !$summary.verificationProjectRetained -or !(Test-Path -LiteralPath $project -PathType Container)) {
    throw 'Use a retained project from verify-game-unity.ps1 -KeepProject for the selected game.'
}
$stage = Join-Path $base ('Prototype-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$stagedDesign = Join-Path $stage 'design.json'
$stagedProfile = Join-Path $stage 'profile.json'
$reportPath = Join-Path $stage 'report.json'
$log = Join-Path $stage 'unity.log'
try {
    Copy-Item -LiteralPath $design -Destination $stagedDesign
    Copy-Item -LiteralPath $profilePath -Destination $stagedProfile
    $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $project, '-executeMethod',
        'AwithGameFrame.Tools.UiPrototype.UnityPrototypeImporter.RunBatch', '-awithPrototypeDesign', $stagedDesign,
        '-awithPrototypeProfile', $stagedProfile, '-awithPrototypeReport', $reportPath, '-logFile', $log)
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $editor -ArgumentList $quoted -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit(240000)) { $process.Kill(); throw "Unity prototype timed out. See $log" }
        $process.Refresh()
        if (!(Test-Path -LiteralPath $reportPath)) { throw "Unity did not produce a prototype report. See $log" }
        $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Output "Prototype report: $reportPath"
        Get-Content -LiteralPath $reportPath -Encoding UTF8
        if ($process.ExitCode -ne 0 -or $report.problems.Count -ne 0) { throw "Prototype did not pass preflight. See $reportPath" }
    }
    finally { $process.Dispose() }

    if (!$Apply) { return }
    $currentHash = if (Test-Path -LiteralPath $destination) { (Get-FileHash -LiteralPath $destination).Hash } else { '' }
    $currentMeta = if (Test-Path -LiteralPath ($destination + '.meta')) { (Get-FileHash -LiteralPath ($destination + '.meta')).Hash } else { '' }
    if ((Get-FileHash -LiteralPath $design).Hash -ne $designHash -or (Get-FileHash -LiteralPath $profilePath).Hash -ne $profileHash -or
        (Get-FileHash -LiteralPath $stagedDesign).Hash -ne $designHash -or (Get-FileHash -LiteralPath $stagedProfile).Hash -ne $profileHash -or
        $currentHash -ne $existingHash -or $currentMeta -ne $existingMeta) {
        throw 'The source or prefab was edited during import. Rerun preflight with the current files.'
    }
    $candidate = Join-Path $project $profile.output
    $meta = Get-Content -LiteralPath ($candidate + '.meta') -Raw
    if ($existingMeta -and $meta -cne (Get-Content -LiteralPath ($destination + '.meta') -Raw)) {
        throw 'Generated prefab metadata differed from the tracked prefab metadata.'
    }
    if ((Get-FileHash -LiteralPath $candidate).Hash -ne $existingHash) {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        $temporary = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        $backup = $temporary + '.backup.tmp'
        try {
            Copy-Item -LiteralPath $candidate -Destination $temporary
            if (!$existingMeta) { Copy-Item -LiteralPath ($candidate + '.meta') -Destination ($destination + '.meta') }
            if ($existingHash) { [IO.File]::Replace($temporary, $destination, $backup) }
            else { [IO.File]::Move($temporary, $destination) }
        }
        finally {
            [IO.File]::Delete($temporary)
            [IO.File]::Delete($backup)
        }
    }
    Write-Output "Imported $($profile.output) ($($report.guid))."
}
finally {
    if ($ownsProject) {
        $removed = Remove-UnityVerificationDirectory $project $base
        $summary.verificationProjectRetained = !$removed
        $summary.verificationCacheRemnant = !$removed -and (Test-Path -LiteralPath (Join-Path $project 'Library'))
        $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryFile -Encoding UTF8
        if (!$removed) { throw "Could not remove the temporary Unity project: $project" }
    }
}
