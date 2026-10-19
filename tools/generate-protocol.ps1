[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$protocolRoot = Join-Path $repositoryRoot 'samples/Protocol'
$arguments = @('run', '--project', (Join-Path $PSScriptRoot 'ConfigGen/AwithGameFrame.ConfigGen.csproj'), '--configuration', 'Release', '--', '--protocol',
    (Join-Path $protocolRoot 'schema.json'), (Join-Path $protocolRoot 'baseline.json'), (Join-Path $protocolRoot 'Generated/GameProtocol.cs'))
if ($Check) { $arguments += '--check' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Protocol generation/compatibility verification failed.' }
