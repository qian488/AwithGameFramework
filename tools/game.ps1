[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('new', 'paths', 'new-module', 'config', 'protocol', 'build', 'sync', 'run-server')]
    [string]$Command,
    [Parameter(Mandatory = $true)][string]$Name,
    [ValidateSet('unity', 'godot')][string]$Engine,
    [string]$Module,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateRange(0, 86400)][double]$RunForSeconds = 0,
    [string]$ServerConfig,
    [switch]$Check,
    [string]$ClientGameRoot,
    [string]$ClientDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = New-Object System.Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot 'build-identity.ps1')

function Assert-Identifier([string]$Value) {
    $keywords = @('abstract','as','base','bool','break','byte','case','catch','char','checked','class',
        'const','continue','decimal','default','delegate','do','double','else','enum','event','explicit',
        'extern','false','finally','fixed','float','for','foreach','goto','if','implicit','in','int',
        'interface','internal','is','lock','long','namespace','new','null','object','operator','out',
        'override','params','private','protected','public','readonly','ref','return','sbyte','sealed',
        'short','sizeof','stackalloc','static','string','struct','switch','this','throw','true','try',
        'typeof','uint','ulong','unchecked','unsafe','ushort','using','virtual','void','volatile','while')
    if ($Value -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,39}$' -or $keywords -ccontains $Value -or
        $Value -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$') {
        throw "Use a C# identifier of 1-40 ASCII letters/digits/underscores starting with a letter; reserved names are unsupported: $Value"
    }
}

function Assert-NoReparsePoint([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current -and $current.Length -ge $repositoryRoot.Length) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Workspace paths must not traverse a symlink/junction: $current"
        }
        if ($current -eq $repositoryRoot) { break }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Enter-WorkspaceBuildLock {
    $path = Join-Path $repositoryRoot 'artifacts/workspace-build.lock'
    Assert-NoReparsePoint $path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $logged = $false
    while ($true) {
        try {
            return [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch [IO.IOException] {
            if ($elapsed.Elapsed.TotalMinutes -ge 15) {
                throw [TimeoutException]::new("Timed out waiting for the workspace build lock at $path.", $_.Exception)
            }
            if (!$logged) {
                Write-Host "Waiting for another workspace build or client sync in $repositoryRoot"
                $logged = $true
            }
            Start-Sleep -Milliseconds 200
        }
    }
}

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}

function Show-Paths($Game) {
    $engineDirectory = if ($Game.engine -eq 'unity') { 'Unity' } else { 'Godot' }
    Write-Host "Game source: $gameRoot/src"
    Write-Host "Server:      $gameRoot/server"
    Write-Host "Create your $engineDirectory project at: $gameRoot/client/$engineDirectory"
    Write-Host 'The tool does not create an engine project. Install Spine/TMP and other engine packages there yourself.'
    Write-Host "Build output: $repositoryRoot/artifacts/games/$Name/$Configuration"
}

function Read-Game {
    $path = Join-Path $gameRoot 'game.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Game not found: $path. Use game.ps1 new first." }
    $game = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($game.schemaVersion -ne 1 -or $game.name -cne $Name -or $game.engine -cnotin @('unity','godot') -or
        $game.applicationProject -cne "src/$Name.Application/$Name.Application.csproj" -or
        $game.serverProject -cne "server/$Name.Server/$Name.Server.csproj") {
        throw "Invalid game.json; workspace name, engine or project paths do not match the supported layout: $path"
    }
    return $game
}

function Get-ConfigurationCopyPath([string]$Relative, [string]$Prefix) {
    if (-not $Relative.StartsWith($Prefix, [StringComparison]::Ordinal) -or
        $Relative -notmatch '^[A-Za-z0-9_. -]+(/[A-Za-z0-9_. -]+)+$' -or
        $Relative -match '(^|/)[. ]+(/|$)|[. ](/|$)') {
        throw "Expected a path under $Prefix in configuration/copies.json, got '$Relative'."
    }
    $path = Join-Path $gameRoot $Relative
    Assert-NoReparsePoint $path
    return $path
}

function Get-ConfigurationDifferenceLine([byte[]]$Source, [byte[]]$Copy) {
    $limit = [Math]::Min($Source.Length, $Copy.Length)
    $line = 1
    for ($index = 0; $index -lt $limit -and $Source[$index] -eq $Copy[$index]; $index++) {
        if ($Source[$index] -eq 10 -or ($Source[$index] -eq 13 -and
            ($index + 1 -eq $Source.Length -or $Source[$index + 1] -ne 10))) {
            $line++
        }
    }
    if ($index -eq $limit -and $Source.Length -eq $Copy.Length) { return 0 }
    return $line
}

function Invoke-GameConfigurationCopies($Game, [bool]$Verify) {
    $manifest = Join-Path $gameRoot 'configuration/copies.json'
    Assert-NoReparsePoint $manifest
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { return }

    $clientPrefix = if ($Game.engine -eq 'unity') { 'client/Unity/Assets/' } else { 'client/Godot/' }
    $destinations = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $copies = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
    $bindings = foreach ($binding in $copies) {
        if (-not $binding -or -not $binding.PSObject.Properties['source'] -or
            -not $binding.PSObject.Properties['destination'] -or
            $binding.source -isnot [string] -or $binding.destination -isnot [string]) {
            throw "Expected source and destination paths in $manifest."
        }
        $source = Get-ConfigurationCopyPath $binding.source 'configuration/'
        $destination = Get-ConfigurationCopyPath $binding.destination $clientPrefix
        $extension = [IO.Path]::GetExtension($source)
        if ($extension -notin @('.csv', '.json') -or $extension -ne [IO.Path]::GetExtension($destination)) {
            throw "Expected matching CSV or JSON extensions in $manifest for '$($binding.source)' and '$($binding.destination)'."
        }
        if (-not $destinations.Add($destination)) { throw "Duplicate destination '$($binding.destination)' in $manifest." }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "$($binding.source):1: canonical configuration file is missing."
        }
        if (Test-Path -LiteralPath $destination -PathType Container) {
            throw "$($binding.destination):1: expected a configuration file."
        }
        @{ Source = $source; Destination = $destination; Paths = $binding }
    }

    $diagnostics = foreach ($binding in $bindings) {
        $bytes = [IO.File]::ReadAllBytes($binding.Source)
        $exists = Test-Path -LiteralPath $binding.Destination -PathType Leaf
        if ($exists) {
            $line = Get-ConfigurationDifferenceLine $bytes ([IO.File]::ReadAllBytes($binding.Destination))
            if ($line -eq 0) { continue }
        }
        else { $line = 1 }

        if ($Verify) {
            $reason = if ($exists) { 'differs from' } else { 'is missing, expected a copy of' }
            "$($binding.Paths.destination):$($line): $reason $($binding.Paths.source)"
            continue
        }

        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($binding.Destination)) | Out-Null
        $temporary = Join-Path $gameRoot (".configuration-copy-$([Guid]::NewGuid().ToString('N')).tmp")
        try {
            [IO.File]::WriteAllBytes($temporary, $bytes)
            if ($exists) { [IO.File]::Replace($temporary, $binding.Destination, [NullString]::Value) }
            else { [IO.File]::Move($temporary, $binding.Destination) }
        }
        finally {
            if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        }
        Write-Host "Updated configuration copy: $($binding.Paths.destination)"
    }
    if ($diagnostics) {
        throw (($diagnostics -join [Environment]::NewLine) + [Environment]::NewLine +
            "Run game.ps1 config -Name $Name to refresh configuration copies.")
    }
    Write-Host "Configuration copies verified: $(@($copies).Count) files."
}

function Test-GameConfiguration {
    return ((Test-Path -LiteralPath (Join-Path $gameRoot 'configuration/schema.json')) -or
        (Test-Path -LiteralPath (Join-Path $gameRoot 'configuration/copies.json')))
}

function Invoke-GameConfiguration($Game, [bool]$Verify) {
    $source = Join-Path $gameRoot 'configuration'
    $schema = Join-Path $source 'schema.json'
    if (-not (Test-Path -LiteralPath $schema) -and
        (Test-Path -LiteralPath (Join-Path $source 'copies.json'))) {
        Invoke-GameConfigurationCopies $Game $Verify
        return
    }
    $data = Join-Path $source 'Data'
    $application = Split-Path -Parent (Join-Path $gameRoot $Game.applicationProject)
    $output = Join-Path $application 'Runtime/Generated/Configuration'
    foreach ($path in @($schema, $data, $output)) { Assert-NoReparsePoint $path }
    if (-not (Test-Path -LiteralPath $schema -PathType Leaf) -or -not (Test-Path -LiteralPath $data -PathType Container)) {
        throw "Game configuration needs configuration/schema.json and configuration/Data: $gameRoot"
    }
    foreach ($directory in @($data, $output)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Game configuration directories must contain only regular files: $($item.FullName)"
            }
        }
    }
    $arguments = @('run', '--project', (Join-Path $PSScriptRoot 'ConfigGen/AwithGameFrame.ConfigGen.csproj'),
        '--configuration', 'Release', '--', $schema, $data, $output)
    if ($Verify) { $arguments += '--check' }
    try { Invoke-DotNet $arguments }
    catch { throw "Configuration for $Name failed. After updating source data, run game.ps1 config -Name $Name. $($_.Exception.Message)" }
    Invoke-GameConfigurationCopies $Game $Verify
}

function Invoke-GameProtocol($Game, [bool]$Verify) {
    $source = Join-Path $gameRoot 'protocol'
    $schema = Join-Path $source 'schema.json'
    $baseline = Join-Path $source 'baseline.json'
    $protobuf = Join-Path $source 'protobuf'
    $application = Split-Path -Parent (Join-Path $gameRoot $Game.applicationProject)
    $usesProtobuf = Test-Path -LiteralPath (Join-Path $protobuf 'buf.yaml') -PathType Leaf
    if ($usesProtobuf) {
        foreach ($path in @($protobuf, (Join-Path $source 'baseline.binpb'), (Join-Path $application 'Runtime/Generated/Protobuf'))) {
            Assert-NoReparsePoint $path
        }
        $options = @{
            SourceDirectory = $protobuf
            BaselinePath = (Join-Path $source 'baseline.binpb')
            OutputDirectory = (Join-Path $application 'Runtime/Generated/Protobuf')
            Check = $Verify
        }
        if ($env:AWITH_BUF) { $options.Buf = $env:AWITH_BUF }
        if ($env:AWITH_PROTOC) { $options.Protoc = $env:AWITH_PROTOC }
        & (Join-Path $PSScriptRoot 'generate-protobuf.ps1') @options
        if (!(Test-Path -LiteralPath $schema -PathType Leaf)) { return }
    }
    $output = Join-Path $application 'Runtime/Generated/Protocol/GameProtocol.cs'
    foreach ($path in @($schema, $baseline, $output)) { Assert-NoReparsePoint $path }
    if (-not (Test-Path -LiteralPath $schema -PathType Leaf) -or -not (Test-Path -LiteralPath $baseline -PathType Leaf)) {
        throw "Game protocol needs protocol/schema.json and protocol/baseline.json: $gameRoot"
    }
    $arguments = @('run', '--project', (Join-Path $PSScriptRoot 'ConfigGen/AwithGameFrame.ConfigGen.csproj'),
        '--configuration', 'Release', '--', '--protocol', $schema, $baseline, $output)
    if ($Verify) { $arguments += '--check' }
    try { Invoke-DotNet $arguments }
    catch { throw "Protocol generation for $Name failed. After updating the source, run game.ps1 protocol -Name $Name. $($_.Exception.Message)" }
}

# This is a dependency check, not an OS sandbox. Native MSBuild still executes with the caller's permissions.
function Assert-PortableProjects([string]$Project) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $projectNames = @{}
    $assemblyNames = @{}
    $pending.Push($Project)
    while ($pending.Count -gt 0) {
        $path = [IO.Path]::GetFullPath($pending.Pop())
        if (-not $seen.Add($path)) { continue }
        $gameSource = [IO.Path]::GetFullPath((Join-Path $gameRoot 'src')) + [IO.Path]::DirectorySeparatorChar
        $frameworkSource = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src')) + [IO.Path]::DirectorySeparatorChar
        if (-not ($path.StartsWith($gameSource, [StringComparison]::OrdinalIgnoreCase) -or
            $path.StartsWith($frameworkSource, [StringComparison]::OrdinalIgnoreCase))) {
            throw "Portable ProjectReference must stay in this game's src or framework src: $path"
        }
        Assert-NoReparsePoint $path
        [xml]$projectXml = [IO.File]::ReadAllText($path)
        $projectName = [IO.Path]::GetFileNameWithoutExtension($path)
        $assemblyNodes = @($projectXml.SelectNodes('//AssemblyName'))
        $assemblyName = if ($assemblyNodes.Count -eq 0) { $projectName } else { $assemblyNodes[0].InnerText }
        if ($projectName -ieq "$Name.Server" -or $assemblyName -ieq "$Name.Server" -or
            $projectNames.ContainsKey($projectName) -or $assemblyNames.ContainsKey($assemblyName)) {
            throw "Duplicate portable/server project or assembly name: $path"
        }
        $projectNames[$projectName] = $path
        $assemblyNames[$assemblyName] = $path
        $target = @($projectXml.SelectNodes('//TargetFramework'))
        $compile = @($projectXml.SelectNodes('//Compile'))
        if ($target.Count -ne 1 -or $target[0].InnerText -ne 'netstandard2.1' -or
            @($projectXml.SelectNodes('//EnableDefaultCompileItems[text()="false"]')).Count -ne 1 -or
            $compile.Count -ne 1 -or $compile[0].GetAttribute('Include') -cne 'Runtime/**/*.cs' -or
            $compile[0].Attributes.Count -ne 1) {
            throw "Portable projects must target netstandard2.1 and compile only Runtime/**/*.cs: $path"
        }
        foreach ($reference in $projectXml.SelectNodes('//ProjectReference')) {
            $pending.Push([IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $reference.GetAttribute('Include'))))
        }
        foreach ($reference in $projectXml.SelectNodes('//Reference | //PackageReference | //FrameworkReference')) {
            if ($reference.GetAttribute('Include') -match '(?i)Unity|Godot|UniTask|Cysharp') {
                throw "Engine dependency in portable project: $path"
            }
        }
        foreach ($hint in $projectXml.SelectNodes('//HintPath')) {
            if ($hint.InnerText -match '(?i)(^|[\\/])(adapters|samples|client)([\\/]|$)') {
                throw "Portable assembly reference points to a host: $path"
            }
        }
        $runtime = Join-Path ([IO.Path]::GetDirectoryName($path)) 'Runtime'
        foreach ($source in @(Get-ChildItem -LiteralPath $runtime -Recurse -File -Filter '*.cs')) {
            # Ignore literals/comments, as the repository-wide portable check does.
            $code = [regex]::Replace([IO.File]::ReadAllText($source.FullName), '(?s)/\*.*?\*/|//[^\r\n]*|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''', ' ')
            if ($code -match '\b(UnityEngine|UnityEditor|Godot|UniTask|Cysharp\.Threading\.Tasks)\b') {
                throw "Engine dependency in portable source: $($source.FullName)"
            }
        }
    }
}

function Invoke-GameBuild($game) {
    if (Test-GameConfiguration) {
        Invoke-GameConfiguration $game $true
    }
    if ((Test-Path -LiteralPath (Join-Path $gameRoot 'protocol/schema.json')) -or
        (Test-Path -LiteralPath (Join-Path $gameRoot 'protocol/protobuf/buf.yaml'))) {
        Invoke-GameProtocol $game $true
    }
    $applicationPath = Join-Path $gameRoot $game.applicationProject
    Assert-PortableProjects $applicationPath
    $outputRoot = Join-Path $repositoryRoot "artifacts/games/$Name/$Configuration"
    $stage = Join-Path $outputRoot ('portable-stage-' + [Guid]::NewGuid().ToString('N'))
    $portable = Join-Path $outputRoot 'portable'
    Assert-NoReparsePoint $outputRoot
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    try {
        Invoke-DotNet @('build', $applicationPath, '--configuration', $Configuration, '--output', $stage,
            '--nologo', '-p:CopyLocalLockFileAssemblies=true')
        Invoke-DotNet @('build', (Join-Path $gameRoot $game.serverProject), '--configuration', $Configuration, '--nologo')
        if (@(Get-ChildItem -LiteralPath $stage -Directory -Force).Count -gt 0) {
            throw 'Client distribution currently supports flat managed DLL/PDB dependencies. Native runtimes and satellite/resource directories need explicit engine integration.'
        }
        foreach ($file in @(Get-ChildItem -LiteralPath $stage -File)) {
            if ($file.Extension -notin @('.dll', '.pdb')) {
                if ($file.Name -notlike '*.deps.json' -and $file.Extension -ne '.xml') {
                    throw "Unsupported client build content: $($file.Name). Keep engine content in the client or define an explicit content integration."
                }
                Remove-Item -LiteralPath $file.FullName -Force
            }
        }
        foreach ($required in @("$Name.Application.dll", "$Name.Domain.dll", 'AwithGameFrame.Core.dll',
            'AwithGameFrame.Data.dll', 'AwithGameFrame.Gameplay.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Missing dependency output: $required" }
        }
        $files = @(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {
            [ordered]@{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        $identity = Get-BuildIdentity $repositoryRoot $gameRoot $game
        $build = [ordered]@{ schemaVersion = 1; name = $Name; configuration = $Configuration
            applicationProject = $game.applicationProject; sourceCommit = $identity.sourceCommit
            sourceDirty = $identity.sourceDirty; sourceFingerprint = $identity.sourceFingerprint
            targetFramework = $identity.targetFramework; engineVersion = $identity.engineVersion; files = $files }
        Write-Utf8 (Join-Path $stage 'build.json') (($build | ConvertTo-Json -Depth 6) + "`n")
        # A successful complete build replaces the old set, so removed dependencies cannot linger.
        $backup = Join-Path $outputRoot ('portable-previous-' + [Guid]::NewGuid().ToString('N'))
        if (Test-Path -LiteralPath $portable) {
            Assert-NoReparsePoint $portable
            [IO.Directory]::Move($portable, $backup)
        }
        try { [IO.Directory]::Move($stage, $portable) }
        catch {
            if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $portable)) {
                [IO.Directory]::Move($backup, $portable)
            }
            throw
        }
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
        Write-Host "Built framework/game DLLs and symbols: $portable"
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}

Assert-Identifier $Name
if ($Check -and $Command -notin @('config', 'protocol')) { throw '-Check applies to the config or protocol command.' }
if (($ClientGameRoot -or $ClientDirectory) -and ($Command -ne 'sync' -or !$ClientGameRoot -or !$ClientDirectory)) {
    throw 'Use -ClientGameRoot and -ClientDirectory together with sync for an isolated client.'
}
$gameRoot = Join-Path $repositoryRoot "games/$Name"
Assert-NoReparsePoint $gameRoot

switch ($Command) {
    'new' {
        if (-not $Engine) { throw 'new requires -Engine unity or godot.' }
        if (Test-Path -LiteralPath $gameRoot) { throw "Refusing to overwrite an existing game: $gameRoot" }
        $templateRoot = Join-Path $repositoryRoot 'templates/game-workspace'
        $stage = Join-Path $repositoryRoot ('artifacts/workspace-new-' + [Guid]::NewGuid().ToString('N'))
        Assert-NoReparsePoint $stage
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        try {
            foreach ($source in @(Get-ChildItem -LiteralPath $templateRoot -Recurse -File -Force)) {
                $relative = $source.FullName.Substring($templateRoot.Length + 1).Replace('__GAME__', $Name)
                $destination = Join-Path $stage $relative
                $text = [IO.File]::ReadAllText($source.FullName).Replace('__GAME__', $Name)
                Write-Utf8 $destination $text
            }
            $game = [ordered]@{ schemaVersion = 1; name = $Name; engine = $Engine
                applicationProject = "src/$Name.Application/$Name.Application.csproj"
                serverProject = "server/$Name.Server/$Name.Server.csproj" }
            Write-Utf8 (Join-Path $stage 'game.json') (($game | ConvertTo-Json) + "`n")
            $moduleDescriptor = [ordered]@{ schemaVersion = 1; id = 'game-' + $Name.ToLowerInvariant().Replace('_', '-')
                purpose = "Engine-independent game code and server composition for $Name."
                sourcePaths = @("games/$Name/src", "games/$Name/server", "games/$Name/configuration", "games/$Name/protocol")
                entryPoints = @("games/$Name/game.json", "games/$Name/src/$Name.Application/$Name.Application.csproj", "games/$Name/configuration/README.md", "games/$Name/protocol/README.md")
                readme = "games/$Name/README.md"; dependencies = @('core','data','gameplay','networking')
                verificationTools = @('game-build') }
            Write-Utf8 (Join-Path $stage 'module.json') (($moduleDescriptor | ConvertTo-Json -Depth 6) + "`n")
            Write-Utf8 (Join-Path $stage 'configuration/README.md') "# $Name configuration`n`nAdd schema.json and a flat Data directory here. Run game.ps1 config -Name $Name to generate and -Check to verify the outputs.`n"
            Write-Utf8 (Join-Path $stage 'protocol/README.md') "# $Name protocol`n`nKeep editable protocol sources and the compatibility baseline here, outside the engine project. Add schema.json and baseline.json, then run:`n`npowershell -File tools/game.ps1 protocol -Name $Name`npowershell -File tools/game.ps1 protocol -Name $Name -Check`n`nThe generated GameProtocol.cs is written to the Application Runtime/Generated/Protocol directory. Do not edit generated output.`n"
            [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'games')) | Out-Null
            [IO.Directory]::Move($stage, $gameRoot)
            Write-Host "Created external C# workspace: $gameRoot"
            Show-Paths $game
        } finally {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        }
    }
    'paths' { Show-Paths (Read-Game) }
    'new-module' {
        $game = Read-Game
        Assert-Identifier $Module
        $moduleName = "$Name.$Module"
        $moduleRoot = Join-Path $gameRoot "src/$moduleName"
        Assert-NoReparsePoint $moduleRoot
        $reservedNames = @("$Name.Server", 'Newtonsoft.Json', 'AwithGameFrame.Unity', 'AwithGameFrame.Extensions', 'AwithGameFrame.Godot') +
            @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.csproj' -File -Recurse | ForEach-Object { $_.BaseName })
        if ($reservedNames -icontains $moduleName) { throw "Module name conflicts with a framework, dependency or server assembly: $moduleName" }
        if (Test-Path -LiteralPath $moduleRoot) { throw "Module already exists: $moduleRoot" }
        $applicationPath = Join-Path $gameRoot $game.applicationProject
        Assert-NoReparsePoint $applicationPath
        [xml]$applicationXml = [IO.File]::ReadAllText($applicationPath)
        $referencePath = "../$moduleName/$moduleName.csproj"
        if (@($applicationXml.SelectNodes('//ProjectReference') | Where-Object { $_.GetAttribute('Include') -eq $referencePath }).Count -gt 0) {
            throw "Application already references this module: $referencePath"
        }
        $projectText = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Runtime/**/*.cs" />
  </ItemGroup>
</Project>
'@
        # Validate the application edit before creating the new module.
        $group = $applicationXml.CreateElement('ItemGroup')
        $reference = $applicationXml.CreateElement('ProjectReference')
        $reference.SetAttribute('Include', $referencePath)
        $group.AppendChild($reference) | Out-Null
        $applicationXml.DocumentElement.AppendChild($group) | Out-Null
        Write-Utf8 (Join-Path $moduleRoot "$moduleName.csproj") ($projectText + "`n")
        Write-Utf8 (Join-Path $moduleRoot 'README.md') "# $moduleName`n`nGame-specific portable feature. Domain contains pure rules; Application owns state and effects. Add only explicit dependencies. This empty module is referenced by $Name.Application.`n"
        Write-Utf8 (Join-Path $moduleRoot 'Runtime/Domain/README.md') "# Domain`n`nPure C# rules with explicit inputs and outputs; no engine, IO, clock or global service access.`n"
        Write-Utf8 (Join-Path $moduleRoot 'Runtime/Application/README.md') "# Application`n`nCompose rules, commit state and perform explicit effects. Keep engine implementation in the client.`n"
        $applicationXml.Save($applicationPath)
        Write-Host "Created $moduleName and referenced it from $Name.Application."
    }
    'config' {
        $game = Read-Game
        $buildLock = Enter-WorkspaceBuildLock
        try { Invoke-GameConfiguration $game ([bool]$Check) }
        finally { $buildLock.Dispose() }
    }
    'protocol' { Invoke-GameProtocol (Read-Game) ([bool]$Check) }
    { $_ -in @('build', 'sync') } {
        $game = Read-Game
        $buildLock = Enter-WorkspaceBuildLock
        try {
            Invoke-GameBuild $game
            if ($Command -eq 'sync') {
                $sync = @{ RepositoryRoot = $repositoryRoot; GameRoot = $gameRoot; Configuration = $Configuration }
                if ($ClientGameRoot) {
                    $sync.GameRoot = $ClientGameRoot
                    $sync.ClientDirectory = $ClientDirectory
                    $sync.SourceGameRoot = $gameRoot
                }
                & (Join-Path $PSScriptRoot 'workspace-client.ps1') @sync
            }
        }
        finally { $buildLock.Dispose() }
    }
    'run-server' {
        $game = Read-Game
        if (Test-GameConfiguration) {
            Invoke-GameConfiguration $game $true
        }
        Assert-PortableProjects (Join-Path $gameRoot $game.applicationProject)
        Assert-NoReparsePoint (Join-Path $gameRoot $game.serverProject)
        $arguments = @('run', '--project', (Join-Path $gameRoot $game.serverProject), '--configuration', $Configuration, '--')
        if ($RunForSeconds -gt 0) { $arguments += @('--run-for-seconds', $RunForSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }
        if ($ServerConfig) { $arguments += @('--config', [IO.Path]::GetFullPath($ServerConfig)) }
        Invoke-DotNet $arguments
    }
}
