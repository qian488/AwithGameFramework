[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('new', 'paths', 'new-module', 'config', 'build', 'sync', 'run-server')]
    [string]$Command,
    [Parameter(Mandatory = $true)][string]$Name,
    [ValidateSet('unity', 'godot')][string]$Engine,
    [string]$Module,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateRange(0, 86400)][double]$RunForSeconds = 0,
    [string]$ServerConfig,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = New-Object System.Text.UTF8Encoding($false)

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

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}

function Get-RelativePath([string]$FromDirectory, [string]$ToPath) {
    $from = New-Object Uri(([IO.Path]::GetFullPath($FromDirectory).TrimEnd([char[]]'\/')) + [IO.Path]::DirectorySeparatorChar)
    $to = New-Object Uri([IO.Path]::GetFullPath($ToPath))
    return [Uri]::UnescapeDataString($from.MakeRelativeUri($to).ToString())
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

function Invoke-GameConfiguration($Game, [bool]$Verify) {
    $source = Join-Path $gameRoot 'configuration'
    $schema = Join-Path $source 'schema.json'
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

Assert-Identifier $Name
if ($Check -and $Command -ne 'config') { throw '-Check applies to the config command.' }
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
                $finalDestination = Join-Path $gameRoot $relative
                $repoReference = Get-RelativePath ([IO.Path]::GetDirectoryName($finalDestination)) $repositoryRoot
                if ($source.Extension -eq '.csproj') { $repoReference = [Security.SecurityElement]::Escape($repoReference) }
                $text = [IO.File]::ReadAllText($source.FullName).Replace('__GAME__', $Name).Replace('__REPO__', $repoReference)
                Write-Utf8 $destination $text
            }
            $game = [ordered]@{ schemaVersion = 1; name = $Name; engine = $Engine
                applicationProject = "src/$Name.Application/$Name.Application.csproj"
                serverProject = "server/$Name.Server/$Name.Server.csproj" }
            Write-Utf8 (Join-Path $stage 'game.json') (($game | ConvertTo-Json) + "`n")
            $moduleDescriptor = [ordered]@{ schemaVersion = 1; id = 'game-' + $Name.ToLowerInvariant().Replace('_', '-')
                purpose = "Engine-independent game code and server composition for $Name."
                sourcePaths = @("games/$Name/src", "games/$Name/server")
                entryPoints = @("games/$Name/game.json", "games/$Name/src/$Name.Application/$Name.Application.csproj")
                readme = "games/$Name/README.md"; dependencies = @('core','data','gameplay','networking')
                verificationTools = @('game-build') }
            Write-Utf8 (Join-Path $stage 'module.json') (($moduleDescriptor | ConvertTo-Json -Depth 6) + "`n")
            Write-Utf8 (Join-Path $stage 'configuration/README.md') "# $Name configuration`n`nAdd schema.json and a flat Data directory here. Run game.ps1 config -Name $Name to generate and -Check to verify the outputs.`n"
            Write-Utf8 (Join-Path $stage 'protocol/README.md') "# $Name protocol`n`nKeep editable protocol sources and compatibility baselines here. Use explicit inputs and outputs with ConfigGen.`n"
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
    'config' { Invoke-GameConfiguration (Read-Game) ([bool]$Check) }
    'build' {
        $game = Read-Game
        if (Test-Path -LiteralPath (Join-Path $gameRoot 'configuration/schema.json')) {
            Invoke-GameConfiguration $game $true
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
            $build = [ordered]@{ schemaVersion = 1; name = $Name; configuration = $Configuration
                applicationProject = $game.applicationProject; files = $files }
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
    'sync' {
        $game = Read-Game
        Write-Host "Building $Name before client sync so Unity receives current external assemblies."
        & $PSCommandPath build -Name $Name -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Game build failed before sync: $Name" }
        & (Join-Path $PSScriptRoot 'workspace-client.ps1') -RepositoryRoot $repositoryRoot -GameRoot $gameRoot -Configuration $Configuration
    }
    'run-server' {
        $game = Read-Game
        if (Test-Path -LiteralPath (Join-Path $gameRoot 'configuration/schema.json')) {
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
