[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = New-Object System.Text.UTF8Encoding($false)
$ownershipFile = '.awith-workspace.json'

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Get-FullChildPath([string]$Parent, [string]$Relative) {
    if ([System.IO.Path]::IsPathRooted($Relative)) { throw "Expected a relative path: $Relative" }
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd([char[]]@('\', '/'))
    $childPath = [System.IO.Path]::GetFullPath((Join-Path $parentPath $Relative))
    if (-not $childPath.StartsWith($parentPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes its owner: $Relative"
    }
    return $childPath
}

function Assert-NoLinks([string]$Path) {
    $current = [System.IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Linked paths are not supported for generated output: $current"
            }
        }
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-RelativeFilePath([string]$Root, [string]$Path) {
    return $Path.Substring($Root.TrimEnd([char[]]@('\', '/')).Length + 1).Replace('\', '/')
}

function Get-OutputFiles([string]$Directory) {
    $result = @()
    foreach ($item in @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked generated entry: $($item.FullName)" }
        if (-not $item.PSIsContainer) { $result += $item }
    }
    return $result
}

function Assert-OwnedOutput([string]$Directory, [string]$Name, [string]$Engine) {
    Assert-NoLinks $Directory
    if (-not (Test-Path -LiteralPath $Directory)) { return }
    $markerPath = Join-Path $Directory $ownershipFile
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Output already exists without a workspace ownership manifest; preserve it and choose another location: $Directory"
    }
    $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($marker.schemaVersion -ne 1 -or $marker.game -cne $Name -or $marker.engine -cne $Engine) {
        throw "Output belongs to another game, engine or format: $Directory"
    }
    $expected = @{}
    foreach ($entry in @($marker.files)) {
        $path = Get-FullChildPath $Directory $entry.path
        $relative = Get-RelativeFilePath $Directory $path
        if ($relative -eq $ownershipFile -or $expected.ContainsKey($relative)) { throw "Invalid ownership manifest: $markerPath" }
        $expected[$relative] = $entry.sha256
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) {
            throw "Generated file was changed or removed; preserve your changes before syncing: $path"
        }
    }
    foreach ($file in @(Get-OutputFiles $Directory)) {
        $relative = Get-RelativeFilePath $Directory $file.FullName
        if ($relative -ne $ownershipFile -and -not $expected.ContainsKey($relative)) {
            throw "Unowned file in generated output; preserve it before syncing: $($file.FullName)"
        }
    }
}

function Complete-OwnedOutput([string]$Stage, [string]$Destination, [string]$Name, [string]$Engine) {
    $entries = @()
    foreach ($file in @(Get-OutputFiles $Stage | Sort-Object FullName)) {
        $entries += [ordered]@{ path = (Get-RelativeFilePath $Stage $file.FullName); sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    Write-Utf8 (Join-Path $Stage $ownershipFile) (([ordered]@{ schemaVersion = 1; game = $Name; engine = $Engine; files = @($entries) } | ConvertTo-Json -Depth 8) + "`n")
    # Recheck immediately before replacing output; only the manifest-owned directory can be removed.
    Assert-OwnedOutput $Destination $Name $Engine
    $destinationParent = Split-Path -Parent $Destination
    Assert-NoLinks $destinationParent
    [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    $backup = $null
    if (Test-Path -LiteralPath $Destination) {
        # Keep backups in artifacts to avoid lengthening already nested client paths on Windows.
        $backup = Get-FullChildPath $buildRoot ('client-backup-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $Destination -Destination $backup
    }
    try {
        Move-Item -LiteralPath $Stage -Destination $Destination
    }
    catch {
        if ($backup -and -not (Test-Path -LiteralPath $Destination)) { Move-Item -LiteralPath $backup -Destination $Destination }
        throw
    }
    if ($backup) {
        # Both paths are absolute; the backup is confined to this game's artifact directory.
        if (-not $backup.StartsWith($buildRoot + [System.IO.Path]::DirectorySeparatorChar + 'client-backup-', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup path.' }
        Assert-OwnedOutput $backup $Name $Engine
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}

function New-UnityMeta([string]$Directory, [string]$RelativePath, [bool]$Folder) {
    $path = Join-Path $Directory ($RelativePath + '.meta')
    if (Test-Path -LiteralPath $path) { return }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash($utf8.GetBytes('awith-workspace/' + $game.name + '/' + $RelativePath.Replace('\', '/'))) }
    finally { $sha.Dispose() }
    $guid = ([System.BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()).Substring(0, 32)
    $text = "fileFormatVersion: 2`nguid: $guid`n"
    if ($Folder) { $text += "folderAsset: yes`n" }
    if ([System.IO.Path]::GetExtension($RelativePath) -eq '.dll') {
        $text += @'
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'@
    }
    else {
        $text += "DefaultImporter:`n  externalObjects: {}`n  userData:`n  assetBundleName:`n  assetBundleVariant:`n"
    }
    Write-Utf8 $path ($text.TrimEnd() + "`n")
}

function Copy-PortableAssemblies([string]$Destination, [bool]$ForUnity) {
    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($file in @(Get-ChildItem -LiteralPath $portable -File | Where-Object { $_.Extension -in @('.dll', '.pdb') })) {
        if ($ForUnity -and $file.BaseName -eq 'Newtonsoft.Json') { continue }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name)
    }
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([char[]]@('\', '/'))
$GameRoot = [System.IO.Path]::GetFullPath($GameRoot).TrimEnd([char[]]@('\', '/'))
if (-not $GameRoot.StartsWith($RepositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'GameRoot must be inside RepositoryRoot.'
}
Assert-NoLinks $GameRoot
$game = Get-Content -LiteralPath (Join-Path $GameRoot 'game.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($game.schemaVersion -ne 1 -or $game.name -cnotmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $game.engine -cnotin @('unity', 'godot')) {
    throw 'Invalid game.json: expected schemaVersion 1, a C# identifier name and unity/godot engine.'
}
$buildRoot = Get-FullChildPath $RepositoryRoot ('artifacts/games/' + $game.name + '/' + $Configuration)
$portable = Join-Path $buildRoot 'portable'
Assert-NoLinks $buildRoot
Assert-NoLinks $portable
$buildManifest = Join-Path $portable 'build.json'
if (-not (Test-Path -LiteralPath $buildManifest -PathType Leaf)) { throw 'Build manifest is missing. Build the game before syncing.' }
$build = Get-Content -LiteralPath $buildManifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.name -cne $game.name -or $build.configuration -cne $Configuration -or $build.applicationProject -cne $game.applicationProject) {
    throw 'Portable build manifest does not match the selected game/configuration. Rebuild before syncing.'
}
$builtFiles = @{}
foreach ($entry in @($build.files)) {
    $file = Get-FullChildPath $portable $entry.path
    if ([System.IO.Path]::GetFileName($entry.path) -cne $entry.path -or [System.IO.Path]::GetExtension($entry.path) -notin @('.dll', '.pdb') -or $builtFiles.ContainsKey($entry.path)) {
        throw 'Invalid portable build file list.'
    }
    $builtFiles[$entry.path] = $entry.sha256
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "Portable build output was changed or removed: $file. Rebuild before syncing."
    }
}
foreach ($file in @(Get-OutputFiles $portable)) {
    if ($file.Name -ne 'build.json' -and -not $builtFiles.ContainsKey($file.Name)) { throw "Unlisted portable build output: $($file.FullName). Rebuild before syncing." }
}
foreach ($assembly in @(($game.name + '.Application'), ($game.name + '.Domain'), 'AwithGameFrame.Core', 'AwithGameFrame.Data', 'AwithGameFrame.Gameplay')) {
    if (-not (Test-Path -LiteralPath (Join-Path $portable ($assembly + '.dll')) -PathType Leaf)) {
        throw "Missing portable assembly $assembly. Build the game before syncing."
    }
}

$clientName = if ($game.engine -eq 'unity') { 'Unity' } else { 'Godot' }
$client = Get-FullChildPath $GameRoot ('client/' + $clientName)
Assert-NoLinks $client
if ($game.engine -eq 'unity') {
    foreach ($entry in @('Assets', 'ProjectSettings/ProjectVersion.txt', 'Packages/manifest.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $client $entry))) { throw "Create your Unity project at $client before syncing (missing $entry)." }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $client 'Packages/manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
        if ($dependency.Name -like 'com.awithgameframe.*' -and $dependency.Name -ne 'com.awithgameframe.workspace') {
            throw "Source packages and workspace DLLs cannot be mixed. Remove $($dependency.Name) from the Unity project before syncing. Optional legacy/plugin packages require a separate integration."
        }
    }
    foreach ($directory in @(Get-ChildItem -LiteralPath (Join-Path $client 'Packages') -Directory -Force)) {
        $packagePath = Join-Path $directory.FullName 'package.json'
        if ($directory.Name -eq 'com.awithgameframe.workspace' -or -not (Test-Path -LiteralPath $packagePath)) { continue }
        $packageName = (Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json).name
        if ($packageName -like 'com.awithgameframe.*') { throw "Remove the duplicate embedded framework package before syncing: $($directory.FullName)" }
    }
    $destination = Get-FullChildPath $client 'Packages/com.awithgameframe.workspace'
}
else {
    if (-not (Test-Path -LiteralPath (Join-Path $client 'project.godot') -PathType Leaf) -or @(Get-ChildItem -LiteralPath $client -Filter '*.csproj' -File -ErrorAction SilentlyContinue).Count -ne 1) {
        throw "Create your Godot .NET project at $client before syncing (requires project.godot and one root .csproj)."
    }
    $destination = Get-FullChildPath $client 'Framework'
}
Assert-OwnedOutput $destination $game.name $game.engine
$stage = Get-FullChildPath $buildRoot ('client-stage-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    if ($game.engine -eq 'unity') {
        Copy-PortableAssemblies (Join-Path $stage 'Plugins') $true
        foreach ($source in @('Runtime', 'Editor')) {
            $sourcePath = Get-FullChildPath (Join-Path $RepositoryRoot 'adapters/unity') $source
            Assert-NoLinks $sourcePath
            foreach ($item in @(Get-ChildItem -LiteralPath $sourcePath -Recurse -Force)) {
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked adapter source: $($item.FullName)" }
            }
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $stage $source) -Recurse
            $sourceMeta = $sourcePath + '.meta'
            if (Test-Path -LiteralPath $sourceMeta) { Copy-Item -LiteralPath $sourceMeta -Destination (Join-Path $stage ($source + '.meta')) }
        }
        # Engine adapters depend on framework assemblies, never on a concrete game's DLLs.
        $dllNames = @('AwithGameFrame.Core.dll', 'AwithGameFrame.Data.dll', 'AwithGameFrame.Gameplay.dll', 'AwithGameFrame.Networking.dll' |
            Where-Object { Test-Path -LiteralPath (Join-Path (Join-Path $stage 'Plugins') $_) })
        foreach ($definition in @(Get-ChildItem -LiteralPath $stage -Filter '*.asmdef' -File -Recurse)) {
            $asmdef = Get-Content -LiteralPath $definition.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $references = @($asmdef.references | Where-Object { $_ -notin @('AwithGameFrame.Core', 'AwithGameFrame.Data', 'AwithGameFrame.Gameplay', 'AwithGameFrame.Networking') })
            $asmdef | Add-Member -NotePropertyName references -NotePropertyValue $references -Force
            $asmdef | Add-Member -NotePropertyName overrideReferences -NotePropertyValue $true -Force
            $asmdef | Add-Member -NotePropertyName precompiledReferences -NotePropertyValue $dllNames -Force
            Write-Utf8 $definition.FullName (($asmdef | ConvertTo-Json -Depth 10) + "`n")
        }
        $package = [ordered]@{
            name = 'com.awithgameframe.workspace'; version = '0.1.0'; displayName = ($game.name + ' Framework Integration')
            description = 'Generated framework and game DLLs with modern Unity adapter sources. Edit external sources and sync again.'
            unity = '2022.3'
            dependencies = [ordered]@{ 'com.unity.ugui' = '1.0.0'; 'com.unity.nuget.newtonsoft-json' = '3.2.1' }
        }
        Write-Utf8 (Join-Path $stage 'package.json') (($package | ConvertTo-Json -Depth 5) + "`n")
        foreach ($item in @(Get-ChildItem -LiteralPath $stage -Recurse -Force | Sort-Object FullName)) {
            if (-not $item.PSIsContainer -and $item.Extension -eq '.meta') { continue }
            New-UnityMeta $stage (Get-RelativeFilePath $stage $item.FullName) $item.PSIsContainer
        }
    }
    else {
        $adapterOutput = Get-FullChildPath $buildRoot 'godot'
        Assert-NoLinks $adapterOutput
        & dotnet build (Join-Path $RepositoryRoot 'adapters/godot/AwithGameFrame.Godot.csproj') --configuration $Configuration --output $adapterOutput --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Godot adapter build failed; client output was preserved.' }
        Copy-PortableAssemblies $stage $false
        foreach ($extension in @('.dll', '.pdb')) {
            $file = Join-Path $adapterOutput ('AwithGameFrame.Godot' + $extension)
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing adapter output: $file" }
            Copy-Item -LiteralPath $file -Destination $stage
        }
        $project = New-Object System.Xml.XmlDocument
        $rootElement = $project.CreateElement('Project')
        $project.AppendChild($rootElement) | Out-Null
        $group = $project.CreateElement('ItemGroup')
        $rootElement.AppendChild($group) | Out-Null
        foreach ($dll in @(Get-ChildItem -LiteralPath $stage -Filter '*.dll' -File | Sort-Object Name)) {
            $reference = $project.CreateElement('Reference')
            $reference.SetAttribute('Include', $dll.BaseName)
            $hint = $project.CreateElement('HintPath')
            $hint.InnerText = '$(MSBuildThisFileDirectory)' + $dll.Name
            $reference.AppendChild($hint) | Out-Null
            $private = $project.CreateElement('Private')
            $private.InnerText = 'true'
            $reference.AppendChild($private) | Out-Null
            $group.AppendChild($reference) | Out-Null
        }
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Encoding = $utf8
        $settings.Indent = $true
        $writer = [System.Xml.XmlWriter]::Create((Join-Path $stage 'AwithGameFrame.Workspace.props'), $settings)
        try { $project.Save($writer) } finally { $writer.Dispose() }
    }
    Complete-OwnedOutput $stage $destination $game.name $game.engine
    Write-Host "Synced $($game.name) $Configuration to $destination"
    if ($game.engine -eq 'godot') {
        Write-Host 'Add this once inside your Godot project .csproj: <Import Project="Framework/AwithGameFrame.Workspace.props" />'
    }
    else {
        Write-Host 'Use an application asmdef referencing AwithGameFrame.Unity and AwithGameFrame.Extensions for engine glue. The generated package owns its DLLs and adapter copies.'
    }
}
finally {
    if (Test-Path -LiteralPath $stage) {
        if (-not $stage.StartsWith($buildRoot + [System.IO.Path]::DirectorySeparatorChar + 'client-stage-', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path.' }
        Assert-NoLinks $stage
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
