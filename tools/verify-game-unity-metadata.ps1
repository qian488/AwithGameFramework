function Assert-GameUnityMetadata {
    param([Parameter(Mandatory = $true)][string]$AssetsPath)

    $assets = (Get-Item -LiteralPath $AssetsPath -Force -ErrorAction Stop).FullName
    $guids = @{}
    $directories = New-Object 'System.Collections.Generic.Stack[string]'
    $directories.Push($assets)
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Name.StartsWith('.') -or $item.Name.EndsWith('~')) { continue }
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked Unity asset is not supported: $($item.FullName)" }
            if ($item.PSIsContainer) { $directories.Push($item.FullName) }
            if (-not $item.PSIsContainer -and $item.Extension -eq '.meta') {
                $contents = [IO.File]::ReadAllText($item.FullName)
                $matches = [regex]::Matches($contents, '(?m)^guid:[ \t]*([a-fA-F0-9]{32})[ \t]*\r?$')
                if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -eq ('0' * 32)) { throw "Missing or invalid Unity GUID: $($item.FullName)" }
                $guid = $matches[0].Groups[1].Value
                if ($guids.ContainsKey($guid)) { throw "Duplicate Unity GUID $guid : $($guids[$guid]) and $($item.FullName)" }
                $guids[$guid] = $item.FullName
                continue
            }
            if (-not $item.PSIsContainer -and $item.Extension -match '^\.(csproj|sln|slnx|suo|user|userprefs|tmp)$') { continue }
            if (-not (Test-Path -LiteralPath ($item.FullName + '.meta') -PathType Leaf)) { throw "Unity import asset is missing its .meta file: $($item.FullName)" }
        }
    }
}
