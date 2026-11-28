# Background Unity Player smoke runner

Run an already-built Windows Unity Player in the background with a game-specific smoke flag and success marker. [Runner script](../../tools/run-unity-player-smoke.ps1)

The runner launches the executable directly from the Player directory with `-batchmode`, `-nographics`, and `-logFile <evidence-directory>/player.log`. It requests a hidden startup and suppresses a console window. The Player's smoke handler must run in batch mode, log its marker after the checks, and quit with `Application.Quit(exitCode)`.

## Usage

```powershell
powershell -File tools/run-unity-player-smoke.ps1 `
  -PlayerPath 'artifacts/TestPlayer/Test.exe' `
  -Flag '-run-smoke' `
  -SuccessMarker 'TEST_PLAYER_SMOKE_PASSED' `
  -TimeoutSeconds 180 `
  -EvidenceDirectory 'artifacts/evidence/test-player-smoke'
```

- `-PlayerPath` accepts an existing `.exe` file.
- `-Flag` accepts one custom switch with one or two leading hyphens and letters, digits, dots, underscores, or hyphens in the name. The first character of the name must be a letter or digit. `batchmode`, `nographics`, `logFile`, `nolog`, and `quit` are reserved.
- `-SuccessMarker` must match a complete Player log line after trimming whitespace. The comparison is case-sensitive.
- `-PlayerArguments` supplies game-specific switches and values. Use a PowerShell array when passing more than one argument.
- `-CaptureFlag` passes a fresh capture directory to the project smoke handler and runs the Player in hidden D3D11 batch mode. The project renders to a texture and writes PNG files into that directory. The result records the PNG paths. Feed the captured frames into image cases for pixel and baseline checks. [Visual verification](unity-visual-verification.md)
- `-TimeoutSeconds` defaults to 120 and accepts 1 through 86400 seconds. On timeout the runner terminates the Player and allows up to five seconds for it to exit.
- `-EvidenceDirectory` defaults to a unique directory under the repository's `artifacts/unity-player-smoke`. A specified directory is created if needed. Existing `player.log` or `evidence.json` output requires a fresh destination.

Relative input paths resolve from the caller's PowerShell location.

## Results

Once input validation reserves the evidence destination, the run records its outcome in two files.

- `player.log` contains the Unity log. If the Player exits or fails to start before creating it, the runner writes a diagnostic placeholder.
- `evidence.json` is UTF-8 and uses schema version `1`. It records the executable and log paths, supplied flag and marker, UTC start and end times, duration, Player exit code, timeout and marker state, status, and error when applicable. In Windows PowerShell 5.1 use `Get-Content -Raw -Encoding UTF8` to read it.

A run passes only when the Player exits with code `0` and the success marker occurs in `player.log` before the timeout.

## Exit codes

| Runner exit code | Evidence status | Meaning |
| ---: | --- | --- |
| `0` | `passed` | Player exited with code `0` and logged the success marker |
| `1` | `launch-failed` or `runner-error` | Player launch or runner failed. Invalid inputs also return `1` and stop before evidence is reserved |
| `2` | `player-exit-code` | Player exited with a non-zero code |
| `3` | `timed-out` | The Player exceeded the timeout |
| `4` | `success-marker-missing` | Player exited with code `0` and the success marker was absent |
| `5` | `capture-missing` | Capture mode completed without a PNG file |

`playerExitCode` records the code after a completed wait. Timeout evidence has `timedOut: true` and a null `playerExitCode`.

## Validation

A script-level check can use a temporary Windows executable that reads `-logFile` and the custom flag. Run it with a passing marker, a missing marker, a non-zero exit code, and a timeout to exercise the runner without building a game.
