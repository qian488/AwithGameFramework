[CmdletBinding(DefaultParameterSetName = 'Single')]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Single')][string]$ProjectPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Pair')][string]$SourceProjectPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Pair')][string]$TargetProjectPath,
    [Parameter(ParameterSetName = 'Pair')][string]$SourceEditorPath,
    [string]$OutputPath,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity-verification-cleanup.ps1')
Set-StrictMode -Version Latest
$windows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$comparison = if ($windows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$utf8 = [Text.UTF8Encoding]::new($false)

function Resolve-PathArgument([string]$Path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Test-Within([string]$Path, [string]$Root) {
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Path.Equals($Root, $comparison) -or $Path.StartsWith($prefix, $comparison)
}

function Assert-PhysicalPath([string]$Path) {
    for ($itemPath = $Path; $itemPath; $itemPath = [IO.Path]::GetDirectoryName($itemPath)) {
        $entry = Get-Item -LiteralPath $itemPath -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked path is outside the audit copy boundary: $itemPath"
        }
    }
}

function Get-NativePath([string]$Path) {
    if (!$windows -or $Path.StartsWith('\\?\', [StringComparison]::Ordinal)) { return $Path }
    if ($Path.StartsWith('\\', [StringComparison]::Ordinal)) { return '\\?\UNC\' + $Path.Substring(2) }
    return '\\?\' + $Path
}

function Copy-PhysicalEntry([IO.FileSystemInfo]$Entry, [string]$Destination) {
    if ($Entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Linked entry is outside the audit copy boundary: $($Entry.FullName)"
    }
    if ($Entry -isnot [IO.DirectoryInfo]) {
        [IO.File]::Copy((Get-NativePath $Entry.FullName), (Get-NativePath $Destination))
        return
    }
    [IO.Directory]::CreateDirectory((Get-NativePath $Destination)) | Out-Null
    $source = [IO.DirectoryInfo]::new((Get-NativePath $Entry.FullName))
    foreach ($child in $source.EnumerateFileSystemInfos()) {
        Copy-PhysicalEntry $child (Join-Path $Destination $child.Name)
    }
}

function Get-Project([string]$Role, [string]$Path, [IO.FileInfo]$Executable) {
    $root = [IO.Path]::GetFullPath((Resolve-PathArgument $Path)).TrimEnd('\', '/')
    Assert-PhysicalPath $root
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        if (!(Test-Path -LiteralPath (Join-Path $root $folder) -PathType Container)) {
            throw "Expected a Unity project with $folder at $root"
        }
    }
    $versionFile = Join-Path $root 'ProjectSettings/ProjectVersion.txt'
    $version = [regex]::Match([IO.File]::ReadAllText($versionFile), '(?m)^m_EditorVersion:\s*(\S+)')
    if (!$version.Success) { throw "Missing m_EditorVersion in $versionFile" }
    return @{ Role = $Role; Root = $root; Editor = $Executable; Version = $version.Groups[1].Value }
}

function Get-Editor([string]$Path) {
    $entry = Get-Item -LiteralPath (Resolve-PathArgument $Path)
    if ($entry.PSIsContainer) { throw "Expected a Unity editor executable at $Path" }
    return $entry
}

function Copy-Project($Project, [string]$Destination) {
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-PhysicalEntry (Get-Item -LiteralPath (Join-Path $Project.Root $folder) -Force) (Join-Path $Destination $folder)
    }
    $manifestPath = Join-Path $Destination 'Packages/manifest.json'
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    $lockPath = Join-Path $Destination 'Packages/packages-lock.json'
    $lock = if ([IO.File]::Exists($lockPath)) { [IO.File]::ReadAllText($lockPath) | ConvertFrom-Json }
    $localPackages = @{}
    $rewritten = $false
    foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
        if (!$dependency.Value.StartsWith('file:', [StringComparison]::Ordinal)) { continue }

        $reference = $dependency.Value.Substring(5)
        $absolute = [IO.Path]::IsPathRooted($reference)
        $original = [IO.Path]::GetFullPath([IO.Path]::Combine((Join-Path $Project.Root 'Packages'), $reference))
        if (!$absolute -and (Test-Within $original $Project.Root)) {
            $relative = $original.Substring($Project.Root.Length).TrimStart('\', '/')
            if ($relative.Split('\', '/')[0] -in @('Assets', 'Packages', 'ProjectSettings')) { continue }
        }
        if (!$localPackages.ContainsKey($original)) {
            Assert-PhysicalPath $original
            if (Test-Within $Destination $original) {
                throw "A local dependency would contain its own audit copy: $original"
            }
            $entry = Get-Item -LiteralPath $original -Force
            if ($entry.PSIsContainer -and !(Test-Path -LiteralPath (Join-Path $original 'package.json') -PathType Leaf)) {
                throw "Expected package.json in local dependency $original"
            }
            $packageDirectory = Join-Path (Split-Path -Parent $Destination) ('local/' + $localPackages.Count)
            [IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
            Copy-PhysicalEntry $entry (Join-Path $packageDirectory $entry.Name)
            $localPackages[$original] = 'file:../../local/' + $localPackages.Count + '/' + $entry.Name
        }
        $dependency.Value = $localPackages[$original]
        if ($lock -and $lock.PSObject.Properties['dependencies']) {
            $locked = $lock.dependencies.PSObject.Properties[$dependency.Name]
            if ($locked -and $locked.Value.version.StartsWith('file:', [StringComparison]::Ordinal)) {
                $locked.Value.version = $dependency.Value
            }
        }
        $rewritten = $true
    }
    if ($rewritten) {
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 40), $utf8)
        if ($lock) { [IO.File]::WriteAllText($lockPath, ($lock | ConvertTo-Json -Depth 40), $utf8) }
    }

    $probeFolder = Join-Path $Destination 'Assets/AwithMigrationPreflightProbe/Editor'
    if (Test-Path -LiteralPath (Split-Path -Parent $probeFolder)) {
        throw 'The copied project already has Assets/AwithMigrationPreflightProbe.'
    }
    [IO.Directory]::CreateDirectory($probeFolder) | Out-Null
    $probeSource = Join-Path $PSScriptRoot '../adapters/unity/Editor/UnityMigrationAudit.cs'
    $probe = [IO.File]::ReadAllText($probeSource).Replace(
        'namespace AwithGameFrame.Unity.Editor', 'namespace AwithGameFrame.MigrationPreflight')
    [IO.File]::WriteAllText((Join-Path $probeFolder 'UnityMigrationAudit.cs'), $probe, $utf8)
    [IO.File]::WriteAllText((Join-Path $probeFolder 'AwithGameFrame.MigrationPreflight.asmdef'),
        '{"name":"AwithGameFrame.MigrationPreflight","includePlatforms":["Editor"],"autoReferenced":false}', $utf8)
}

function Invoke-ProjectAudit($Project, [string]$RunDirectory) {
    $roleDirectory = Join-Path $RunDirectory $Project.Role
    $stage = Join-Path $roleDirectory 'u'
    [IO.Directory]::CreateDirectory($roleDirectory) | Out-Null
    $rawPath = Join-Path $roleDirectory 'editor-report.json'
    $log = Join-Path $roleDirectory 'unity.log'
    $result = [ordered]@{
        role = $Project.Role
        projectPath = $Project.Root.Replace('\', '/')
        projectVersion = $Project.Version
        editorPath = $Project.Editor.FullName.Replace('\', '/')
        status = 'incomplete'
        editorExitCode = $null
        failure = $null
        audit = $null
    }
    try {
        Copy-Project $Project $stage
        $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $stage + '"'),
            '-executeMethod', 'AwithGameFrame.MigrationPreflight.UnityMigrationAudit.Run',
            '-awith-migration-audit-output', ('"' + $rawPath + '"'), '-logFile', ('"' + $log + '"'))
        $start = @{ FilePath = $Project.Editor.FullName; ArgumentList = $arguments; PassThru = $true }
        if ($windows) { $start.WindowStyle = 'Hidden' }
        $previousProfile = $env:ALLUSERSPROFILE
        try {
            if ($windows -and !$previousProfile) { $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData') }
            $process = Start-Process @start
        }
        finally { $env:ALLUSERSPROFILE = $previousProfile }
        try {
            if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
                $process.Kill()
                $process.WaitForExit(10000) | Out-Null
                throw "Unity batch import exceeded $TimeoutSeconds seconds."
            }
            $result.editorExitCode = $process.ExitCode
        }
        finally { $process.Dispose() }
        if ($result.editorExitCode -ne 0 -or ![IO.File]::Exists($rawPath)) {
            throw 'Unity did not complete the audit. See unity.log for import and compilation diagnostics.'
        }
        $result.audit = [IO.File]::ReadAllText($rawPath) | ConvertFrom-Json
        if ($result.audit.schemaVersion -ne 1) { throw 'Unexpected Unity audit report schema.' }
        $result.status = if ($result.audit.findings.Count) { 'findings' } else { 'passed' }
    }
    catch {
        $result.failure = $_.Exception.Message.Replace($RunDirectory, '<run>')
    }
    finally { $null = Remove-UnityVerificationDirectory $stage $RunDirectory }
    Write-Host "$($Project.Role) $($result.status). Evidence directory $roleDirectory"
    return $result
}

$editor = Get-Editor $EditorPath
$projects = @(if ($PSCmdlet.ParameterSetName -eq 'Pair') {
    $sourceEditor = if ($SourceEditorPath) { Get-Editor $SourceEditorPath } else { $editor }
    @((Get-Project 'source' $SourceProjectPath $sourceEditor), (Get-Project 'target' $TargetProjectPath $editor))
}
else { Get-Project 'project' $ProjectPath $editor })
if ($projects.Count -eq 2 -and ((Test-Within $projects[0].Root $projects[1].Root) -or (Test-Within $projects[1].Root $projects[0].Root))) {
    throw 'Source and target projects must be distinct and cannot be nested.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$run = Join-Path $repositoryRoot ('artifacts/UnityMigrationAudit/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$output = if ($OutputPath) { Resolve-PathArgument $OutputPath } else { Join-Path $run 'migration-audit.json' }
Assert-PhysicalPath $run
Assert-PhysicalPath $output
foreach ($project in $projects) {
    if ((Test-Within $run $project.Root) -or (Test-Within $output $project.Root)) {
        throw "Audit output and staging must be outside $($project.Root)"
    }
}
$results = @(foreach ($project in $projects) { Invoke-ProjectAudit $project $run })
$incomplete = @($results | Where-Object { $_.status -eq 'incomplete' }).Count -ne 0
$findings = @($results | Where-Object { $_.status -eq 'findings' }).Count -ne 0
$report = [ordered]@{
    schemaVersion = 1
    mode = if ($projects.Count -eq 2) { 'source-target' } else { 'single' }
    passed = !$incomplete -and !$findings
    projects = $results
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
$temporaryReport = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporaryReport, (($report | ConvertTo-Json -Depth 12) + [char]10), $utf8)
    if ([IO.File]::Exists($output)) { [IO.File]::Replace($temporaryReport, $output, [NullString]::Value) }
    else { [IO.File]::Move($temporaryReport, $output) }
}
finally { [IO.File]::Delete($temporaryReport) }
Write-Host "Unity migration audit report $output"
if ($incomplete) { exit 1 }
if ($findings) { exit 2 }
