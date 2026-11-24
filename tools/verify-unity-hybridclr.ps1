[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$HybridClrPackagePath,
    [Parameter(Mandatory = $true)][string]$PatchedIl2CppPath,
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,39}$')][string]$Name
)

$ErrorActionPreference = 'Stop'
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$package = Get-Item -LiteralPath $HybridClrPackagePath
$native = Get-Item -LiteralPath $PatchedIl2CppPath
$definition = Get-Content -LiteralPath (Join-Path $package.FullName 'package.json') -Raw | ConvertFrom-Json
if ($definition.name -ne 'com.code-philosophy.hybridclr' -or $definition.version -ne '8.12.0') {
    throw 'The fixture uses HybridCLR package 8.12.0.'
}
if (!(Test-Path -LiteralPath (Join-Path $native.FullName 'hybridclr/RuntimeApi.cpp') -PathType Leaf) -and
    !(Test-Path -LiteralPath (Join-Path $native.FullName 'hybridclr') -PathType Container)) {
    throw 'Supply a libil2cpp source directory with matching HybridCLR runtime sources.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validation = Join-Path $repository ('artifacts/UnityHybridClrValidation/' + [guid]::NewGuid().ToString('N'))
$project = if ($Name) { Join-Path $validation 'client/Fixture' } else { $validation }
foreach ($directory in @('Packages', 'ProjectSettings')) {
    [IO.Directory]::CreateDirectory((Join-Path $project $directory)) | Out-Null
}
Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $project 'Packages/com.code-philosophy.hybridclr') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityFixtures/HybridCLR') -Destination (Join-Path $project 'Assets') -Recurse
$dependencies = [ordered]@{}
if ($Name) {
    $sampleManifest = Get-Content -LiteralPath (Join-Path $repository 'samples/Unity/Packages/manifest.json') -Raw | ConvertFrom-Json
    foreach ($module in $sampleManifest.dependencies.PSObject.Properties | Where-Object { $_.Name.StartsWith('com.unity.modules.') }) {
        $dependencies[$module.Name] = $module.Value
    }
}
else {
    $adapter = (Join-Path $repository 'adapters/unity-hybridclr').Replace('\', '/')
    $dependencies['com.awithgameframe.unity.hybridclr'] = "file:$adapter"
}
@{ dependencies = $dependencies } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding UTF8
$version = (Get-Item -LiteralPath $EditorPath).VersionInfo.ProductVersion.Split('_')[0]
"m_EditorVersion: $version" | Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8
if ($Name) {
    $source = Join-Path $repository ('games/' + $Name)
    Copy-Item -LiteralPath (Join-Path $source 'game.json') -Destination $validation
    & (Join-Path $PSScriptRoot 'game.ps1') build -Name $Name
    & (Join-Path $PSScriptRoot 'workspace-client.ps1') -RepositoryRoot $repository -GameRoot $validation -ClientDirectory Fixture -SourceGameRoot $source
}

function Invoke-HiddenProcess([string]$Executable, [string[]]$Arguments, [string]$Log, [int]$Timeout) {
    $flags = @($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath $Executable -ArgumentList $flags -PassThru -WindowStyle Hidden
    try {
        if (!$process.WaitForExit($Timeout * 1000)) {
            $process.Kill()
            throw "HybridCLR check timed out. See $Log"
        }
        if ($process.ExitCode -ne 0) { throw "HybridCLR process returned $($process.ExitCode). See $Log" }
    }
    finally { $process.Dispose() }
}

$previousProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE')
try {
    if (!$previousProfile) { $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData') }
    Write-Host "HybridCLR 8.12.0 fixture with Unity $version at $project"
    foreach ($step in @('Prepare', 'Generate', 'Build')) {
        $log = Join-Path $project ($step + '.log')
        Invoke-HiddenProcess $editor @('-batchmode', '-nographics', '-quit', '-buildTarget', 'Win64',
            '-projectPath', $project, '-logFile', $log, '-awith-hybrid-il2cpp', $native.FullName,
            '-executeMethod', ('HybridBuild.' + $step)) $log 1200
    }
    $runtimeLog = Join-Path $project 'Player.log'
    Invoke-HiddenProcess (Join-Path $project 'Out/HybridSmoke.exe') @('-batchmode', '-nographics', '-logFile', $runtimeLog) $runtimeLog 90
    if (!(Select-String -LiteralPath $runtimeLog -SimpleMatch 'AWITH_HYBRIDCLR_PLAYER_PASSED' -Quiet)) {
        throw "HybridCLR Player assertions missing. See $runtimeLog"
    }
    Write-Host "AWITH_HYBRIDCLR_PLAYER_PASSED $runtimeLog"
}
finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }
