function Assert-UnityVerificationChild([string]$Path, [string]$Parent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd([char[]]@('\', '/'))
    $comparison = if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        [StringComparison]::OrdinalIgnoreCase
    } else { [StringComparison]::Ordinal }
    if (!$fullPath.StartsWith($parentPath + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "Unity verification cleanup path is outside $parentPath -> $fullPath"
    }
    for ($ancestor = $fullPath; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Unity verification cleanup rejected a linked path: $ancestor"
        }
    }
}

function Remove-UnityVerificationDirectory([string]$Path, [string]$RunDirectory) {
    $artifacts = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'
    Assert-UnityVerificationChild $RunDirectory $artifacts
    Assert-UnityVerificationChild $Path $RunDirectory
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (!(Test-Path -LiteralPath $fullPath)) { return $true }
    $deletePath = $fullPath
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $deletePath = if ($fullPath.StartsWith('\\')) { '\\?\UNC\' + $fullPath.Substring(2) } else { '\\?\' + $fullPath }
    }

    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            [IO.Directory]::Delete($deletePath, $true)
            return $true
        }
        catch {
            if (!(Test-Path -LiteralPath $fullPath)) { return $true }
            if ($attempt -eq 4) {
                Write-Warning "Unity verification cleanup could not remove $fullPath ($($_.Exception.Message))"
                return $false
            }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function Clear-UnityVerificationProjectFiles([string]$ProjectPath, [string]$RunDirectory) {
    foreach ($name in @('Library', 'Temp', 'obj', 'Assets', 'Packages', 'ProjectSettings', 'UserSettings')) {
        $null = Remove-UnityVerificationDirectory (Join-Path $ProjectPath $name) $RunDirectory
    }
}

function Save-UnityAutomationEvidence([string]$ProjectPath) {
    $source = Join-Path $ProjectPath 'Library/AwithGameFrameAutomation'
    if (!(Test-Path -LiteralPath $source -PathType Container)) { return }
    $destination = Join-Path $ProjectPath 'AutomationEvidence'
    Assert-UnityVerificationChild $source $ProjectPath
    Assert-UnityVerificationChild $destination $ProjectPath
    if (Test-Path -LiteralPath $destination) {
        throw "Automation evidence already exists: $destination"
    }
    Move-Item -LiteralPath $source -Destination $destination
}
