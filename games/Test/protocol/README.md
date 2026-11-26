# Test protocol

Keep editable protocol sources and the compatibility baseline here, outside the engine project. Add `schema.json` and `baseline.json`, then run:

```powershell
powershell -File tools/game.ps1 protocol -Name Test
powershell -File tools/game.ps1 protocol -Name Test -Check
```

The generated `GameProtocol.cs` is written to `src/Test.Application/Runtime/Generated/Protocol`. Existing endpoints and fields follow the HTTP schema baseline.

The contract score submission and receipt DTOs are shared by the HTTP submission endpoint and the score WebSocket session. The session uses AWRT framing. Score recovery returns an ordered batch of the same receipt DTO after the client's persisted cursor.

The leaderboard uses a read-only POST with an explicit request DTO and returns a generated array of leaderboard records. The optional player ID in the submission and receipt keeps existing score payloads readable.

## Expedition scores

Both POST endpoints use the configured loopback `ScorePort` and the generated HTTP protocol. A successful response uses the HTTP 200 envelope `{ "status": 0, "data": ... }`.

| Generated endpoint | Path | Request | Response data |
| --- | --- | --- | --- |
| SubmitExpeditionScore | /expedition-scores | submissionId, playerId, contentVersion, routeId, gearId, firstTicks, secondTicks | submissionId, accepted |
| GetExpeditionLeaderboard | /expedition-scores/leaderboard | playerId, contentVersion, routeId, gearId, limit | entries, ownBestTicks, ownRank |

Send a GUID in N format as submissionId and positive 32-bit tick counts for both legs. The server returns the lowercase GUID with accepted set to true after saving the score. Identical retries return the same acceptance, including after restart. A different payload for the same GUID returns HTTP 409. Invalid requests return HTTP 400. IDs other than submissionId accept nonblank strings up to 128 characters.

Each entry contains playerId, firstTicks, secondTicks, totalTicks, rank and isMine. Groups match the exact contentVersion, routeId and gearId. The fastest total per player counts, with the faster first leg chosen when a player ties their own total. Players with equal totals are ordered by ordinal player ID. The limit ranges from 1 to 50. The querying player gets their full-group rank and best total even beyond that limit. The response uses zero for both own fields when the player has no score in that group. Total tick counts are 64-bit integers.

The expedition ledger is stored under ScoreDirectory/expedition.

For a game using Protobuf, put `buf.yaml` and source files under `protocol/protobuf/`. Create the reviewed baseline with `buf build protocol/protobuf -o protocol/baseline.binpb`. The same two game commands generate and check `Runtime/Generated/Protobuf`. Install Buf and protoc on PATH or set `AWITH_BUF` and `AWITH_PROTOC` to their executable paths. The game projects choose the Google.Protobuf runtime dependency for the generated files.
