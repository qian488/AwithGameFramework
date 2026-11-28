[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$HudCaptureDirectory
)

$ErrorActionPreference = 'Stop'

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$integration = Join-Path (Split-Path -Parent $PSScriptRoot) 'bbads-integration'
& (Join-Path $integration 'write-resource-spec.ps1') -OutputPath (Join-Path $output 'resources.json') `
    -AtlasOutputPath (Join-Path $output 'atlases.json')

function Write-VisualSpec([string]$Name, [System.Collections.IDictionary]$Case) {
    $path = Join-Path $output "$Name.json"
    $spec = [ordered]@{ schemaVersion = 1; cases = @($Case) }
    [IO.File]::WriteAllText($path, ($spec | ConvertTo-Json -Depth 6))
}

Write-VisualSpec 'hud-scene' ([ordered]@{
    id = 'bbads-hud-scene'
    scene = 'Assets/BBads/Scenes/BBadsBattle.unity'
    camera = '/Main Camera'
    width = 540
    height = 960
    requireNonUniform = $true
    maxDarkRatio = 0.9
    maxMagentaRatio = 0.005
    visible = @('/Canvas/BattleHUD/WaveLabelFrame', '/Canvas/BattleHUD/WaveText')
})

if ($HudCaptureDirectory) {
    $captures = Get-Item -LiteralPath $HudCaptureDirectory
    if (!$captures.PSIsContainer) { throw "Expected a HUD capture directory: $HudCaptureDirectory" }
    $baseline = (Get-Item -LiteralPath (Join-Path $captures.FullName 'hud-clean.png')).FullName
    foreach ($stage in 'clean', 'hidden', 'restored') {
        $image = (Get-Item -LiteralPath (Join-Path $captures.FullName "hud-$stage.png")).FullName
        Write-VisualSpec "hud-pixels-$stage" ([ordered]@{
            id = "bbads-hud-$stage"
            image = $image
            baseline = $baseline
            width = 540
            height = 960
            requireNonUniform = $true
            maxDarkRatio = 0.9
            maxMagentaRatio = 0.005
            pixelTolerance = 4
            maxChangedRatio = 0
        })
    }
}
