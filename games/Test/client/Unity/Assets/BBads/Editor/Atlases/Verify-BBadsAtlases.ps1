[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EditorPath,
    [string]$SourceAssets,
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$client = Get-Item -LiteralPath (Join-Path $PSScriptRoot '../../../..')
$repository = $client.Parent.Parent.Parent.Parent.FullName
$bbads = Join-Path $client.FullName 'Assets/BBads'
if (!$SourceAssets) { $SourceAssets = $bbads }
$source = (Get-Item -LiteralPath $SourceAssets).FullName
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Catalog.json') -Raw | ConvertFrom-Json

$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$evidence = Join-Path $repository "artifacts/bbads-atlas/$runId"
$project = Join-Path $evidence 'Project'
$probeAssets = Join-Path $project 'Assets/BBads'
$probeEditor = Join-Path $probeAssets 'Editor/Atlases'
$probeRuntime = Join-Path $project 'Assets/AtlasProbe'
$settings = Join-Path $project 'ProjectSettings'
$packages = Join-Path $project 'Packages'
foreach ($folder in @($probeEditor, $probeRuntime, $settings, $packages)) {
    [IO.Directory]::CreateDirectory($folder) | Out-Null
}
Write-Host "BBads atlas evidence: $evidence"

function Invoke-ProbeProcess([string]$Executable, [string[]]$Arguments, [string]$LogPath) {
    $quoted = $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $originalProfile = $env:ALLUSERSPROFILE
    if (!$originalProfile) {
        $env:ALLUSERSPROFILE = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    }
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru
    }
    finally {
        $env:ALLUSERSPROFILE = $originalProfile
    }

    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "Timed out after $TimeoutSeconds seconds. See $LogPath"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode). See $LogPath" }
}

function Get-StableCounter($Samples, [string]$Label) {
    $samplesArray = @($Samples)
    $unique = @($samplesArray | Sort-Object -Unique)
    if ($samplesArray.Count -ne 60 -or $unique.Count -ne 1 -or $unique[0] -le 0) {
        throw "Expected 60 stable positive samples for $Label. See the raw report."
    }
    return [long]$unique[0]
}

