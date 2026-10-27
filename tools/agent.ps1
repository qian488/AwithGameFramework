[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$AgentArguments)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repositoryRoot
try {
    & dotnet run --project (Join-Path $PSScriptRoot 'AgentKit/AwithGameFrame.AgentKit.csproj') -- @AgentArguments
    $agentExitCode = $LASTEXITCODE
} finally { Pop-Location }
exit $agentExitCode
