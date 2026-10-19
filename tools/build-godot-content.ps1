[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$AddressMapPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')][string]$Version
)
$ErrorActionPreference = 'Stop'

function Remove-GodotSourceUids([string]$Text, [string]$SourcePath) {
    # Tokenize quoted strings and comments first, so declaration-shaped user text is untouched.
    $quoted = '"(?:\\[\s\S]|[^"\\])*"'
    $attributePattern = '\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>' + $quoted + ')'
    $tokenPattern = '(?<quoted>' + $quoted + ')|(?<comment>;[^\r\n]*)|(?<declaration>^\s*\[(?<kind>gd_scene|gd_resource|ext_resource)\b(?:[^\]"]|' + $quoted + ')*\])'
    return [regex]::Replace($Text, $tokenPattern, [Text.RegularExpressions.MatchEvaluator]{
        param($token)
        if (-not $token.Groups['declaration'].Success) { return $token.Value }
        $attributes = @{}
        foreach ($attribute in [regex]::Matches($token.Value, $attributePattern)) {
            $attributes[$attribute.Groups['name'].Value] = $attribute.Groups['value'].Value.Trim('"')
        }
        if ($token.Groups['kind'].Value -eq 'ext_resource' -and
            (-not $attributes.ContainsKey('path') -or [string]::IsNullOrWhiteSpace($attributes['path']) -or $attributes['path'].StartsWith('uid://'))) {
            throw "External resources require a source path for namespacing; UID-only references are unsupported: $SourcePath"
        }
        return [regex]::Replace($token.Value, $attributePattern, [Text.RegularExpressions.MatchEvaluator]{
            param($attribute)
            if ($attribute.Groups['name'].Value -eq 'uid') { return '' }
            return $attribute.Value
        })
    }, [Text.RegularExpressions.RegexOptions]::Multiline)
}

