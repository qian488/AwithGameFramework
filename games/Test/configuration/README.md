# Test configuration

可编辑配置源位于本目录。Boss 试点参考商业项目从同一份表定义导出代码和数据的做法，通过 [ConfigGen](../../../tools/ConfigGen/README.md) 生成游戏程序集所用的 DTO 与 JSON。

| 源文件 | 用途 |
| --- | --- |
| `schema.json` | 字段类型，以及 Boss 连段到招式表的 ID 引用 |
| `Data/BossStrike.csv` | 起手、命中与收招步数，伤害、架势压力、距离和角度 |
| `Data/BossChallenge.json` | 地图、Boss 名称、武器、数值和连段组合 |

复制一行 Boss 配置并分配新 ID，可用 `CedarGrove`、`SaltCauseway` 或 `EmberFoundry` 选择现有地图。 `Opening` 至少填一个招式 ID。 `FollowUp` 和 `Desperation` 可用空数组，分别表示无交替招式和无半血变招。生成器检查重复 ID 和缺失引用，应用层加载时校验游戏数值、地图和武器。生成数据嵌入 `Test.Application.dll`，选关界面读取已加载的选项。

```powershell
powershell -File tools/game.ps1 config -Name Test
powershell -File tools/game.ps1 build -Name Test
powershell -File tools/game.ps1 sync -Name Test
```

生成产物位于 `src/Test.Application/Runtime/Generated/Configuration`。使用 `config -Name Test -Check` 检查源文件和产物是否一致，可在启动 Unity 前定位表和字段。

`rain-pass-opponents.json` is the editable source for Rain Pass trials. It is embedded in `Test.Application.dll` by the normal game build. The application loads the file with the framework's `ConfigTable<T>` and validates each row before exposing it to Unity. The hash identifies the `RainPassOpponents/v1` document contract and stays the same when only rows change. Use unique, positive IDs since local best times are keyed by opponent ID. See the [trial workflow](../README.md#雨夜试炼) for build and sync steps.
