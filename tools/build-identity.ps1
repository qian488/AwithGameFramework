function Get-BuildIdentityGitValue([string]$RepositoryRoot, [string[]]$Arguments) {
    $value = & git -C $RepositoryRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return 'unknown' }
    return ($value -join "`n").Trim()
}

function Get-BuildIdentityFiles([string]$RepositoryRoot, [string]$GameRoot) {
    $files = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $extensions = @('.cs', '.csproj', '.props', '.targets', '.json', '.csv', '.xlsx', '.proto', '.yaml', '.binpb', '.lock', '.ps1')
    $roots = @(
        (Join-Path $RepositoryRoot 'src'),
        (Join-Path $RepositoryRoot 'tools/ConfigGen'),
        (Join-Path $GameRoot 'src'),
        (Join-Path $GameRoot 'configuration'),
        (Join-Path $GameRoot 'protocol')
    )
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
            $relative = $file.FullName.Substring($root.Length + 1)
            if ($file.Extension -in $extensions -and $relative -notmatch '(^|[/\\])(bin|obj)([/\\]|$)' -and
                ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                $null = $files.Add($file.FullName)
            }
        }
    }
    foreach ($path in @(
        (Join-Path $RepositoryRoot 'Directory.Build.props'),
        (Join-Path $RepositoryRoot 'Directory.Build.targets'),
        (Join-Path $RepositoryRoot 'global.json'),
        (Join-Path $RepositoryRoot 'tools/game.ps1'),
        (Join-Path $RepositoryRoot 'tools/workspace-client.ps1'),
        (Join-Path $RepositoryRoot 'tools/build-identity.ps1'),
        (Join-Path $RepositoryRoot 'tools/generate-protobuf.ps1'),
        (Join-Path $GameRoot 'game.json')
    )) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { $null = $files.Add([IO.Path]::GetFullPath($path)) }
    }
    return @($files | Sort-Object)
}

function Get-BuildIdentityFingerprint([string]$RepositoryRoot, [string]$GameRoot) {
    $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([char[]]@('\', '/'))
    $records = foreach ($file in Get-BuildIdentityFiles $repository $GameRoot) {
        $relative = $file.Substring($repository.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $relative + ' ' + $hash
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes((($records -join "`n") + "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()) }
    finally { $sha.Dispose() }
}

function Get-BuildIdentityUnityVersion([string]$ClientRoot) {
    $path = Join-Path $ClientRoot 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $match = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^m_EditorVersion:\s*(?<version>\S+)\s*$')
    if (-not $match.Success) { throw "Unity ProjectVersion.txt has no m_EditorVersion: $path" }
    return $match.Groups['version'].Value
}

function Get-BuildIdentity([string]$RepositoryRoot, [string]$GameRoot, $Game) {
    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    [ordered]@{
        sourceCommit = Get-BuildIdentityGitValue $repository @('rev-parse', 'HEAD')
        sourceDirty = (Get-BuildIdentityGitValue $repository @('status', '--porcelain')) -ne ''
        sourceFingerprint = Get-BuildIdentityFingerprint $repository $GameRoot
        targetFramework = 'netstandard2.1'
        engineVersion = $(if ($Game.engine -eq 'unity') { Get-BuildIdentityUnityVersion (Join-Path $GameRoot 'client/Unity') })
    }
}
