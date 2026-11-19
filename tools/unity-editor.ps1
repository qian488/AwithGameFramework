[CmdletBinding(DefaultParameterSetName = 'Game')]
param(
    [Parameter(ParameterSetName = 'Game')][ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,39}$')][string]$Name,
    [Parameter(Mandatory = $true, ParameterSetName = 'Project')][string]$ProjectPath,
    [ValidateSet('actions', 'project.info', 'editor.diagnostics', 'asset.find', 'scene.inspect', 'scene.open', 'scene.create', 'scene.save',
        'object.create', 'object.transform', 'component.add', 'component.inspect', 'component.set', 'prefab.place',
        'scene.transaction', 'checkpoint.capture', 'checkpoint.list', 'checkpoint.inspect', 'checkpoint.restore')][string]$Action = 'actions',
    [string]$EditorPath,
    [string]$Scene,
    [string]$Asset,
    [string]$Target,
    [string]$Parent,
    [string]$ObjectName,
    [string]$Kind,
    [string]$Query,
    [single[]]$Position,
    [single[]]$Rotation,
    [single[]]$Scale,
    [System.Collections.IDictionary]$Properties,
    [string]$StepsFile,
    [string]$Checkpoint,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedHash,
    [ValidateRange(1, 500)][int]$Limit = 100,
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 60,
    [switch]$Save,
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
if ($Action -eq 'actions') {
    [ordered]@{
        version = 1
        actions = [ordered]@{
            'project.info' = 'Inspect the Unity version and active scene.'
            'editor.diagnostics' = 'Read live console and compiler diagnostics, or probe a closed project with -EditorPath. Optional -Limit.'
            'asset.find' = '-Query <AssetDatabase filter>, optional -Limit'
            'scene.inspect' = 'Optional -Scene, -Target <hierarchy path>, -Limit'
            'scene.open' = '-Scene <existing Assets/...unity>'
            'scene.create' = '-Scene <new Assets/...unity>'
            'scene.save' = 'Live editor only, optional -Scene guard'
            'object.create' = '-ObjectName, optional -Kind Empty|Cube|Sphere|Capsule|Cylinder|Plane|Quad, -Parent and local transform'
            'object.transform' = '-Target, at least one of -Position, -Rotation, -Scale (each takes three values)'
            'component.add' = '-Target, -Kind Camera|Light|AudioSource|BoxCollider|SphereCollider|Rigidbody|ParticleSystem'
            'component.inspect' = '-Target, -Kind <full component type>, optional -Query <serialized property path>, -Limit'
            'component.set' = '-Target, -Kind <full component type>, -Properties <path/value map>, optional -Preview'
            'prefab.place' = '-Asset <Assets/...prefab>, optional -ObjectName, -Parent and local transform'
            'scene.transaction' = '-Scene, -StepsFile <JSON>, optional -Preview or batch -Save'
            'checkpoint.capture' = '-Asset <Assets/...unity or .prefab>'
            'checkpoint.list' = 'List project-local checkpoints, optional -Limit'
            'checkpoint.inspect' = '-Checkpoint <id>'
            'checkpoint.restore' = '-EditorPath, -Checkpoint <id>, -ExpectedHash <reviewed current SHA-256>'
        }
        batch = 'Pass -EditorPath for a closed project. Object, component and prefab changes require -Scene and -Save.'
    } | ConvertTo-Json -Depth 4
    return
}

$batchMode = -not [string]::IsNullOrEmpty($EditorPath)
$diagnose = $Action -eq 'editor.diagnostics'
$componentEdit = $Action -eq 'component.set'
$transaction = $Action -eq 'scene.transaction'
if (($Preview -and !$transaction -and !$componentEdit) -or ($PSBoundParameters.ContainsKey('Properties') -and !$componentEdit)) {
    throw '-Preview applies to scene.transaction and component.set. -Properties applies to component.set.'
}
if ($StepsFile -and !$transaction) { throw '-StepsFile applies to scene.transaction.' }
if ($transaction -and (!$StepsFile -or !$Scene)) { throw 'scene.transaction requires -StepsFile and -Scene.' }
if ($Action -eq 'checkpoint.capture' -and !$Asset) { throw 'checkpoint.capture requires -Asset.' }
if ($Action -in @('checkpoint.inspect', 'checkpoint.restore') -and !$Checkpoint) { throw 'Supply -Checkpoint from checkpoint.capture or checkpoint.list.' }
if ($Action -eq 'checkpoint.restore' -and (!$batchMode -or !$ExpectedHash)) { throw 'checkpoint.restore requires -EditorPath and -ExpectedHash.' }
$sceneMutation = $Action -in @('object.create', 'object.transform', 'component.add', 'prefab.place') -or (($componentEdit -or $transaction) -and !$Preview)
if ($Save -and (!$batchMode -or !$sceneMutation)) { throw '-Save applies to batch object, component and prefab operations.' }
if ($batchMode -and $sceneMutation -and (!$Save -or !$Scene)) { throw 'Batch mutations require -Scene and -Save.' }
if ($batchMode -and $Preview -and !$Scene) { throw 'Batch previews require -Scene.' }
if ($batchMode -and $Action -eq 'scene.save') { throw 'In batch mode, use -Save on the mutation that creates the change.' }
if ($diagnose -and $PSBoundParameters.Keys.Where({ $_ -in @('Scene', 'Asset', 'Target', 'Parent', 'ObjectName', 'Kind', 'Query', 'Position', 'Rotation', 'Scale') }).Count) {
    throw 'editor.diagnostics does not accept scene editing arguments.'
}

$propertyEdits = @()
$transactionSteps = @()
if ($transaction) {
    $file = Get-Item -LiteralPath $StepsFile
    if ($file.PSIsContainer -or $file.Length -gt 65536) { throw 'The transaction plan must be a JSON file of at most 64 KB.' }
    $plan = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($plan.version -ne 1 -or $plan.steps -isnot [array] -or $plan.steps.Count -notin (1..32)) {
        throw 'Supply a version 1 transaction plan containing 1 to 32 steps.'
    }
    $transactionSteps = $plan.steps
}
if ($componentEdit) {
    if (!$Properties -or $Properties.Count -gt 32) { throw 'Supply between 1 and 32 property edits in -Properties.' }
    $propertyEdits = @(
        foreach ($entry in $Properties.GetEnumerator()) {
            if ($entry.Key -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.Key) -or $entry.Value -isnot [IConvertible]) {
                throw 'Each property edit requires a string path and a scalar value.'
            }
            @{ path = $entry.Key; value = [Convert]::ToString($entry.Value, [Globalization.CultureInfo]::InvariantCulture) }
        }
    )
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($PSCmdlet.ParameterSetName -eq 'Game') {
    if (!$Name) { throw 'Pass -Name <game> or -ProjectPath <Unity project>.' }
    $gameRoot = Join-Path $repository ('games/' + $Name)
    $game = Get-Content -LiteralPath (Join-Path $gameRoot 'game.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($game.name -cne $Name -or $game.engine -cne 'unity') { throw 'The selected game must declare Unity in game.json.' }
    $ProjectPath = Join-Path $gameRoot 'client/Unity'
}

$project = Get-Item -LiteralPath $ProjectPath
if (!$project.PSIsContainer) { throw 'ProjectPath must be a Unity project directory.' }
foreach ($entry in @('Assets', 'Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $project.FullName $entry))) { throw "Unity project is missing $entry." }
}

$editor = $null
if ($batchMode) {
    $editor = Get-Item -LiteralPath $EditorPath
    if ($editor.PSIsContainer) { throw 'EditorPath must point to the Unity executable.' }
}

$library = Join-Path $project.FullName 'Library'
$inbox = Join-Path $library 'AwithGameFrameAutomation'
foreach ($directory in @($project.FullName, $library, $inbox)) {
    if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Linked automation path is unsupported: $directory"
    }
}

if (!$batchMode) {
    $liveMarker = Join-Path $inbox 'live'
    $livePid = 0
    $liveProcess = $null
    if ((Test-Path -LiteralPath $liveMarker -PathType Leaf) -and
        !((Get-Item -LiteralPath $liveMarker -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -and
        [int]::TryParse((Get-Content -LiteralPath $liveMarker -Raw -ErrorAction SilentlyContinue), [ref]$livePid)) {
        $liveProcess = Get-Process -Id $livePid -ErrorAction SilentlyContinue
    }
    $connectionError = 'Open the selected Unity project and turn on Tools > AwithGameFrame > Enable Editor Automation.'
    if ($diagnose) {
        $snapshot = $null
        $state = 'editor-unavailable'
        if ($liveProcess -and $liveProcess.ProcessName -eq 'Unity') {
            $state = 'snapshot-unavailable'
            $connectionError = 'The live editor has no current diagnostics snapshot. Check the automation package and session.'
            $snapshotPath = Join-Path $inbox 'diagnostics.json'
            if ((Test-Path -LiteralPath $snapshotPath -PathType Leaf) -and
                !((Get-Item -LiteralPath $snapshotPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                try {
                    $stream = [IO.File]::Open($snapshotPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                    try {
                        $reader = [IO.StreamReader]::new($stream)
                        $snapshot = $reader.ReadToEnd() | ConvertFrom-Json
                    }
                    finally { $stream.Dispose() }
                    $processStart = $liveProcess.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
                    if ($snapshot.version -ne 1 -or $snapshot.processId -ne $livePid -or $snapshot.processStart -cne $processStart) {
                        $snapshot = $null
                    }
                }
                catch { $snapshot = $null }
            }
            if ($snapshot) { $state = 'available'; $connectionError = $null }
        }
        $result = [ordered]@{
            version = 1; mode = 'live'; state = $state; error = $connectionError; projectPath = $project.FullName
            updatedUtc = $null; compilationState = 'not-observed'; logs = @(); compilerMessages = @()
            logsTruncated = $false; compilerMessagesTruncated = $false
        }
        if ($snapshot) {
            $result.updatedUtc = $snapshot.updatedUtc
            $result.compilationState = $snapshot.compilationState
            $result.logs = @($snapshot.logs | Select-Object -Last $Limit)
            $result.compilerMessages = @($snapshot.compilerMessages | Select-Object -Last $Limit)
            $result.logsTruncated = $snapshot.logsTruncated -or $snapshot.logs.Count -gt $Limit
            $result.compilerMessagesTruncated = $snapshot.compilerMessagesTruncated -or $snapshot.compilerMessages.Count -gt $Limit
        }
        $result | ConvertTo-Json -Depth 5
        return
    }
    if (!$liveProcess -or $liveProcess.ProcessName -ne 'Unity') { throw $connectionError }
}

function Get-BatchDiagnostics($LogPath, $Reply, $EditorProcess) {
    $compilerMessages = [Collections.Generic.Queue[object]]::new()
    $truncated = $false
    $compileFailed = $false
    if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
        $logReader = $null
        try {
            $logStream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $logReader = [IO.StreamReader]::new($logStream)
            $lastDiagnostic = $null
            while ($null -ne ($entry = $logReader.ReadLine())) {
                if ($entry -match '^\s*Scripts have compiler errors\.?\s*$') { $compileFailed = $true }
                if ($entry -ceq $lastDiagnostic -or $entry -notmatch '^\s*(?<file>.+)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>CS\d+):\s*(?<message>.*)$') { continue }
                $lastDiagnostic = $entry
                $compileFailed = $compileFailed -or $Matches.severity -eq 'error'
                if ($compilerMessages.Count -eq $Limit) { $null = $compilerMessages.Dequeue(); $truncated = $true }
                $compilerMessages.Enqueue([ordered]@{
                    severity = $Matches.severity; message = $Matches.code + ': ' + $Matches.message
                    file = $Matches.file; line = [int]$Matches.line; column = [int]$Matches.column
                })
            }
        }
        catch [IO.IOException] { }
        finally { if ($logReader) { $logReader.Dispose() } }
    }
    $exitCode = $null
    $state = 'timed-out'
    if ($EditorProcess.HasExited) {
        $exitCode = $EditorProcess.ExitCode
        $state = 'editor-failed'
        if ($compileFailed) { $state = 'compilation-failed' }
        elseif ($Reply -and !$Reply.ok) { $state = 'command-failed' }
        elseif ($Reply -and $exitCode -eq 0) { $state = 'completed' }
    }
    $compilationState = 'not-observed'
    if ($compileFailed) { $compilationState = 'failed' }
    elseif ($state -eq 'completed') { $compilationState = 'succeeded' }
    $commandError = $null
    if ($Reply) { $commandError = $Reply.error }
    [ordered]@{
        version = 1; mode = 'batch'; state = $state; projectPath = $project.FullName; compilationState = $compilationState
        compilerMessages = $compilerMessages.ToArray(); compilerMessagesTruncated = $truncated
        exitCode = $exitCode; error = $commandError; logPath = $LogPath
    }
}

[IO.Directory]::CreateDirectory($inbox) | Out-Null
$requestId = [Guid]::NewGuid().ToString('N')
$requestPath = Join-Path $inbox ($requestId + '.request')
$pendingPath = Join-Path $inbox ($requestId + '.tmp')
$resultPath = Join-Path $inbox ($requestId + '.result')
$logPath = Join-Path $inbox ($requestId + '.log')
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$requestedAction = if ($diagnose) { 'project.info' } else { $Action }
$payload = [ordered]@{
    version = 1; id = $requestId; batch = $batchMode; expiresUtc = $deadline.ToUnixTimeSeconds()
    action = $requestedAction; scene = $Scene; asset = $Asset; target = $Target; parent = $Parent; name = $ObjectName
    kind = $Kind; query = $Query; position = $Position; rotation = $Rotation; scale = $Scale; limit = $Limit; save = $Save.IsPresent
    preview = $Preview.IsPresent; properties = $propertyEdits; steps = $transactionSteps
    snapshot = $Checkpoint; expectedHash = $ExpectedHash
}
$process = $null
try {
    [IO.File]::WriteAllText($pendingPath, ($payload | ConvertTo-Json -Depth 9 -Compress), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($pendingPath, $requestPath)
    if ($batchMode) {
        $previousProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE')
        try {
            if (!$previousProfile -and $project.PSProvider.Name -eq 'FileSystem' -and [IO.Path]::DirectorySeparatorChar -eq [char]92) {
                $env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData')
            }
            $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project.FullName + '"'),
                '-executeMethod', 'AwithGameFrame.Unity.Editor.EditorAutomation.RunBatch', '-awithEditorRequest', $requestId,
                '-logFile', ('"' + $logPath + '"'))
            $options = @{ FilePath = $editor.FullName; ArgumentList = $arguments; PassThru = $true }
            if ([IO.Path]::DirectorySeparatorChar -eq [char]92) { $options.WindowStyle = 'Hidden' }
            $process = Start-Process @options
        }
        finally { [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $previousProfile) }
    }

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process -and !$process.HasExited) { Start-Sleep -Milliseconds 200; continue }
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            $json = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8
            $reply = $json | ConvertFrom-Json
            if ($reply.id -cne $requestId -or $reply.version -ne 1) { throw 'Unity returned a mismatched response.' }
            if ($diagnose) { Get-BatchDiagnostics $logPath $reply $process | ConvertTo-Json -Depth 5; return }
            if (!$reply.ok) { throw ('Unity ' + $Action + ' failed: ' + $reply.error + ' Record: ' + $resultPath) }
            if ($process -and $process.ExitCode -ne 0) { throw "Unity exited with code $($process.ExitCode). See $logPath" }
            $json
            return
        }

        if ($process -and $process.HasExited) {
            $diagnostics = Get-BatchDiagnostics $logPath $null $process
            if ($diagnose) { $diagnostics | ConvertTo-Json -Depth 5; return }
            if ($diagnostics.state -eq 'compilation-failed') {
                $compilerError = $diagnostics.compilerMessages | Where-Object severity -eq 'error' | Select-Object -Last 1
                if ($compilerError) {
                    throw "Unity compilation failed. $($compilerError.file):$($compilerError.line):$($compilerError.column) $($compilerError.message). See $logPath"
                }
                throw "Unity compilation failed. See $logPath"
            }
            throw "Unity exited without a response. See $logPath"
        }
        Start-Sleep -Milliseconds 200
    }
    if ($diagnose) { Get-BatchDiagnostics $logPath $null $process | ConvertTo-Json -Depth 5; return }
    if (Test-Path -LiteralPath $requestPath -PathType Leaf) {
        try {
            [IO.File]::Move($requestPath, $pendingPath)
            throw "Editor automation timed out before Unity claimed request $requestId. Batch log: $logPath"
        }
        catch [IO.FileNotFoundException] {}
    }
    throw "Request $requestId was claimed but did not report completion in time. Inspect the scene and $inbox before retrying."
}
finally {
    if ($process) { $process.Dispose() }
    foreach ($path in @($pendingPath, $requestPath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
    }
}
