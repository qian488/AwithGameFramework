# Rain Pass Expedition configuration

Edit [expedition.json](expedition.json) to change the map order, first-clear reward, equipment unlock cost or starting supplies. `schemaVersion` identifies the JSON format the loader supports. Increment the content `version` when changing the policy so callers can identify which revision they used.

- `routes` contains the starting route choices in display order. Each ID must match its supported combat map, either `salt-causeway` with `SaltCauseway` or `ember-foundry` with `EmberFoundry`. `finalRoute` identifies the built-in Cedar Grove contract.
- `firstClearMarks` awards one mark for an initial clear in the shipped policy. `unlockCost` spends one mark per equipment unlock. Use the equipment IDs `standard`, `guard`, and `archer`, with a positive arrow capacity and posture limit. The required default loadout `standard` starts with 6 arrows and 100 posture in the shipped configuration.

In `game.json`, bind `unityConfiguration.Expedition` to `configuration/expedition.json` and run `powershell -File tools/game.ps1 sync -Name Test`. Sync copies the source bytes to the workspace package and generates deterministic Unity metadata for the resource. Load `Resources.Load<TextAsset>("AwithConfiguration/Test/Expedition")` and pass its `bytes` to `Test.Application.Expedition.ExpeditionCatalog.LoadAsync`. For a .NET caller, pass the bytes read directly from the source file.

`ExpeditionCatalog.LoadAsync` reports invalid values with their field paths. Once the catalog is loaded, `ExpeditionSession` uses the selected equipment and installed content fingerprint to prepare a battle. It calculates resupply from the saved checkpoint and writes the replay before committing the checkpoint or final result.

The standalone source uses the framework JSON serializer. Run `dotnet test games/Test/tests/Test.Application.Tests/Test.Application.Tests.csproj --filter FullyQualifiedName~ExpeditionCatalogTests` to validate the source and malformed input diagnostics.
