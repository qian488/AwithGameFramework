[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$contentSource = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([char[]]'\/')
$contentOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([char[]]'\/')
if (-not (Test-Path -LiteralPath $contentSource -PathType Container)) { throw 'Content source does not exist.' }
if ($contentSource -eq $contentOutput -or $contentOutput.StartsWith($contentSource + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $contentSource.StartsWith($contentOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and output directories must be separate trees.'
}
if (Test-Path -LiteralPath $contentOutput) { throw 'Use a new output directory for each published version.' }
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -match '[\x00-\x1F\x7F-\x9F]') { throw 'Version must be non-empty text.' }
$contentFiles = @()
$contentPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$contentItems = @(Get-ChildItem -LiteralPath $contentSource -Recurse -Force)
foreach ($contentItem in $contentItems) {
    if ($contentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Content must not contain links: $($contentItem.FullName)" }
}
foreach ($contentItem in @($contentItems | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)) {
    $contentRelative = $contentItem.FullName.Substring($contentSource.Length + 1).Replace([char]92, [char]47)
    if ($contentRelative.Split('/')[0] -ieq 'content-manifest.json' -or $contentRelative -match '[:*?"<>|\\\x00-\x1F\x7F-\x9F]' -or
        @($contentRelative.Split('/') | Where-Object { $_ -match '[. ]$|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)' }).Count -gt 0) {
        throw "Non-portable content path: $contentRelative"
    }
    if (-not $contentPaths.Add($contentRelative)) { throw "Duplicate content path: $contentRelative" }
    $contentFiles += [ordered]@{
        key = $contentRelative
        path = $contentRelative
        size = $contentItem.Length
        sha256 = (Get-FileHash -LiteralPath $contentItem.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
foreach ($contentFile in $contentFiles) {
    $contentParts = $contentFile.path.Split('/')
    for ($contentIndex = 1; $contentIndex -lt $contentParts.Length; $contentIndex++) {
        if ($contentPaths.Contains([string]::Join('/', $contentParts[0..($contentIndex - 1)]))) {
            throw "Content file/directory collision: $($contentFile.path)"
        }
    }
}
$null = New-Item -ItemType Directory -Path $contentOutput
foreach ($contentFile in $contentFiles) {
    $contentTarget = Join-Path $contentOutput $contentFile.path
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($contentTarget))
    Copy-Item -LiteralPath (Join-Path $contentSource $contentFile.path) -Destination $contentTarget
    if ((Get-Item -LiteralPath $contentTarget).Length -ne $contentFile.size -or
        (Get-FileHash -LiteralPath $contentTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $contentFile.sha256) { throw 'Source changed during publishing.' }
}
$contentManifest = [ordered]@{ version = $Version; files = @($contentFiles) }
$contentJson = ConvertTo-Json -InputObject $contentManifest -Depth 8 -Compress
[IO.File]::WriteAllText((Join-Path $contentOutput 'content-manifest.json'), $contentJson, [Text.UTF8Encoding]::new($false))
Write-Host "Published $($contentFiles.Count) files to $contentOutput"
