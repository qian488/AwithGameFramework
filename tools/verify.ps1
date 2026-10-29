[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = Join-Path $repositoryRoot 'src'
$solutionPath = Join-Path $repositoryRoot 'AwithGameFrame.sln'

function Test-PathWithin {
    param([string]$Path, [string]$Directory)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/')
    return $fullPath.Equals($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-JsonProperty {
    param($Object, [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Read-Json {
    param([string]$Path)
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'verify-docs.ps1')

Write-Host 'Checking portable source and project dependencies...'
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
})
$projectFiles = @($sourceFiles | Where-Object { $_.Extension -eq '.csproj' })
if ($projectFiles.Count -eq 0) { throw 'No portable projects found under src/.' }

# Strip comments and literals so documentation mentioning an engine is allowed.
$nonCodePattern = '(?s)/\*.*?\*/|//[^\r\n]*|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*'''
$enginePattern = '\b(UnityEngine|UnityEditor|UniTask|Cysharp\.Threading\.Tasks|Godot)\b'
foreach ($file in @($sourceFiles | Where-Object { $_.Extension -eq '.cs' })) {
    $code = [regex]::Replace([System.IO.File]::ReadAllText($file.FullName), $nonCodePattern, ' ')
    if ($code -match $enginePattern) { throw "Engine dependency in portable source: $($file.FullName)" }
}
# Keep the recommended instance runtime independent of the legacy global facades.
$instanceDirectories = @('Hosting', 'Flow', 'Events', 'Timing', 'Pooling', 'Resources') |
    ForEach-Object { Join-Path $sourceRoot "AwithGameFrame.Core/Runtime/$_" }
$instanceDirectories += @('Audio', 'Input', 'Presentation', 'Scenes', 'Settings') |
    ForEach-Object { Join-Path $sourceRoot "AwithGameFrame.Gameplay/Runtime/$_" }
$instanceFiles = @($instanceDirectories | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -Filter '*.cs' -File })
$instanceFiles += Get-Item -LiteralPath (Join-Path $sourceRoot 'AwithGameFrame.Gameplay/Runtime/Localization/LocalizationService.cs')
foreach ($file in $instanceFiles) {
    $code = [regex]::Replace([IO.File]::ReadAllText($file.FullName), $nonCodePattern, ' ')
    if ($code -match '\b(BaseManager|FrameworkBootstrap|ServiceLocator|EventCenter|DataPersistenceAPI)\b') {
        throw "Instance runtime depends on a legacy global API: $($file.FullName)"
    }
}
foreach ($file in $projectFiles) {
    [xml]$project = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($reference in $project.SelectNodes("//*[local-name()='ProjectReference']")) {
        $target = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $reference.GetAttribute('Include')))
        if (-not (Test-PathWithin $target $sourceRoot)) {
            throw "Portable project references a host project: $($file.FullName) -> $target"
        }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing project reference: $target" }
    }
    foreach ($reference in $project.SelectNodes("//*[local-name()='Reference' or local-name()='PackageReference']")) {
        if ($reference.GetAttribute('Include') -match '(?i)Unity|UniTask|Cysharp|Godot') {
            throw "Engine assembly or package reference in portable project: $($file.FullName)"
        }
    }
    foreach ($hint in $project.SelectNodes("//*[local-name()='HintPath']")) {
        $target = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $hint.InnerText))
        foreach ($hostDirectory in @('adapters', 'samples')) {
            if (Test-PathWithin $target (Join-Path $repositoryRoot $hostDirectory)) {
                throw "Portable assembly reference points into ${hostDirectory}: $($file.FullName)"
            }
        }
    }
}

Write-Host 'Checking Unity import metadata, assembly definitions, and package paths...'
$unityPackageRoots = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'adapters') -Directory |
    Where-Object { $_.Name -eq 'unity' -or $_.Name.StartsWith('unity-') } | ForEach-Object { $_.FullName })
$packageRoots = @($sourceFiles | Where-Object { $_.Name -eq 'package.json' } | ForEach-Object { $_.DirectoryName }) +
    $unityPackageRoots + @(Join-Path $repositoryRoot 'samples/Shared')
