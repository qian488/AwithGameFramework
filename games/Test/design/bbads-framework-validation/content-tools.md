# BBads 内容工具验证

2026-11-27，在 HEAD `230688bed61e77f04c8deba533e3e934f2c5d5eb` 及当时共享工作区的未提交迁移上验证 A13、A14。Unity 实测版本为 **2022.3.16f1**。导入、审计、配置 PlayMode 和 PNG 比较使用隐藏批处理及 `-nographics`。场景离屏渲染和运行时截帧使用隐藏批处理及 Direct3D11。所有注入发生在 `artifacts` 下的副本中。验收范围为 Windows 编辑器。

下表中的证据文件均相对于 `artifacts/bbads-content-tools`。初始 Unity 导入和原始资源、图集报告位于 `artifacts/game-unity/Test/20261127-121443-d8c2fca7`。

| 用例 | 注入前与预期 | 注入后 | 恢复后及证据 |
| --- | --- | --- | --- |
| A13 配置副本过期 | `game.ps1 config -Name Test -Check` 退出 0，经检查的 6 份副本一致。仅将源 `unit.csv` 第 2 行 `PlayerWarrior.hp` 从 100 改为 101，预期只读检查退出 1。 | 退出 1。`client/Unity/Assets/BBads/Resources/BBads/Csv/unit.csv:2: differs from configuration/BBads/unit.csv`。源/目标分别为 101/100。 | 执行 `config` 后源/目标为 101/101，再检查退出 0。恢复原始源文件并刷新后为 100/100，再检查退出 0。原始与恢复后的源、目标 SHA-256 均为 `7DCC0012F861C7DADFDB550DBF219F72AEADF845E02B2FECEA4435AE89581721`，目标 `.meta` 在四个状态中的 SHA-256 相同。`config-observations.json`、`config-{clean,stale,refresh,refreshed,restore,restored}.log`。 |
| A13 CSV 字段类型 | 隐藏 PlayMode 从真实 `BBadsBattle` 场景加载导入的 CSV，读到 `PlayerWarrior.hp=100` 和 96 个敌方生成事件。仅改隔离 Unity 工程的 `unit.csv` 第 2 行第 5 列 `hp` 为 `not_a_number`，预期启动时报告表、行和字段。 | PlayMode 退出 2，测试 0/1。`CsvRuntimeData.Awake` 记录 `BBadsConfigurationException: unit.csv, row 2, field 'hp': Expected a finite number, found 'not_a_number'.` | 原字节回写后测试 1/1，退出 0，重新读到 `hp=100` 和 96 条敌方生成事件。`csv-{clean,invalid,restored}.xml`、同名 `.log`。 |
| A13 缺失 Resources 键 | 资源审计通过，60 个键。将源副本 `item.csv` 第 2 行 `Fish1.pictureResourcePath` 改为 `BBads/Items/Fish1_ContentValidationMissing`，更新同一副本的 Unity CSV 并重新生成资源清单。预期审计报告缺失 Sprite。 | 退出 1，检查 61 个键。唯一错误的来源为 `configuration/BBads/item.csv id=Fish1 pictureResourcePath`，类型为 `UnityEngine.Sprite`，匹配数为 0，消息为 `No resource matched the key or its fallbacks.`。其他行仍引用原始键。 | 还原 CSV 和原清单后退出 0，60 个键均通过。`resource-{missing,restored}.json`、同名 `.log` 与 `specs/resources-missing.json`。 |
| A14 场景 Sprite 引用和图集 | 迁移审计检查 5 场景和 18 Prefab，有 0 项发现。Backpack、BattleHud、WorldCombat 分别打入 13、6、5 个 Sprite。移除副本中的 `Assets/BBads/Art/CF/1/Wave_Bar.png` 及其 `.meta`，预期报告断开的场景引用与图集成员。 | 在已导入副本直接执行迁移审计，记录 1 项 `missing-object-reference`，位置为 `Assets/BBads/Scenes/BBadsBattle.unity` 的 `/Canvas[1]/BattleHUD[2]/WaveFill[1]:UnityEngine.UI.Image.m_Sprite`。该编辑器方法写完报告后退出 0。图集审计退出 1。BattleHud 报告 `expectedSprites=5`、`packedSprites=5` 和 `Source has no imported sprites: Assets/BBads/Art/CF/1/Wave_Bar.png`。其余两组通过。 | 按原字节恢复 PNG 和 `.meta`，迁移审计重新报告 0 项发现。图集退出 0，BattleHud 恢复至 6/6。`migration-{clean,missing-sprite,restored}.json`、`atlas-{missing-sprite,restored}.json` 及同名日志。 |
| A14 编辑态 HUD | 540×960 的 `BBadsBattle` 离屏画面通过。清单要求 `WaveLabelFrame` 和 `WaveText` 可见。仅将副本场景的 `WaveLabelFrame.m_IsActive` 从 1 改为 0，预期布局检查报告对应层级。 | 退出 1，消息为 `Missing or ambiguous hierarchy path: /Canvas/BattleHUD/WaveLabelFrame`。检查在渲染前结束。 | 场景按原字节恢复后退出 0，画面仍为 540×960，暗像素比例为 0.02459876611828804，洋红像素比例为 0。`scene-hud-{clean,hidden,restored}/visual-report.json` 及同名批处理日志。 |
| A14 运行时 HUD | 实际场景由 `WAVE 1/16` 推进到 `WAVE 2/16`。游戏时间为 15.50 秒，场景时长 160 秒，填充 0.55，绑定 Sprite `Wave_Bar_0`。用 BBads 冒烟的相机捕获方法截取 540×960 PNG，再在同一帧禁用 `WaveText` 的 TMP 组件。预期像素比较失败。 | 退出 1。通道容差 4、允许变化比例 0，实测变化比例 0.002463348675519228，即 1,277 / 518,400 像素。报告为 `Changed pixels exceed the limit (0.25% > 0.00%).` | 在同帧重新启用 TMP 组件后比较退出 0，变化比例为 0，PNG SHA-256 与注入前相同。`hud-runtime.xml`、`hud-runtime.log`、`captures/hud-{clean,hidden,restored}.png`、`pixels-{clean,hidden,restored}/visual-report.json`。 |

