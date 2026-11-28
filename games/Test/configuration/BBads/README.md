# BBads configuration

These CSV files come from `Assets/Resources/Csv` in BBads commit `72f9e87ca38f00e8e78142e8f8ad12b7e2e2d366`. The copies match the supplied checkout byte for byte. The committed baseline contains the same data with LF line endings.

| Table | Source rows | Meaning and keys |
| --- | ---: | --- |
| `attack_profile.csv` | 13 | Attack presentation, hit delay in seconds, projectile motion and effects. Keyed by `id`. |
| `unit.csv` | 44 | Unit camp, stats, movement, attack and presentation. Keyed by `id`. `attackProfileId` references `attack_profile.id`. |
| `item.csv` | 25 | Backpack item type, shape, cost, cooldown in seconds, merge and activation. Keyed by `id`. `nextItemId` references `item.id` when present. `summonUnitId` references `unit.id` and is required for Cat items, which summon Player units. |
| `encounter.csv` | 96 | Enemy spawn events. `enemyId` references an Enemy unit. `time` is elapsed battle time in seconds and `x` and `y` are world coordinates. Several rows can share a timestamp. |
| `item_spawn.csv` | 32 | Item appearance and optional scheduled placement. `itemId` references `item.id`. `appearTime` and `placeTime` are elapsed battle time in seconds. Scheduled placements use nonnegative integer grid coordinates and occur at or after appearance. Leave all three placement fields blank for an unscheduled item. The original all-`-1` sentinel is also accepted. |
| `summon_position.csv` | 3 | World coordinates for player summons. `order` is a nonnegative integer and is unique within each scenario selector. |

`demo_001` starts spawning enemies at 3 seconds and has its final events at 158 seconds. Of its 32 item appearances, 26 have a scheduled placement. Catalog IDs, references and scenario selectors are case-insensitive. A blank scenario selector applies to every scenario. Rows sharing a time retain their CSV order in `GetScenario`.

The executable schema is in [BBadsConfiguration.cs](../../src/Test.Application/Runtime/BBads/Configuration/BBadsConfiguration.cs) and the public row types are in [BBadsConfigurationRows.cs](../../src/Test.Application/Runtime/BBads/Configuration/BBadsConfigurationRows.cs). Tables require the named columns, accept reordered headers and reject invalid rows with the table name, the physical line where the record starts, and the field. Numbers use invariant decimal notation. The configuration keeps resource keys as strings so the client can resolve the assets and backpack shape cells.

Use `Test.Application.BBads.Configuration.BBadsConfiguration.Load(readCsv)` where `readCsv` receives a table name such as `attack_profile` and returns the CSV text or `null` for a missing asset. Use `GetScenario("demo_001")` for that scenario's enemy spawns, item spawns and summon positions. Unscheduled placement fields are exposed as `-1`, and `HasScheduledPlacement` is false.

## Refresh the Unity copies

Edit these canonical CSVs and run `powershell -File tools/game.ps1 config -Name Test` from the repository root. The [copy declaration](../copies.json) maps the six files to `client/Unity/Assets/BBads/Resources/BBads/Csv`. The command refreshes the CSV bytes and preserves the Unity `.meta` files.

Run `powershell -File tools/game.ps1 config -Name Test -Check` for a read-only parity check. A stale copy reports its path and the first differing physical line. A missing file reports line 1. The game build, client sync and standard repository verification run the parity check.

From the repository root, run the NUnit checks with the following command.

```powershell
dotnet test games/Test/tests/Test.Application.Tests/Test.Application.Tests.csproj --filter FullyQualifiedName~BBadsConfigurationTests
```
