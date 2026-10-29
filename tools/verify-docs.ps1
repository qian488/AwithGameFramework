[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Join-Path $PSScriptRoot '..' }
$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([char[]]'\/')

# Git supplies tracked and new files, so ignored caches are never traversed.
$originalConsoleEncoding = [Console]::OutputEncoding
try {
    # Windows PowerShell otherwise decodes UTF-8 Git paths using the console code page.
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $listed = @(& git -C $root -c core.quotepath=false ls-files -z --cached --others --exclude-standard -- '*.md' '*.mdc')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate documentation with git ls-files.' }
} finally {
    [Console]::OutputEncoding = $originalConsoleEncoding
}
$files = @(($listed -join "`n").Split([char]0) | Where-Object {
    $_ -and $_ -notmatch '(^|/)(legacy|artifacts|bin|obj|\.godot|node_modules)(/|$)' -and
    $_ -notmatch '^samples/Unity/(Library|Temp|Logs|Assets/(Plugins|Packages))/' -and
    (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
} | Sort-Object -Unique)
if ($files.Count -eq 0) { throw 'No maintained Markdown documents found.' }

$failures = New-Object 'System.Collections.Generic.List[string]'
$linkCount = 0
$inlinePattern = '\[[^\]\r\n]*\]\([ \t]*(?:<(?<target>[^>\r\n]+)>|(?<target>(?:\\.|[^ \t()\\]|\([^()\r\n]*\))+))(?:[ \t]+[^)\r\n]*)?\)'
$referencePattern = '^[ \t]{0,3}\[[^\]]+\]:[ \t]*(?:<(?<target>[^>\r\n]+)>|(?<target>\S+))'

foreach ($relative in $files) {
    $document = Join-Path $root $relative
    $inFence = $false
    $fenceCharacter = [char]0
    $fenceLength = 0
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($document)) {
        $lineNumber++
        if ($line -match '^[ \t]{0,3}(`{3,}|~{3,})(.*)$') {
            $marker = $Matches[1]
            if (-not $inFence) {
                $inFence = $true
                $fenceCharacter = $marker[0]
                $fenceLength = $marker.Length
            } elseif ($marker[0] -eq $fenceCharacter -and $marker.Length -ge $fenceLength -and
                [string]::IsNullOrWhiteSpace($Matches[2])) {
                $inFence = $false
            }
            continue
        }
        if ($inFence) { continue }
        $text = [regex]::Replace($line, '(`+).*?\1', '')
        $links = @([regex]::Matches($text, $inlinePattern)) + @([regex]::Matches($text, $referencePattern))
        foreach ($link in $links) {
            $target = $link.Groups['target'].Value
            if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|//|#)') { continue }
            $pathPart = ($target -split '[#?]', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
            $linkCount++
            try {
                $decoded = [Uri]::UnescapeDataString($pathPart)
                $decoded = [regex]::Replace($decoded, '\\([ ()\[\]])', '$1')
                if ($decoded.StartsWith('/')) {
                    $resolved = [IO.Path]::GetFullPath((Join-Path $root $decoded.TrimStart('/')))
                } else {
                    $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path $document) $decoded))
                }
                $comparison = [StringComparison]::Ordinal
                if ([IO.Path]::DirectorySeparatorChar -eq '\') { $comparison = [StringComparison]::OrdinalIgnoreCase }
                $within = $resolved.Equals($root, $comparison) -or
                    $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, $comparison)
                if (-not $within) {
                    $failures.Add("${relative}:${lineNumber}: local link leaves repository: $target")
                } elseif (-not (Test-Path -LiteralPath $resolved)) {
                    $failures.Add("${relative}:${lineNumber}: missing local link target: $target")
                }
            } catch {
                $failures.Add("${relative}:${lineNumber}: invalid local link: $target ($($_.Exception.Message))")
            }
        }
    }
}
if ($failures.Count -gt 0) { throw ("Documentation links failed:`n" + ($failures -join "`n")) }
Write-Host "Documentation links passed: $($files.Count) documents, $linkCount local targets."
Write-Host 'Scope: inline Markdown links and reference definitions; excludes legacy, vendored packages, ignored files and fenced/inline code. No network, anchor or HTML validation.'
