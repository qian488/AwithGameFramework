[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$AtlasOutputPath
)

$ErrorActionPreference = 'Stop'
$tables = Join-Path $PSScriptRoot '../../configuration/BBads'
$entries = [ordered]@{}
function Add-Resource([string]$Key, [string]$Type, [string]$Origin, [bool]$AllowMultiple = $false) {
    if ([string]::IsNullOrWhiteSpace($Key)) { return }
    if ($Key -notlike 'BBads/*') { $Key = "BBads/$Key" }
    $identity = "$Key|$Type"
    if (!$entries.Contains($identity)) {
        $entries[$identity] = [ordered]@{ key = $Key; type = $Type; origin = $Origin; allowMultiple = $AllowMultiple }
    }
}

$spriteFields = @('pictureResourcePath', 'frameResourcePath', 'projectileResourcePath')
$spineFields = @('spineResourcePath', 'castEffectResourcePath', 'hitEffectResourcePath', 'deathEffectResourcePath')
foreach ($name in 'unit', 'attack_profile', 'item') {
    foreach ($row in Import-Csv -LiteralPath (Join-Path $tables "$name.csv") -Encoding UTF8) {
        $source = "configuration/BBads/${name}.csv id=$($row.id)"
        foreach ($field in $spriteFields) {
            Add-Resource $row.$field 'UnityEngine.Sprite' "$source $field"
        }
        foreach ($field in $spineFields) {
            Add-Resource $row.$field 'Spine.Unity.SkeletonDataAsset' "$source $field" $true
        }
        if ($name -eq 'item') {
            Add-Resource "SlotShapes/$($row.shape)" 'BackpackSlotSO' "$source shape"
        }
    }
}
Add-Resource 'Unit' 'UnityEngine.GameObject' 'CombatSpawner'
foreach ($key in 'Unit_HealthBar', 'Unit_HealthBar1', 'Unit_HealthBar2') {
    Add-Resource "HealthBar/$key" 'UnityEngine.Sprite' 'WorldHealthBar'
}
foreach ($table in Get-ChildItem -LiteralPath $tables -Filter '*.csv' -File | Sort-Object Name) {
    Add-Resource "Csv/$($table.BaseName)" 'UnityEngine.TextAsset' "configuration/BBads/$($table.Name)"
}

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$spec = [ordered]@{ schemaVersion = 1; resources = @($entries.Values) }
[IO.File]::WriteAllText($output, ($spec | ConvertTo-Json -Depth 5))
Write-Host "Resource specification: $output"

if ($AtlasOutputPath) {
    $atlas = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../client/Unity/Assets/BBads/Editor/Atlases/Catalog.json') -Raw |
        ConvertFrom-Json
    $groups = @(foreach ($group in $atlas.groups) {
        [ordered]@{
            atlas = "Assets/BBads/Atlases/$($group.name).spriteatlasv2"
            sources = @($group.sources | ForEach-Object { "Assets/BBads/$_" })
        }
    })
    $atlasOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AtlasOutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($atlasOutput)) | Out-Null
    [IO.File]::WriteAllText($atlasOutput, ([ordered]@{ schemaVersion = 1; groups = $groups } | ConvertTo-Json -Depth 5))
    Write-Host "Atlas specification: $atlasOutput"
}
