# Test configuration

Keep editable configuration sources here, outside the engine project. Configure explicit inputs and outputs before using ConfigGen; the existing sample commands only process samples.

`rain-pass-opponents.json` is the editable source for Rain Pass trials. It is embedded in `Test.Application.dll` by the normal game build. The application loads the file with the framework's `ConfigTable<T>` and validates each row before exposing it to Unity. The hash identifies the `RainPassOpponents/v1` document contract and stays the same when only rows change. Use unique, positive IDs since local best times are keyed by opponent ID. See the [trial workflow](../README.md#雨夜试炼) for build and sync steps.