迁移包装脚本在新导入副本中将同一 Sprite 注入判为 `findings`，包装脚本退出 2。除组件属性外，还报告 `unresolved-serialized-guid`，位置为 `BBadsBattle.unity:3182`，GUID 为 `657cf31d804edd04d86a2cc879c96202`，file ID 为 `2043785083260247473`。编辑器本身退出 0。已导入副本的报告只含组件属性发现。同一包装脚本在恢复 PNG 和 `.meta` 后退出 0，检查结果为 5 场景、18 Prefab、0 项发现。证据为 `migration-wrapper-{missing-sprite,restored}.json` 及同名日志。Unity 日志分别在 `artifacts/UnityMigrationAudit/85ffc26b2434/project/unity.log` 和 `artifacts/UnityMigrationAudit/f78ac705ece8/project/unity.log`。

运行时 HUD 探针第一次把最后一次刷怪时间 158 秒用作场景时长，填充断言预期 0.569620252、实测 0.549999952。`ScenarioDefinition` 另加两秒尾段。修正探针按 160 秒断言后，PlayMode 和三份 PNG 比较均得到预期结果。首次报告保留在 `first-hud-runtime.xml`。

## 复跑

从仓库根目录生成清单并准备 Unity 副本。命令输出本次证据目录，`summary.json` 的 `verificationProject` 是后续步骤使用的副本路径。 [清单生成器](write-content-verification-spec.ps1)

```powershell
$editor = 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe'
$specs = 'artifacts/bbads-content-tools-replay/specs'
& games/Test/design/bbads-framework-validation/write-content-verification-spec.ps1 -OutputDirectory $specs
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -ResourceSpec "$specs/resources.json" `
    -AtlasSpec "$specs/atlases.json" -VisualSpec "$specs/hud-scene.json" -KeepProject
```

1. 在副本中按上表做一次单独的修改，然后用相同清单重跑。恢复输入后再跑同一入口。保留 `.log`、退出码与结果 JSON。 `artifacts/bbads-content-tools/run-unity-cases.ps1` 保存了本次在同一 Unity 副本上调用各编辑器方法的命令。直接调用迁移方法时按 `findings` 判定，使用包装脚本时按汇总 `status` 和退出码判定。 [迁移预检](../../../../docs/reference/unity-migration-audit.md) · [资源和图集审计](../../../../docs/reference/unity-resource-audit.md) · [离屏画面](../../../../docs/reference/unity-visual-verification.md)
2. 配置副本使用保留仓库相对路径的小型镜像。复制 `tools/game.ps1`、`tools/build-identity.ps1`、`tools/ConfigGen`、`src`、根 MSBuild 配置、Test 的 `game.json`、`configuration`、`src` 和 Unity CSV 目录。到镜像根下运行 `powershell -NoProfile -File tools/game.ps1 config -Name Test -Check`。去掉 `-Check` 刷新。当前 Test 同时带有 Boss 表的生成配置，镜像需要保留 ConfigGen 和生成输出。 [BBads 配置](../../configuration/BBads/README.md)
3. 缺失键的资源清单须从含注入的源 CSV 生成。可将现有 `write-resource-spec.ps1` 放在镜像的 `games/Test/design/bbads-integration`，从那里按原调用方式生成。恢复时使用正常资源清单和原始 CSV。 [资源清单生成器](../bbads-integration/write-resource-spec.ps1)
4. 将 [内容探针](content-probe/BBadsContentValidation.cs) 和 [测试程序集清单](content-probe/BBads.ContentValidation.asmdef) 复制到隔离 Unity 工程的 `Assets/BBadsContentValidation`。使用隐藏的 Unity 批处理按 `-runTests -testPlatform PlayMode -testFilter BBadsContentValidation.ReadsImportedBattleCsv` 运行 CSV 检查，指定 `-nographics` 和 `-testResults`。坏字段阶段预期得到一个含上述异常的失败测试，恢复后同一测试通过。
5. 对运行时 HUD 使用 `BBadsContentValidation.CapturesWaveHud`，设置环境变量 `BBADS_CONTENT_CAPTURES` 为截图目录并启用批处理图形设备。探针以 `ScenarioRunner.Tick(15.5f)` 固定对局进度，调用已有 `BBadsSmokeProbe.CaptureBattle` 记录同一帧的三种状态。向清单生成器传入 `-HudCaptureDirectory`，再分别用 `-VisualSpec` 运行 `hud-pixels-{clean,hidden,restored}.json`。也可用本次保留的截图重跑图像比较。 [BBads 截帧入口](../../client/Unity/Assets/BBads/Runtime/BBadsSmokeProbe.cs)

Test 外部程序集及服务端构建通过，文档链接检查通过。证据为 `game-build.log` 和 `docs-verify.log`。恢复后的 Unity 场景、Sprite 及其 `.meta` 和两份 CSV 均与共享输入的 SHA-256 相同，见 `restored-inputs.sha256`。