if ($Version.Contains('..')) { throw 'Version cannot contain consecutive dots.' }
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$source = (Get-Item -LiteralPath $SourceDirectory).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory for each content build.' }
if ($output.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the source tree.' }
$map = Get-Content -LiteralPath $AddressMapPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($map.PSObject.Properties)
if ($entries.Count -eq 0) { throw 'Address map must contain at least one key -> relative asset path.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repository ('artifacts/ContentBuild/Godot-' + [Guid]::NewGuid().ToString('N'))
$resourceRoot = 'res://awith-content/' + $Version + '/'
$contentRoot = Join-Path $project ('awith-content/' + $Version)
New-Item -ItemType Directory -Force -Path $contentRoot | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
foreach ($item in $sourceFiles) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Content sources cannot contain symbolic links or junctions.' }
    if ($item.PSIsContainer) { continue }
    $relative = $item.FullName.Substring($source.Length + 1).Replace([char]92, [char]47)
    if ($relative.Split('/') -contains '.godot' -or $relative.Split('/') -contains 'obj' -or $relative.Split('/') -contains 'bin') { continue }
    if ($item.Extension -in @('.import', '.uid')) { continue }
    if ($item.Extension -in @('.cs', '.gd', '.dll', '.pck') -or $item.Name -eq 'project.godot') { throw "Resource packs cannot introduce code or nested projects: $relative" }
    if ($item.Extension -in @('.scn', '.res')) { throw "Binary Godot containers cannot be namespaced; use text .tscn/.tres sources: $relative" }
    $destination = Join-Path $contentRoot $relative
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    if ($item.Extension -in @('.tscn', '.tres', '.gdshader', '.gdshaderinc')) {
        # Dedicated source roots use res:// paths relative to that root. Namespace every serialized dependency together.
        $text = [IO.File]::ReadAllText($item.FullName)
        if ($item.Extension -in @('.tscn', '.tres')) { $text = Remove-GodotSourceUids $text $relative }
        [IO.File]::WriteAllText($destination, $text.Replace('res://', $resourceRoot), $utf8)
    } else { [IO.File]::Copy($item.FullName, $destination) }
}
$addresses = [ordered]@{}
$selected = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) {
    $relative = [string]$entry.Value
    if ([string]::IsNullOrWhiteSpace($entry.Name) -or [string]::IsNullOrWhiteSpace($relative) -or $relative.Contains([char]92) -or
        $relative.Contains(':') -or $relative.StartsWith('/') -or @($relative.Split('/') | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' }).Count -gt 0 -or
        -not $selected.Add($relative) -or -not (Test-Path -LiteralPath (Join-Path $contentRoot $relative) -PathType Leaf)) { throw "Invalid or duplicate content address: $($entry.Name)" }
    $addresses[$entry.Name] = $resourceRoot + $relative
}
[IO.File]::WriteAllText((Join-Path $project 'project.godot'), "config_version=5`n[application]`nconfig/name=`"Awith content build`"`n[rendering]`nrenderer/rendering_method=`"gl_compatibility`"`n", $utf8)
$exportFiles = @($addresses.Values | ForEach-Object { ConvertTo-Json -InputObject $_ -Compress }) -join ', '
$preset = "[preset.0]`nname=`"Content`"`nplatform=`"Windows Desktop`"`nrunnable=false`ndedicated_server=false`ncustom_features=`"`"`nexport_filter=`"resources`"`nexport_files=PackedStringArray($exportFiles)`ninclude_filter=`"`"`nexclude_filter=`"*.cs,*.gd,*.dll`"`nexport_path=`"`"`npatches=PackedStringArray()`nencrypt_pck=false`nencrypt_directory=false`nscript_export_mode=2`n[preset.0.options]`nbinary_format/embed_pck=false`nbinary_format/architecture=`"x86_64`"`n"
[IO.File]::WriteAllText((Join-Path $project 'export_presets.cfg'), $preset, $utf8)
New-Item -ItemType Directory -Path $output | Out-Null
$packPath = Join-Path $output 'content.pck'
$importLog = Join-Path $project 'Import.log'
& $editor --headless --editor --path $project --import --log-file $importLog
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $importLog -Pattern 'ERROR:|SCRIPT ERROR:' -Quiet)) { throw "Godot content import failed. See $importLog" }
$uidScript = @'
extends SceneTree

func _initialize():
    var result = {}
    scan("ROOT_PLACEHOLDER", result)
    var output = FileAccess.open("res://content-uids.json", FileAccess.WRITE)
    output.store_string(JSON.stringify(result))
    output.close()
    quit()

func scan(directory: String, result: Dictionary):
    for file in DirAccess.get_files_at(directory):
        if file.ends_with(".import") or file.ends_with(".uid"):
            continue
        var path = directory.path_join(file)
        if ResourceLoader.exists(path):
            var id = ResourceLoader.get_resource_uid(path)
            if id != ResourceUID.INVALID_ID:
                result[ResourceUID.id_to_text(id)] = path
    for child in DirAccess.get_directories_at(directory):
        scan(directory.path_join(child), result)
'@
[IO.File]::WriteAllText((Join-Path $project 'content-uids.gd'), $uidScript.Replace('ROOT_PLACEHOLDER', $resourceRoot), $utf8)
$uidLog = Join-Path $project 'Uids.log'
& $editor --headless --path $project --script 'res://content-uids.gd' --log-file $uidLog
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $uidLog -Pattern 'ERROR:|SCRIPT ERROR:' -Quiet)) { throw "Godot UID export failed. See $uidLog" }
$uids = Get-Content -LiteralPath (Join-Path $project 'content-uids.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$exportLog = Join-Path $project 'Export.log'
& $editor --headless --path $project --export-pack 'Content' $packPath --log-file $exportLog
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packPath) -or (Select-String -LiteralPath $exportLog -Pattern 'ERROR:|SCRIPT ERROR:' -Quiet)) { throw "Godot content export failed. See $exportLog" }
$catalog = [ordered]@{ ResourceRoot = $resourceRoot; PackKey = 'content.pck'; PackSha256 = (Get-FileHash -LiteralPath $packPath -Algorithm SHA256).Hash.ToLowerInvariant(); Resources = $addresses; UidPaths = $uids }
[IO.File]::WriteAllText((Join-Path $output 'pack-catalog.json'), (ConvertTo-Json -InputObject $catalog -Depth 8), $utf8)
Write-Host "GODOT_CONTENT_BUILT pack=$packPath project=$project"
