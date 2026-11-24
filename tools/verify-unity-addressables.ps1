[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,39}$')][string]$Name
)

$ErrorActionPreference = 'Stop'
$editor = Get-Item -LiteralPath $EditorPath
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validation = Join-Path $repository ('artifacts/UnityAddressablesValidation/' + [Guid]::NewGuid().ToString('N'))
$project = if ($Name) { Join-Path $validation 'client/Fixture' } else { $validation }
foreach ($directory in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    [IO.Directory]::CreateDirectory((Join-Path $project $directory)) | Out-Null
}

$sampleManifest = Get-Content -LiteralPath (Join-Path $repository 'samples/Unity/Packages/manifest.json') -Raw | ConvertFrom-Json
$definition = Get-Content -LiteralPath (Join-Path $repository 'adapters/unity-addressables/package.json') -Raw | ConvertFrom-Json
$dependencies = [ordered]@{ 'com.unity.addressables' = $definition.dependencies.'com.unity.addressables' }
if (!$Name) {
    foreach ($package in @('src/AwithGameFrame.Core', 'src/AwithGameFrame.Data', 'src/AwithGameFrame.Gameplay', 'adapters/unity-addressables')) {
        $path = Join-Path $repository $package
        $definition = Get-Content -LiteralPath (Join-Path $path 'package.json') -Raw | ConvertFrom-Json
        $dependencies[$definition.name] = 'file:' + $path.Replace('\', '/')
    }
}
foreach ($module in $sampleManifest.dependencies.PSObject.Properties | Where-Object { $_.Name.StartsWith('com.unity.modules.') }) {
    $dependencies[$module.Name] = $module.Value
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $project 'Packages/manifest.json') -Encoding UTF8
$version = $editor.VersionInfo.ProductVersion.Split('_')[0]
"m_EditorVersion: $version" | Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8
if ($Name) {
    $source = Join-Path $repository ('games/' + $Name)
    Copy-Item -LiteralPath (Join-Path $source 'game.json') -Destination $validation
    & (Join-Path $PSScriptRoot 'game.ps1') build -Name $Name
    & (Join-Path $PSScriptRoot 'workspace-client.ps1') -RepositoryRoot $repository -GameRoot $validation -ClientDirectory Fixture -SourceGameRoot $source
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityFixtures/AddressablesSmoke.cs') -Destination (Join-Path $project 'Assets/Editor')
$fixtureAssembly = @{
    name = 'AwithGameFrame.Addressables.Smoke'
    references = @('AwithGameFrame.Unity.Addressables', 'Unity.Addressables.Editor')
    includePlatforms = @('Editor')
}
if (!$Name) { $fixtureAssembly.references += @('AwithGameFrame.Core', 'AwithGameFrame.Gameplay') }
$fixtureAssembly | ConvertTo-Json | Set-Content (Join-Path $project 'Assets/Editor/AddressablesSmoke.asmdef') -Encoding UTF8

function Invoke-AddressablesStep([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $project ($Name + '.log')
    $start = @{
        FilePath = $editor.FullName
        ArgumentList = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"')) + $Arguments
        PassThru = $true
    }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    try {
        if (!$process.WaitForExit(300000)) {
            $process.Kill()
            throw "Unity Addressables $Name timed out. See $log"
        }
        $process.Refresh()
        if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors' -Quiet)) {
            throw "Unity Addressables $Name failed with exit $($process.ExitCode). See $log"
        }
    }
    finally { $process.Dispose() }
    return $log
}

$previousProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE')
try {
    if (!$previousProfile -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData')
    }
    Write-Host "Addressables fixture with Unity $version at $project"
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $portProbe.Start()
        $baseUrl = 'http://127.0.0.1:' + $portProbe.LocalEndpoint.Port + '/'
    }
    finally { $portProbe.Stop() }
    $null = Invoke-AddressablesStep 'Build' @('-quit', '-awith-content-url', $baseUrl, '-executeMethod', 'AddressablesSmoke.Prepare')
    $runtime = Invoke-AddressablesStep 'PlayMode' @('-awith-addressables-smoke', '-awith-content-url', $baseUrl, '-executeMethod', 'AddressablesSmoke.Run')
    if (!(Select-String -LiteralPath $runtime -SimpleMatch 'AWITH_ADDRESSABLES_SMOKE_PASSED' -Quiet)) {
        throw "Unity Addressables PlayMode assertions missing. See $runtime"
    }
    Write-Host "Addressables asset build and PlayMode passed. See $runtime"
}
finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }
