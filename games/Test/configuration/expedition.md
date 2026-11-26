# Rain Pass Expedition configuration

Edit [expedition.json](expedition.json) to change the map order, first-clear reward, equipment unlock cost or starting supplies. `schemaVersion` identifies the JSON format the loader supports. Increment the content `version` when changing the policy so callers can identify which revision they used.

- `routes` contains the starting route choices in display order. Their IDs match the existing contract IDs for Salt Causeway and Ember Foundry. `finalRoute` identifies the built-in Cedar Grove contract.
- `firstClearMarks` awards one mark for an initial clear in the shipped policy. `unlockCost` spends one mark per equipment unlock. `standard` is the default loadout and starts with 6 arrows and 100 posture. `guard` supplies 4 arrows and 125 posture, and `archer` supplies 10 arrows and 80 posture.

In `game.json`, bind `unityConfiguration.Expedition` to `configuration/expedition.json` and run `powershell -File tools/game.ps1 sync -Name Test`. Sync copies the source bytes to the workspace package and generates deterministic Unity metadata for the resource. Load `Resources.Load<TextAsset>("AwithConfiguration/Test/Expedition")` and pass its `bytes` to `Test.Application.Expedition.ExpeditionCatalog.LoadAsync`. For a .NET caller, pass the bytes read directly from the source file.

Use `Routes` and `FinalRoute` for route IDs and typed combat maps. Resolve downloadable content through the existing contract lookup by route ID and use the built-in contract entry for Cedar Grove. Use `Equipment` as the loadout list and apply each entry's `Arrows` and `Posture` as starting supplies. Read the mark award and equipment cost from `FirstClearMarks` and `UnlockCost`. Report `ConfigurationValidationException` at the configuration load boundary.

The standalone source uses the framework JSON serializer. Run `dotnet test games/Test/tests/Test.Application.Tests/Test.Application.Tests.csproj --filter FullyQualifiedName~ExpeditionCatalogTests` to validate the source and malformed input diagnostics.