foreach ($group in $catalog.groups) {
    foreach ($relative in $group.sources) {
        $candidates = @($relative)
        if ($relative.StartsWith('Resources/BBads/')) {
            $suffix = $relative.Substring('Resources/BBads/'.Length)
            $candidates += "Resources/BBads/BBads/$suffix", "Resources/$suffix"
        }
        $matches = @($candidates | Where-Object {
            Test-Path -LiteralPath (Join-Path $source $_) -PathType Leaf
        })
        if ($matches.Count -ne 1) { throw "Expected one source for $relative under $source. Found $($matches.Count)." }
        $inputFile = Join-Path $source $matches[0]
        $destination = Split-Path -Parent (Join-Path $probeAssets $matches[0])
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        Copy-Item -LiteralPath $inputFile,($inputFile + '.meta') -Destination $destination
    }
}
Copy-Item -LiteralPath (Join-Path $bbads 'Atlases'),(Join-Path $bbads 'Atlases.meta') -Destination $probeAssets -Recurse
foreach ($file in @('BBadsAtlases.cs', 'Catalog.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $probeEditor
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BBadsAtlasBuild.cs.txt') -Destination (Join-Path $probeEditor 'BBadsAtlasBuild.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BBadsAtlasProbe.cs.txt') -Destination (Join-Path $probeRuntime 'BBadsAtlasProbe.cs')
Copy-Item -LiteralPath (Join-Path $client.FullName 'ProjectSettings/ProjectVersion.txt') -Destination $settings
$editorSettings = [IO.File]::ReadAllText((Join-Path $client.FullName 'ProjectSettings/EditorSettings.asset'))
[IO.File]::WriteAllText((Join-Path $settings 'EditorSettings.asset'),
    ($editorSettings -replace '(?m)^  m_SpritePackerMode: \d+', '  m_SpritePackerMode: 5'))
[IO.File]::WriteAllText((Join-Path $packages 'manifest.json'),
    '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')

$editorLog = Join-Path $evidence 'editor.log'
Invoke-ProbeProcess $editor @('-batchmode', '-nographics', '-quit', '-buildTarget', 'Win64',
    '-projectPath', $project, '-logFile', $editorLog,
    '-executeMethod', 'Test.BBads.AtlasTools.BBadsAtlasBuild.BuildFixtures') $editorLog

foreach ($variant in @('baseline', 'atlas')) {
    $directory = Join-Path $evidence $variant
    $playerLog = Join-Path $directory 'player.log'
    $arguments = @('-batchmode', '-force-d3d11', '-logFile', $playerLog, '-atlasEvidence', $directory)
    if ($variant -eq 'atlas') { $arguments += '-atlasBaseline', (Join-Path $evidence 'baseline') }
    Invoke-ProbeProcess (Join-Path $directory 'AtlasProbe.exe') $arguments $playerLog
}

$summary = foreach ($group in $catalog.groups) {
    $before = Get-Content -LiteralPath (Join-Path $evidence "baseline/$($group.name).json") -Raw | ConvertFrom-Json
    $after = Get-Content -LiteralPath (Join-Path $evidence "atlas/$($group.name).json") -Raw | ConvertFrom-Json
    if ($before.sprites -ne $after.sprites -or $before.packed -ne 0 -or $after.packed -ne $after.sprites -or
        @($after.textures).Count -ne 1 -or @($before.textures).Count -le @($after.textures).Count) {
        throw "Unexpected texture binding for $($group.name). See the raw reports."
    }
    if ($before.visiblePixels -lt 1000 -or $after.visiblePixels -lt 1000 -or
        $after.pixelsOver32 -gt ($before.visiblePixels * 0.05)) {
        throw "Blank render or excessive pixel difference for $($group.name). See the PNG pair and raw reports."
    }
    [pscustomobject]@{
        group = $group.name
        baselineBatches = Get-StableCounter $before.batches "$($group.name) baseline batches"
        atlasBatches = Get-StableCounter $after.batches "$($group.name) atlas batches"
        baselineDraws = Get-StableCounter $before.draws "$($group.name) baseline draws"
        atlasDraws = Get-StableCounter $after.draws "$($group.name) atlas draws"
        baselineSetPass = Get-StableCounter $before.setPass "$($group.name) baseline SetPass"
        atlasSetPass = Get-StableCounter $after.setPass "$($group.name) atlas SetPass"
        baselineTextureBytes = $before.textureRuntimeBytes
        atlasTextureBytes = $after.textureRuntimeBytes
        pixelsOver32 = $after.pixelsOver32
        maxChannelDelta = $after.maxChannelDelta
    }
}
[ordered]@{
    unity = $after.unity
    graphicsApi = $after.graphicsApi
    gpu = $after.gpu
    colorSpace = $after.colorSpace
    sourceAssets = $source
    baselineBuildBytes = [long](Get-Content -LiteralPath (Join-Path $evidence 'baseline/build-bytes.txt'))
    atlasBuildBytes = [long](Get-Content -LiteralPath (Join-Path $evidence 'atlas/build-bytes.txt'))
    groups = @($summary)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'comparison.json') -Encoding UTF8

$summary | Format-Table group, baselineBatches, atlasBatches, baselineDraws, atlasDraws
foreach ($group in $summary) {
    if ($group.atlasBatches -gt $group.baselineBatches -or $group.atlasDraws -gt $group.baselineDraws -or
        $group.atlasSetPass -gt $group.baselineSetPass) {
        throw "Rendering submission regression in $($group.group). See comparison.json."
    }
    if ($group.group -ne 'WorldCombat' -and $group.atlasBatches -ge $group.baselineBatches) {
        throw "Expected fewer UI batches for $($group.group). See comparison.json."
    }
}
Write-Host 'BBADS_ATLAS_REGRESSION_PASSED'
