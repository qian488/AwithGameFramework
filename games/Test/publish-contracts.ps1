[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$VerificationSummary,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$summaryFile = Get-Item -LiteralPath $VerificationSummary
$summary = [IO.File]::ReadAllText($summaryFile.FullName) | ConvertFrom-Json
$root = Join-Path $repository 'artifacts/game-unity/Test'
$project = Join-Path $summaryFile.Directory.FullName 'client/Test-Unity-Verification'
if ($summary.game -cne 'Test' -or !$summary.importVerified -or !$summary.verificationProjectRetained -or
    $summary.verificationProject -ne $project -or
    !$project.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use the summary from verify-game-unity.ps1 -Name Test -KeepProject.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new publication directory.' }
$staging = Join-Path $summaryFile.Directory.FullName ('ContractPublish-' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$format = [Globalization.CultureInfo]::InvariantCulture
$releases = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'configuration/contracts.json')) | ConvertFrom-Json
$seen = [Collections.Generic.HashSet[string]]::new()
$offers = [ordered]@{}
foreach ($release in $releases) {
    $id = $release.id
    $version = $release.version
    if ($id -cnotmatch '^[a-z0-9][a-z0-9-]{0,47}$' -or $version -cnotmatch '^[a-z0-9][a-z0-9-]{0,47}$' -or
        !$seen.Add("$id/$version") -or $release.banner.Count -ne 3) { throw "Invalid offer $id/$version." }
    if ($offers.Contains($id) -and $offers[$id].title -ne $release.title) { throw "Inconsistent title for $id." }
    $offers[$id] = [ordered]@{ id = $id; title = $release.title; version = $version }
}

foreach ($release in $releases) {
    $id = $release.id
    $version = $release.version
    $name = "$id-$version"
    $stage = Join-Path $staging $name
    $inputDirectory = Join-Path $stage 'Data'
    [IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
    $strike = @('Id,Windup,Active,Recovery,Damage,Pressure,Reach,Arc',
        "$($release.bossId),$($release.windup),5,38,25,23,2.65,110")
    [IO.File]::WriteAllLines((Join-Path $inputDirectory 'BossStrike.csv'), [string[]]$strike, $utf8)
    $boss = [ordered]@{
        Id = $release.bossId; Map = $release.map; Name = $release.boss; Advice = 'Deflect the opening strike.'
        Weapon = $release.weapon; Health = 108; Posture = 96; Cooldown = 60; Speed = 2.5; Lunge = 0.55
        Guards = $false; Opening = @($release.bossId); FollowUp = @(); Desperation = @()
    }
    [IO.File]::WriteAllText((Join-Path $inputDirectory 'BossChallenge.json'), (ConvertTo-Json -InputObject @($boss) -Depth 5), $utf8)
    $tables = Join-Path $stage 'Generated'
    & dotnet run --project (Join-Path $repository 'tools/ConfigGen') -- (Join-Path $PSScriptRoot 'configuration/schema.json') $inputDirectory $tables
    if ($LASTEXITCODE -ne 0) { throw "Configuration build failed for $name." }

    $assetDirectory = "Assets/ContractRelease/$name"
    [IO.Directory]::CreateDirectory((Join-Path $project $assetDirectory)) | Out-Null
    $scene = "$assetDirectory/Battle.unity"
    $banner = "$assetDirectory/Banner.mat"
    Copy-Item -LiteralPath (Join-Path $project 'Assets/Scenes/CombatShowcase.unity') -Destination (Join-Path $project $scene)
    $sourceMaterial = Join-Path $project 'Assets/Content/RainPass/Generated/RainPass_Basalt.mat'
    $material = [IO.File]::ReadAllText($sourceMaterial)
    $colors = @($release.banner | ForEach-Object { [Convert]::ToString($_, $format) })
    $material = $material -replace 'm_Name: RainPass_Basalt', "m_Name: $name banner"
    $material = $material -replace '(?m)^    - _Color: .+$', "    - _Color: {r: $($colors[0]), g: $($colors[1]), b: $($colors[2]), a: 1}"
    [IO.File]::WriteAllText((Join-Path $project $banner), $material, $utf8)
    $spec = [ordered]@{
        bundles = @(
            @{ name = "$name-scene"; assetPaths = @($scene) },
            @{ name = "$name-banner"; assetPaths = @($banner) }
        )
        assets = @(
            @{ key = 'contract-scene'; bundle = "$name-scene"; assetPath = $scene; scene = $true },
            @{ key = 'contract-banner'; bundle = "$name-banner"; assetPath = $banner }
        )
    }
    $specPath = Join-Path $stage 'bundle-spec.json'
    [IO.File]::WriteAllText($specPath, ($spec | ConvertTo-Json -Depth 7), $utf8)
    $bundles = Join-Path $stage 'Bundles'
    $build = @{ EditorPath = $EditorPath; ProjectPath = $project; SpecificationPath = $specPath; OutputDirectory = $bundles }
    & (Join-Path $repository 'tools/build-unity-content.ps1') @build
    Copy-Item -LiteralPath (Join-Path $tables 'BossStrike.json'), (Join-Path $tables 'BossChallenge.json') -Destination $bundles
    $descriptor = @{ id = $id; bossId = $release.bossId }
    [IO.File]::WriteAllText((Join-Path $bundles 'contract.json'), ($descriptor | ConvertTo-Json -Compress), $utf8)
    & (Join-Path $repository 'tools/publish-content.ps1') -SourceDirectory $bundles -OutputDirectory (Join-Path $output "$id/$version") -Version $version
}
[IO.File]::WriteAllText((Join-Path $output 'index.json'), (ConvertTo-Json -InputObject @($offers.Values) -Depth 5), $utf8)
Write-Host "Published contracts: $output"
