[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sharedRoot = Join-Path $repositoryRoot 'samples/Shared'
$generatedRoot = Join-Path $sharedRoot 'Runtime/Generated'
$arguments = @('run', '--project', (Join-Path $PSScriptRoot 'ConfigGen/AwithGameFrame.ConfigGen.csproj'), '--configuration', 'Release', '--',
    (Join-Path $sharedRoot 'Configuration/schema.json'), (Join-Path $sharedRoot 'Configuration/Data'), $generatedRoot)
if ($Check) { $arguments += '--check' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Configuration generation/verification failed.' }
foreach ($destination in @('samples/Unity/Assets/Resources/AwithDemo', 'samples/Godot/Configuration')) {
    $destinationRoot = Join-Path $repositoryRoot $destination
    $expectedNames = @(Get-ChildItem -LiteralPath $generatedRoot -Filter '*.json' -File | ForEach-Object Name)
    if (Test-Path -LiteralPath $destinationRoot) {
        foreach ($existing in Get-ChildItem -LiteralPath $destinationRoot -Recurse -Filter '*.json' -File) {
            if ($expectedNames -cnotcontains $existing.Name -or $existing.DirectoryName -ne [IO.Path]::GetFullPath($destinationRoot)) {
                throw "Unexpected host configuration; remove obsolete generated artifact explicitly: $($existing.FullName)"
            }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $generatedRoot -Filter '*.json' -File) {
        $target = Join-Path $destinationRoot $file.Name
        if ($Check) {
            if (-not (Test-Path -LiteralPath $target) -or [IO.File]::ReadAllText($target) -ne [IO.File]::ReadAllText($file.FullName)) {
                throw "Generated host configuration is stale: $target"
            }
        } else {
            New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
}
