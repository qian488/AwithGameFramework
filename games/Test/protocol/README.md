# Test protocol

Keep editable protocol sources and the compatibility baseline here, outside the engine project. Add `schema.json` and `baseline.json`, then run:

```powershell
powershell -File tools/game.ps1 protocol -Name Test
powershell -File tools/game.ps1 protocol -Name Test -Check
```

The generated `GameProtocol.cs` is written to `src/Test.Application/Runtime/Generated/Protocol`. Existing endpoints and fields follow the HTTP schema baseline.

The contract score submission and receipt DTOs are shared by the HTTP submission endpoint and the score WebSocket session. The session uses AWRT framing. Score recovery returns an ordered batch of the same receipt DTO after the client's persisted cursor.

The leaderboard uses a read-only POST with an explicit request DTO and returns a generated array of leaderboard records. The optional player ID in the submission and receipt keeps existing score payloads readable.

For a game using Protobuf, put `buf.yaml` and source files under `protocol/protobuf/`. Create the reviewed baseline with `buf build protocol/protobuf -o protocol/baseline.binpb`. The same two game commands generate and check `Runtime/Generated/Protobuf`. Install Buf and protoc on PATH or set `AWITH_BUF` and `AWITH_PROTOC` to their executable paths. The game projects choose the Google.Protobuf runtime dependency for the generated files.
