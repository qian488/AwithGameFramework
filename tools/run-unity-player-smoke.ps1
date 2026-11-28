[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [Parameter(Mandatory = $true)][string]$Flag,
    [Parameter(Mandatory = $true)][string]$SuccessMarker,
    [string]$EvidenceDirectory,
    [string[]]$PlayerArguments = @(),
    [string]$CaptureFlag,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-FileSystemPath([string]$Path) {
    $provider = $null
    $drive = $null
    $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $Path, [ref]$provider, [ref]$drive)
    if ($provider.Name -ne 'FileSystem') {
        throw "Expected a file system path: $Path"
    }
    return [IO.Path]::GetFullPath($resolved)
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This runner requires Windows.'
}
function Assert-CustomFlag([string]$Value) {
    if ($Value -notmatch '\A--?[A-Za-z0-9][A-Za-z0-9_.-]*\z' -or
        $Value.TrimStart('-') -match '\A(batchmode|nographics|logFile|nolog|quit|force-.*)\z') {
        throw 'Expected a custom Player flag.'
    }
}
function Quote-WindowsArgument([string]$Value) {
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    return '"' + [regex]::Replace($escaped, '\\+$', '$0$0') + '"'
}
Assert-CustomFlag $Flag
if ($CaptureFlag) {
    Assert-CustomFlag $CaptureFlag
    if ($CaptureFlag -ieq $Flag) { throw 'CaptureFlag and Flag must differ.' }
}
foreach ($argument in $PlayerArguments) {
    if ($argument -match '[\x00\r\n]') { throw 'PlayerArguments must be single-line values.' }
    if ($argument -match '\A--?[A-Za-z]') {
        Assert-CustomFlag $argument
        if ($argument -in @($Flag, $CaptureFlag)) { throw 'PlayerArguments repeats a runner flag.' }
    }
}
if ([string]::IsNullOrWhiteSpace($SuccessMarker) -or $SuccessMarker -match '[\x00\r\n]') {
    throw 'SuccessMarker must be a non-empty single-line log marker.'
}

$player = Get-Item -LiteralPath (Resolve-FileSystemPath $PlayerPath)
if ($player -isnot [IO.FileInfo] -or $player.Extension -ine '.exe') {
    throw "PlayerPath must point to an existing Windows .exe file: $PlayerPath"
}

if (-not $PSBoundParameters.ContainsKey('EvidenceDirectory')) {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture) +
        '-' + [Guid]::NewGuid().ToString('N')
    $EvidenceDirectory = Join-Path $repositoryRoot "artifacts/unity-player-smoke/$runId"
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    throw 'EvidenceDirectory must be a file system directory path.'
}
$evidenceRoot = Resolve-FileSystemPath $EvidenceDirectory
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
$logPath = Join-Path $evidenceRoot 'player.log'
$evidencePath = Join-Path $evidenceRoot 'evidence.json'
if ((Test-Path -LiteralPath $logPath) -or (Test-Path -LiteralPath $evidencePath)) {
    throw "EvidenceDirectory already contains runner output. Use a fresh directory: $evidenceRoot"
}

$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $player.FullName
$startInfo.WorkingDirectory = $player.DirectoryName
# Windows file paths cannot contain quotes, and the custom flag is a single switch.
$arguments = @('-batchmode', '-logFile', $logPath, $Flag) + $PlayerArguments
$captureDirectory = $null
if ($CaptureFlag) {
    $captureDirectory = Join-Path $evidenceRoot 'captures'
    if (Test-Path -LiteralPath $captureDirectory) { throw 'Capture directory already exists.' }
    $arguments += @('-force-d3d11', $CaptureFlag, $captureDirectory)
}
else { $arguments += '-nographics' }
$startInfo.Arguments = (($arguments | ForEach-Object { Quote-WindowsArgument $_ }) -join ' ')
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden

$process = New-Object Diagnostics.Process
$process.StartInfo = $startInfo
$record = [ordered]@{
    schemaVersion = 1
    kind = 'unity-player-smoke'
    status = 'launch-failed'
    playerPath = $player.FullName
    flag = $Flag
    successMarker = $SuccessMarker
    timeoutSeconds = $TimeoutSeconds
    startedUtc = $null
    endedUtc = $null
    durationMilliseconds = $null
    playerExitCode = $null
    timedOut = $false
    markerFound = $false
    logPath = $logPath
    graphics = [bool]$CaptureFlag
    captures = @()
    error = $null
}

# Reserve the output before starting the Player so two runs cannot share a log.
$evidenceFile = [IO.File]::Open($evidencePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $record.startedUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $runnerExitCode = 1
    try {
        if (-not $process.Start()) { throw "Could not start the Player: $($player.FullName)" }
        $record.status = 'runner-error'
        if ($process.WaitForExit($TimeoutSeconds * 1000)) {
            $record.playerExitCode = $process.ExitCode
        }
        else {
            $record.timedOut = $true
            $record.status = 'timed-out'
            $runnerExitCode = 3
            try { $process.Kill() }
            catch [InvalidOperationException] {
                # The Player may exit between the timed wait and the kill request.
                if (-not $process.HasExited) { throw }
            }
            if (-not $process.WaitForExit(5000)) {
                throw 'The Player did not exit within five seconds of the termination request.'
            }
        }

        if ([IO.File]::Exists($logPath)) {
            $reader = [IO.File]::OpenText($logPath)
            try {
                while ($null -ne ($line = $reader.ReadLine())) {
                    if ($line.Trim().Equals($SuccessMarker, [StringComparison]::Ordinal)) {
                        $record.markerFound = $true
                        break
                    }
                }
            }
            finally { $reader.Dispose() }
        }
        if ($captureDirectory -and [IO.Directory]::Exists($captureDirectory)) {
            $record.captures = @(Get-ChildItem -LiteralPath $captureDirectory -File -Filter '*.png' |
                Where-Object Length -gt 0 | Sort-Object Name | ForEach-Object FullName)
        }

        if ($record.timedOut) {
            $record.error = "Player exceeded the $TimeoutSeconds second timeout."
        }
        elseif ($record.playerExitCode -ne 0) {
            $record.status = 'player-exit-code'
            $record.error = "Player exited with code $($record.playerExitCode)."
            $runnerExitCode = 2
        }
        elseif (-not $record.markerFound) {
            $record.status = 'success-marker-missing'
            $record.error = "Success marker was not found: $SuccessMarker"
            $runnerExitCode = 4
        }
        elseif ($CaptureFlag -and $record.captures.Count -eq 0) {
            $record.status = 'capture-missing'
            $record.error = "The Player did not produce PNG captures in $captureDirectory."
            $runnerExitCode = 5
        }
        else {
            $record.status = 'passed'
            $runnerExitCode = 0
        }
    }
    catch {
        $record.error = $_.Exception.Message
    }
    finally {
        $elapsed.Stop()
        $process.Dispose()
    }

    $record.endedUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    $record.durationMilliseconds = $elapsed.ElapsedMilliseconds
    $json = ($record | ConvertTo-Json -Depth 3) + [Environment]::NewLine
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $evidenceFile.Write($bytes, 0, $bytes.Length)
}
finally {
    $evidenceFile.Dispose()
}

if (-not [IO.File]::Exists($logPath)) {
    [IO.File]::WriteAllText($logPath, '[runner] The Player did not produce a log.' + [Environment]::NewLine)
}

Write-Host "Unity Player smoke evidence: $evidencePath"
if ($runnerExitCode -ne 0) {
    [Console]::Error.WriteLine("$($record.error) Log path: $logPath")
}
exit $runnerExitCode