function Assert-UnityImportMetadata {
    param([string[]]$PackageRoots, [string]$SampleAssetsRoot)
    $importRoots = $PackageRoots + @($SampleAssetsRoot)
    $metadataGuids = @{}
    foreach ($importRoot in $importRoots) {
        foreach ($item in Get-ChildItem -LiteralPath $importRoot -Recurse -Force) {
            $relativePath = $item.FullName.Substring($importRoot.Length).TrimStart([char[]]'\/')
            # Unity excludes hidden paths and folders ending in ~ from asset import.
            if ($relativePath -match '(^|[\\/])\.[^\\/]*|(^|[\\/])[^\\/]*~([\\/]|$)') { continue }
            if (($PackageRoots -contains $importRoot) -and $relativePath -match '(^|[\\/])(bin|obj)([\\/]|$)') {
                throw "Build output inside a Unity package: $($item.FullName). Keep .NET outputs under artifacts/."
            }
            if ($item.Extension -eq '.meta') {
                $meta = Get-Content -LiteralPath $item.FullName -Raw
                if ($meta -notmatch '(?m)^guid:[ \t]*([a-fA-F0-9]{32})[ \t]*\r?$') {
                    throw "Missing or invalid Unity GUID: $($item.FullName)"
                }
                $guid = $Matches[1]
                if ($metadataGuids.ContainsKey($guid)) {
                    throw "Duplicate Unity GUID ${guid}: $($metadataGuids[$guid]) and $($item.FullName)"
                }
                $metadataGuids[$guid] = $item.FullName
                continue
            }
            # IDE project files are ignored by Unity and do not require import metadata.
            if (-not $item.PSIsContainer -and $item.Extension -match '^\.(csproj|sln|slnx|suo|user|userprefs|tmp)$') { continue }
            if (-not (Test-Path -LiteralPath ($item.FullName + '.meta') -PathType Leaf)) {
                throw "Unity import asset is missing its .meta file: $($item.FullName)"
            }
        }
    }
}
Assert-UnityImportMetadata -PackageRoots $packageRoots -SampleAssetsRoot (Join-Path $repositoryRoot 'samples/Unity/Assets')
$assemblyDefinitions = @{}
$assemblyGuids = @{}
$scanRoots = @(@('src', 'samples/Unity/Assets', 'samples/Shared') | ForEach-Object { Join-Path $repositoryRoot $_ }) + $unityPackageRoots
foreach ($relativeRoot in $scanRoots) {
    foreach ($file in Get-ChildItem -LiteralPath $relativeRoot -Recurse -Filter '*.asmdef' -File) {
        $definition = Read-Json $file.FullName
        $name = Get-JsonProperty $definition 'name'
        if ([string]::IsNullOrWhiteSpace($name)) { throw "Unnamed assembly definition: $($file.FullName)" }
        if ($assemblyDefinitions.ContainsKey($name)) { throw "Duplicate Unity assembly: $name" }
        $assemblyDefinitions[$name] = @{ Definition = $definition; Path = $file.FullName; Dependencies = @() }
        if (Test-Path -LiteralPath ($file.FullName + '.meta')) {
            $meta = Get-Content -LiteralPath ($file.FullName + '.meta') -Raw
            if ($meta -match '(?m)^guid:\s*([a-fA-F0-9]+)\s*$') { $assemblyGuids[$Matches[1]] = $name }
        }
        if (Test-PathWithin $file.FullName $sourceRoot) {
            if ((Get-JsonProperty $definition 'noEngineReferences') -ne $true) {
                throw "Portable Unity assembly must set noEngineReferences: $($file.FullName)"
            }
            foreach ($reference in @(Get-JsonProperty $definition 'precompiledReferences')) {
                if ($reference -match '(?i)Unity|UniTask|Cysharp') { throw "Engine reference in portable assembly $name" }
            }
        }
    }
}
foreach ($name in $assemblyDefinitions.Keys) {
    $entry = $assemblyDefinitions[$name]
    foreach ($reference in @(Get-JsonProperty $entry.Definition 'references')) {
        if ([string]::IsNullOrWhiteSpace($reference)) { continue }
        $targetName = $reference
        if ($reference.StartsWith('GUID:')) {
            $guid = $reference.Substring(5)
            if ($assemblyGuids.ContainsKey($guid)) { $targetName = $assemblyGuids[$guid] }
        }
        if ($assemblyDefinitions.ContainsKey($targetName)) {
            $entry.Dependencies += $targetName
            if ((Test-PathWithin $entry.Path $sourceRoot) -and
                -not (Test-PathWithin $assemblyDefinitions[$targetName].Path $sourceRoot)) {
                throw "Portable Unity assembly depends on a host assembly: $name -> $targetName"
            }
        } elseif ($targetName.StartsWith('AwithGameFrame.')) {
            throw "Missing framework Unity assembly: $name -> $targetName"
        } elseif ((Test-PathWithin $entry.Path $sourceRoot) -and $targetName -match '(?i)Unity|UniTask|Cysharp') {
            throw "Engine reference in portable Unity assembly: $name -> $targetName"
        }
    }
}
$visited = @{}
$visiting = @{}
function Visit-Assembly {
    param([string]$Name)
    if ($visiting.ContainsKey($Name)) { throw "Unity assembly dependency cycle at $Name" }
    if ($visited.ContainsKey($Name)) { return }
    $visiting[$Name] = $true
    foreach ($dependency in $assemblyDefinitions[$Name].Dependencies) { Visit-Assembly $dependency }
    $visiting.Remove($Name)
    $visited[$Name] = $true
}
foreach ($name in $assemblyDefinitions.Keys) { Visit-Assembly $name }

