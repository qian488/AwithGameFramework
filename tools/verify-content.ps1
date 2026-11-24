[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$SigningKeyPath,
    [string]$PublicKeyPath
)
$ErrorActionPreference = 'Stop'
if ([bool]$SigningKeyPath -ne [bool]$PublicKeyPath) { throw 'Supply both the private signing and the public verification key.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new verification output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$published = Join-Path $output 'Published'
& (Join-Path $PSScriptRoot 'publish-content.ps1') -SourceDirectory $SourceDirectory -OutputDirectory $published -Version $Version -SigningKeyPath $SigningKeyPath
$checkArguments = @($published, (Join-Path $output 'Installed'), (Join-Path $output 'Install-report.json'))
if ($PublicKeyPath) { $checkArguments += $PublicKeyPath }
& dotnet run --project (Join-Path $PSScriptRoot 'ContentVerify/AwithGameFrame.ContentVerify.csproj') --configuration Release -- @checkArguments
if ($LASTEXITCODE -ne 0) { throw 'Local HTTP content installation failed.' }
Write-Host "Published and verified installation report: $(Join-Path $output 'Install-report.json')"
