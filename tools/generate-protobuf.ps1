[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$BaselinePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Buf = 'buf',
    [string]$Protoc = 'protoc',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$bufCommand = (Get-Command -Name $Buf -CommandType Application -ErrorAction Stop).Source
$protocCommand = (Get-Command -Name $Protoc -CommandType Application -ErrorAction Stop).Source
$source = (Get-Item -LiteralPath $SourceDirectory).FullName
$baseline = (Get-Item -LiteralPath $BaselinePath).FullName
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath (Join-Path $source 'buf.yaml') -PathType Leaf)) {
    throw 'The Protobuf source directory needs buf.yaml with the project compatibility rules.'
}
$entries = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
foreach ($entry in $entries) {
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked Protobuf source: $($entry.FullName)" }
}
$sources = @($entries | Where-Object { !$_.PSIsContainer -and $_.Extension -ceq '.proto' } | Sort-Object FullName)
if (!$sources) { throw "No .proto sources under $source." }
$relativePaths = @($sources | ForEach-Object { $_.FullName.Substring($source.Length + 1).Replace('\', '/') })
$stage = Join-Path ([IO.Path]::GetTempPath()) ('awith-protobuf-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    Push-Location $source
    try {
        & $bufCommand breaking . --against $baseline
        if ($LASTEXITCODE -ne 0) { throw 'Protobuf compatibility check failed.' }
        $descriptor = Join-Path $stage 'schema.binpb'
        & $bufCommand build . --as-file-descriptor-set --output $descriptor
        if ($LASTEXITCODE -ne 0) { throw 'Protobuf descriptor build failed.' }
        & $protocCommand "--descriptor_set_in=$descriptor" "--csharp_out=$stage" @relativePaths
        if ($LASTEXITCODE -ne 0) { throw 'Protobuf C# generation failed.' }
    }
    finally { Pop-Location }

    $generated = @(Get-ChildItem -LiteralPath $stage -Filter '*.cs' -File | Sort-Object Name)
    if (!$generated) { throw 'Protoc emitted no C# files.' }
    if (Test-Path -LiteralPath $output -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -Force) {
            if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.Name -notin $generated.Name) {
                throw "Unexpected Protobuf output. Review and remove obsolete files explicitly: $($file.FullName)"
            }
        }
    }
    if ($Check) {
        foreach ($file in $generated) {
            $expected = Join-Path $output $file.Name
            if (!(Test-Path -LiteralPath $expected -PathType Leaf) -or
                (Get-FileHash -LiteralPath $expected).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                throw "Generated Protobuf code is stale: $expected"
            }
        }
    }
    else {
        [IO.Directory]::CreateDirectory($output) | Out-Null
        foreach ($file in $generated) { Copy-Item -LiteralPath $file.FullName -Destination $output -Force }
    }
    Write-Output "Protobuf verified: $($generated.Count) C# files."
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    if ($resolvedStage.StartsWith($tempRoot + 'awith-protobuf-', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage -PathType Container)) {
        [IO.Directory]::Delete($resolvedStage, $true)
    }
}
