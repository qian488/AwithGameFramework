[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$VerificationSummary,
    [Parameter(Mandatory)][string]$EditorPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repository 'tools/unity-verification-cleanup.ps1')
$summaryFile = Get-Item -LiteralPath $VerificationSummary
$summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
$evidence = $summaryFile.Directory.FullName
$project = Join-Path $evidence 'client/Test-Unity-Verification'
Assert-UnityVerificationChild $evidence (Join-Path $repository 'artifacts/game-unity/Test')
if ($summary.game -cne 'Test' -or !$summary.importVerified -or !$summary.verificationProjectRetained -or
    $summary.verificationProject -ne $project) { throw 'Use a retained Test Unity verification project.' }
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$output = Join-Path $evidence ('TournamentUI-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($output) | Out-Null
Write-Host "Tournament UI evidence: $output"

function Invoke-Editor([string]$Step, [string[]]$Options, [switch]$Graphics, [switch]$ExpectFailure) {
    $log = Join-Path $output ($Step + '.log')
    $arguments = @('-batchmode', '-quit', '-projectPath', $project, '-logFile', $log) + $Options
    if (!$Graphics) { $arguments += '-nographics' }
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $editor -ArgumentList $quoted -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit(240000)) { $process.Kill(); throw "Timed out. See $log" }
        $process.Refresh()
        if (($process.ExitCode -ne 0) -ne [bool]$ExpectFailure) { throw "Unexpected Unity exit code. See $log" }
    }
    finally { $process.Dispose() }
}

Invoke-Editor 'layout' @('-executeMethod', 'Test.Client.Combat.Editor.TournamentUiVisual.Run',
    '-tournament-ui-visual', $output) -Graphics
Invoke-Editor 'frames' @('-executeMethod', 'AwithGameFrame.Unity.Editor.UnityVisualVerification.RunBatch',
    '-awith-visual-spec', (Join-Path $output 'visual-cases.json'), '-awith-visual-output', (Join-Path $output 'Frames'))

$prototype = Join-Path $PSScriptRoot 'ui-prototypes/tournament.json'
$profile = Join-Path $PSScriptRoot 'ui-prototypes/tournament.profile.json'
$prefab = Join-Path $project 'Assets/Resources/Tournament/GeneratedView.prefab'
$wrapper = Join-Path $project 'Assets/Resources/Tournament/Lobby.prefab'
$initialMeta = (Get-FileHash -LiteralPath ($prefab + '.meta')).Hash
$initialWrapper = (Get-FileHash -LiteralPath $wrapper).Hash
function ReadyId {
    $match = [regex]::Match((Get-Content -LiteralPath $prefab -Raw), '(?m)id: ready\s*\n\s*binding: ready[\s\S]*?target: \{fileID: (\d+)\}')
    if (!$match.Success) { throw 'Could not read the ready binding file ID.' }
    $match.Groups[1].Value
}
$initialReadyId = ReadyId

function Test-Import([string]$Name, [scriptblock]$Edit, [string]$Problem) {
    $draft = Get-Content -LiteralPath $prototype -Raw -Encoding utf8 | ConvertFrom-Json
    & $Edit $draft
    $design = Join-Path $output ($Name + '.json')
    $result = Join-Path $output ($Name + '-report.json')
    [IO.File]::WriteAllText($design, ($draft | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $before = (Get-FileHash -LiteralPath $prefab).Hash
    Invoke-Editor $Name @('-executeMethod', 'AwithGameFrame.Tools.UiPrototype.UnityPrototypeImporter.RunBatch',
        '-awithPrototypeReport', $result, '-awithPrototypeProfile', $profile, '-awithPrototypeDesign', $design) -ExpectFailure:([bool]$Problem)
    $proof = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($Problem) {
        if (!($proof.problems -match $Problem) -or (Get-FileHash -LiteralPath $prefab).Hash -ne $before) {
            throw "Import problem did not match or changed the existing prefab. See $result"
        }
    }
    elseif (!$proof.written -or $proof.changed -notcontains 'ready' -or $proof.changed -notcontains 'backpack-icon') {
        throw "Edited source did not update both expected stable nodes. See $result"
    }
    if ((Get-FileHash -LiteralPath ($prefab + '.meta')).Hash -ne $initialMeta -or
        (Get-FileHash -LiteralPath $wrapper).Hash -ne $initialWrapper -or (ReadyId) -ne $initialReadyId) {
        throw 'The import changed native identities or the owning prefab.'
    }
}

Test-Import 'edited' {
    param($draft)
    $ready = $draft.nodes | Where-Object id -eq 'ready'
    $ready.text = 'Confirm ready'
    $ready.width = 270
    ($draft.nodes | Where-Object id -eq 'backpack-icon').asset = 'fish-slot'
} ''
Test-Import 'missing-resource' {
    param($draft)
    ($draft.nodes | Where-Object id -eq 'backpack-icon').asset = 'missing-fish'
} 'backpack-icon: missing asset'
Test-Import 'duplicate-id' {
    param($draft)
    $draft.nodes += $draft.nodes[0]
} 'duplicate node ID'
Test-Import 'invalid-binding' {
    param($draft)
    ($draft.nodes | Where-Object id -eq 'ready').binding = 'missing-ready'
} 'ready: required button binding'
Write-Output "TOURNAMENT_UI_VERIFIED $output"
