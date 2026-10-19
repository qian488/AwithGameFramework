[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [switch]$Export,
    [string]$TemplatePath
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sampleRoot = Join-Path $repositoryRoot 'samples/Godot'
$editor = (Get-Item -LiteralPath $EditorPath).FullName
$validationRoot = Join-Path $repositoryRoot ('artifacts/GodotValidation/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
& $editor --version
if ($LASTEXITCODE -ne 0) { throw 'Godot version query failed.' }
& dotnet build (Join-Path $sampleRoot 'AwithGameFrame.Godot.Sample.csproj') --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw 'Godot SDK compilation failed.' }
$importLog = Join-Path $validationRoot 'Import.log'
& $editor --headless --editor --path $sampleRoot --import --log-file $importLog
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $importLog -Pattern 'SCRIPT ERROR:|ERROR:' -Quiet)) { throw "Godot sample resource import failed. See $importLog" }
$contentBuild = Join-Path $validationRoot 'Content'
& (Join-Path $PSScriptRoot 'build-godot-content.ps1') -EditorPath $editor -SourceDirectory (Join-Path $repositoryRoot 'samples/Content/Godot') -AddressMapPath (Join-Path $repositoryRoot 'samples/Content/godot-addresses.json') -OutputDirectory $contentBuild -Version ('validation-' + [Guid]::NewGuid().ToString('N'))
$contentInstall = Join-Path $validationRoot 'ContentInstall'
& (Join-Path $PSScriptRoot 'verify-content.ps1') -SourceDirectory $contentBuild -Version 'godot-validation' -OutputDirectory $contentInstall
$installed = Get-Content -LiteralPath (Join-Path $contentInstall 'Install-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$contentCatalog = Join-Path $installed.directory 'pack-catalog.json'
$logPath = Join-Path $validationRoot 'Runtime.log'
$processOptions = @{
    FilePath = $editor
    ArgumentList = @('--headless', '--path', ('"' + $sampleRoot + '"'), '--log-file', ('"' + $logPath + '"'), '--', '--framework-smoke', '--content-catalog', ('"' + $contentCatalog + '"'))
    PassThru = $true
}
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $processOptions.WindowStyle = 'Hidden' }
$process = Start-Process @processOptions
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Godot validation timed out. See $logPath" }
$process.Refresh()
if ($process.ExitCode -ne 0) { throw "Godot validation failed ($($process.ExitCode)). See $logPath" }
if (-not (Test-Path -LiteralPath $logPath) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'GODOT_HOST_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'SHARED_SCENARIO_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'FOUNDATION_CORE_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'GODOT_PRESENTATION_SCENES_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'GODOT_INPUT_AUDIO_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'GODOT_UNSCALED_CLOCK_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'COMPOSED_APPLICATION_PASSED' -Quiet) -or
    -not (Select-String -LiteralPath $logPath -SimpleMatch 'GODOT_RESOURCE_PACK_PASSED' -Quiet) -or
    (Select-String -LiteralPath $logPath -Pattern 'SCRIPT ERROR:|ERROR:|ObjectDB instances leaked at exit|invalid UID' -Quiet)) {
    throw "Godot assertions failed or emitted errors. See $logPath"
}
Write-Host "Godot SDK build and real host scenario passed. Log: $logPath"

if ($Export) {
    $godotSolution = Join-Path $sampleRoot 'AwithGameFrame.Godot.Sample.sln'
    if (-not (Test-Path -LiteralPath $godotSolution)) {
        & dotnet new sln --format sln --name AwithGameFrame.Godot.Sample --output $sampleRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the Godot export solution.' }
        & dotnet sln $godotSolution add (Join-Path $sampleRoot 'AwithGameFrame.Godot.Sample.csproj')
        if ($LASTEXITCODE -ne 0) { throw 'Could not add the Godot export project.' }
    }
    $presetPath = Join-Path $sampleRoot 'export_presets.cfg'
    $originalPreset = [IO.File]::ReadAllText($presetPath)
    $exportDirectory = Join-Path $validationRoot 'Export'
    New-Item -ItemType Directory -Force -Path $exportDirectory | Out-Null
    $playerPath = Join-Path $exportDirectory 'AwithSharedDemo.exe'
    $exportLog = Join-Path $validationRoot 'Export.log'
    try {
        if ($TemplatePath) {
            $template = (Get-Item -LiteralPath $TemplatePath).FullName.Replace([char]92, [char]47)
            $configuredPreset = $originalPreset.Replace('custom_template/debug=""', ('custom_template/debug="' + $template + '"'))
            [IO.File]::WriteAllText($presetPath, $configuredPreset)
        }
        & $editor --headless --path $sampleRoot --log-file $exportLog --export-debug 'Windows Desktop' $playerPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $playerPath) -or
            (Select-String -LiteralPath $exportLog -Pattern 'ERROR:|error [A-Z]+\d+' -Quiet)) { throw "Godot export failed. See $exportLog" }
    } finally { [IO.File]::WriteAllText($presetPath, $originalPreset) }

    $exportRuntimeLog = Join-Path $validationRoot 'Export-runtime.log'
    $exportOptions = @{
        FilePath = $playerPath
        ArgumentList = @('--headless', '--log-file', ('"' + $exportRuntimeLog + '"'), '--', '--framework-smoke', '--content-catalog', ('"' + $contentCatalog + '"'))
        PassThru = $true
    }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $exportOptions.WindowStyle = 'Hidden' }
    $exportProcess = Start-Process @exportOptions
    if (-not $exportProcess.WaitForExit(120000)) { $exportProcess.Kill(); throw "Godot exported game timed out. See $exportRuntimeLog" }
    $exportProcess.Refresh()
    if ($exportProcess.ExitCode -ne 0 -or -not (Test-Path $exportRuntimeLog) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'GODOT_HOST_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'FOUNDATION_CORE_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'GODOT_PRESENTATION_SCENES_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'GODOT_INPUT_AUDIO_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'GODOT_UNSCALED_CLOCK_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'COMPOSED_APPLICATION_PASSED' -Quiet) -or
        -not (Select-String -LiteralPath $exportRuntimeLog -SimpleMatch 'GODOT_RESOURCE_PACK_PASSED' -Quiet) -or
        (Select-String -LiteralPath $exportRuntimeLog -Pattern 'SCRIPT ERROR:|ERROR:|ObjectDB instances leaked at exit|invalid UID' -Quiet)) {
        throw "Godot exported game failed. See $exportRuntimeLog"
    }
    $manifest = [ordered]@{
        engine = 'Godot'; version = (& $editor --version | Out-String).Trim(); target = 'Windows x86_64 .NET debug';
        commit = (& git -C $repositoryRoot rev-parse HEAD | Out-String).Trim();
        workingTreeModified = [bool](& git -C $repositoryRoot status --porcelain | Out-String).Trim();
        files = @(Get-ChildItem -LiteralPath $exportDirectory -Recurse -File | ForEach-Object {
            [ordered]@{ path = $_.FullName.Substring($exportDirectory.Length + 1); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $validationRoot 'Build-report.json')
    Write-Host "Godot Windows .NET export and runtime assertions passed. Report: $validationRoot"
}