foreach ($name in @('AwithGameFrame.Unity', 'AwithGameFrame.Extensions', 'AwithGameFrame.Unity.Editor')) {
    foreach ($reference in @(Get-JsonProperty $assemblyDefinitions[$name].Definition 'references')) {
        $resolved = $reference
        if ($reference.StartsWith('GUID:') -and $assemblyGuids.ContainsKey($reference.Substring(5))) {
            $resolved = $assemblyGuids[$reference.Substring(5)]
        }
        if ($resolved -match 'Foundation|Legacy|Compatibility|UniTask|DOTween|SQLite') {
            throw "Modern Unity adapter depends on a compatibility/plugin assembly: $name -> $reference"
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'adapters/unity') -Recurse -Filter '*.cs' -File) {
    $code = [regex]::Replace([IO.File]::ReadAllText($file.FullName), $nonCodePattern, ' ')
    if ($code -match '\b(BaseManager|FrameworkBootstrap|ServiceLocator|EventCenter|DataPersistenceAPI|Cysharp|DOTween|Sqlite)\b') {
        throw "Modern Unity adapter contains a legacy global or optional plugin dependency: $($file.FullName)"
    }
}

$manifestPath = Join-Path $repositoryRoot 'samples/Unity/Packages/manifest.json'
$manifest = Read-Json $manifestPath
$localFrameworkPackages = @{}
foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
    if (-not $dependency.Value.StartsWith('file:')) { continue }
    $relativePath = [System.Uri]::UnescapeDataString($dependency.Value.Substring(5))
    $packageDirectory = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $manifestPath) $relativePath))
    if (-not (Test-PathWithin $packageDirectory $repositoryRoot)) { throw "Local UPM package leaves the repository: $($dependency.Name)" }
    $packagePath = Join-Path $packageDirectory 'package.json'
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Missing local UPM package: $packagePath" }
    $package = Read-Json $packagePath
    if ($package.name -ne $dependency.Name) { throw "UPM package name does not match manifest: $($dependency.Name)" }
    $localFrameworkPackages[$package.name] = $true
}
foreach ($packagePath in (@($sourceFiles | Where-Object { $_.Name -eq 'package.json' }) +
    @($unityPackageRoots | ForEach-Object { Get-Item -LiteralPath (Join-Path $_ 'package.json') }))) {
    $package = Read-Json $packagePath.FullName
    if (-not $localFrameworkPackages.ContainsKey($package.name)) {
        throw "Unity sample is missing a local framework package: $($package.name)"
    }
}

if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) { throw "Missing solution: $solutionPath" }
Push-Location $repositoryRoot
try {
    & (Join-Path $PSScriptRoot 'generate-config.ps1') -Check
    & (Join-Path $PSScriptRoot 'generate-protocol.ps1') -Check
    Invoke-DotNet -Arguments @('build', $solutionPath, '--configuration', $Configuration, '--nologo')
    Assert-UnityImportMetadata -PackageRoots $packageRoots -SampleAssetsRoot (Join-Path $repositoryRoot 'samples/Unity/Assets')
    Invoke-DotNet -Arguments @('run', '--project', (Join-Path $repositoryRoot 'tools/AgentKit/AwithGameFrame.AgentKit.csproj'), '--configuration', $Configuration, '--no-build', '--no-restore', '--', 'contracts')
    Invoke-DotNet -Arguments @('test', $solutionPath, '--configuration', $Configuration, '--no-build', '--no-restore', '--nologo', '--logger', 'trx;LogFileName=framework.trx', '--results-directory', (Join-Path $repositoryRoot 'artifacts/TestResults'))
    Invoke-DotNet -Arguments @('test', (Join-Path $repositoryRoot 'templates/portable-feature/Tests~/AwithGameFrame.FeatureTemplate.Tests.csproj'), '--configuration', $Configuration, '--nologo', '--logger', 'trx;LogFileName=template.trx', '--results-directory', (Join-Path $repositoryRoot 'artifacts/TestResults'))
    Invoke-DotNet -Arguments @('run', '--project', (Join-Path $repositoryRoot 'samples/Headless/AwithGameFrame.Headless.csproj'), '--configuration', $Configuration, '--no-build', '--no-restore')
} finally {
    Pop-Location
}
Write-Host 'Documentation, architecture checks, agent contracts, build, tests, reusable template, and headless sample passed.'
