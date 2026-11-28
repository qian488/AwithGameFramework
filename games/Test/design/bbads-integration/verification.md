# BBads 集成验证

本次用 Unity **2022.3.16f1** 的隔离工程验证 Test。Windows Release 可运行原 Test 场景和 BBads，后台测试分别验证战斗流程和 D3D11 离屏画面。

## 重跑验证

从仓库根目录构建。同一个 Player 以 CombatShowcase 为首场景，可运行 Rain Pass 的既有冒烟，也可通过 BBads 的参数进入新场景。

```powershell
$editor = '<Unity.exe>'
$scenes = @('Assets/BBads/Scenes/BBadsBattle.unity', 'Assets/Scenes/SampleScene.unity',
    'Assets/Scenes/RainPassCamp.unity', 'Assets/Scenes/EffectShowcase.unity')
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -BuildScene 'Assets/Scenes/CombatShowcase.unity' `
    -AdditionalBuildScenes $scenes -RunTests -ReleasePlayer
```

使用构建报告 `summary.json` 的 `player` 路径。

```powershell
$player = '<Player/Test.exe>'
& games/Test/verify-rain-pass.ps1 -PlayerPath $player
& tools/run-unity-player-smoke.ps1 -PlayerPath $player -Flag '-bbads-smoke' -SuccessMarker 'BBADS_SMOKE_PASSED'
```

画面复测给上面的命令追加 `-CaptureFlag '-bbads-smoke-capture'`。可追加 `-PlayerArguments @('-bbads-art-quality','low','-bbads-smoke-expect-tier','low','-bbads-smoke-capture-size','540x960')`。战斗相机渲染到 RenderTexture，截图包含实际启用的 Canvas、波次、出战单位和背包道具。将证据中的 PNG 路径写入画面用例的 `image` 字段即可运行像素检查。 [画面验收](../../../../docs/reference/unity-visual-verification.md)

以下命令从当前 BBads 表和图集目录生成验证输入，再交给 Unity 审计。验证副本会执行导入和引用检查。

```powershell
$out = 'artifacts/bbads-audit'
& games/Test/design/bbads-integration/write-resource-spec.ps1 -OutputPath "$out/resources.json" -AtlasOutputPath "$out/atlases.json"
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -ResourceSpec "$out/resources.json" -AtlasSpec "$out/atlases.json"
```

高低档 PNG 使用 [导入配置](variant-import-profile.json)。预览和应用的参数见 [通用纹理导入](../../../../docs/reference/unity-art-import-profiles.md)。

Android 用同一验证脚本的 `-PlayerTarget Android` 构建 APK。Android 目标下的 ASTC 导入格式已在独立夹具验证。临时准备 Unity 2022.3.16f1 的 OpenJDK、SDK 和 NDK 后，构建通过 Unity 前置检查并进入 Gradle，Gradle 组装失败且没有生成 APK。真机触控、GPU 实际纹理格式和移动画质仍待验收。

## 证据

| 项目 | 已验证内容 |
| --- | --- |
| 迁移和引用 | Windows 隔离导入检查全部场景和 Prefab 的 Missing Script。Unity 另按运行时类型实际加载了 54 个动态键，日志为 `bbads-resource-audit.log`。跨版本修复覆盖 TMP 字体材质和 Spine Rigidbody2D API。画面以 2022 的 Built-in 渲染为准。 [源基线](source-baseline.md) |
| 规则与配置 | 六张原表共 213 行。NUnit 检查坏引用、无效格子与合并，推进、暂停、重开与调度释放，还有充能余量。 [配置](../../configuration/BBads/README.md) |
| 美术导入 | 首次应用修改 145 个 TextureImporter，第二次应用为 0。两次报告见 `artifacts/game-unity/Test/20261126-165855-3e26ffc4/bbads-art-apply.json` 和 `bbads-art-repeat.json`。 [导入规则](art-import.md) |
| 图集 | 同组 Windows D3D11 夹具的 Draw Calls 为背包 13→1、HUD 6→1、世界 16→1。完整战斗采集当前 HUD 实际绑定的图集。 [图集](art-atlas.md) |
| 高低档 | 正式 Player 的战斗场景读取了 1080×1920 和 540×960 的 BC7 背景。两档完整构建大小为 136,681,041 和 135,018,913 字节，构建皆含 Test 原场景。独立档位包各自跑过了战斗冒烟。 [档位](art-quality.md) |
| 同帧画面 | 首段 16 秒的高低档 540×960 PNG 位于 `artifacts/game-unity/Test/20261126-172322-1e78ec50/bbads-high/initial.png` 和 `bbads-low/initial.png`。记录了出战双方的 Spine 动画轨道、HUD、血条和背包道具。 |
| 完整战斗合批 | 隔离工程构建有图集和无图集两个 Development Player。同一战斗相机在 15.35–15.67 秒渲染 20 帧。Draw Calls 中位数 28→19，SetPass 18→16。波次底图、填充和文字底框绑定同一张 1024×256 BC3 图集页。对照图和逐帧报告见同目录的 `scene-atlas-*` 和 `bbads-art-comparison.json`。 |
| Windows Release | `artifacts/game-unity/Test/20261126-172322-1e78ec50` 中，BBads 的无图形、高档和低档运行均通过。每轮核对 7 次敌人和 3 次道具生成、自动放置、一次召唤、非法放置拒绝和实际伤害。另用测试输入触发基地败北，死亡事件一次，后续刷怪为零。三轮覆盖重开、退出到 SampleScene 和再次进入。 |
| Test 回归 | EditMode 16/16、PlayMode 11/11、Application 97/97、Domain 60/60。Rain Pass 既有 Release 冒烟通过三条原路线和 Boss 试炼，含弓箭、格挡弹反和败北重试。报告见 `artifacts/game-unity/Test/PlayerSmoke-38fa85d42b5c46b2862945fd2bc6a581/result.json`。 |

人工验收可以在 Unity 打开 `BBadsBattle.unity`。检查鼠标实际拖拽、非法落点的反馈、窄屏边缘和动画观感。Android APK 生成后在目标机检查触控、GPU 实际纹理格式以及低档画面。源工程 Unity 6 的运行画面仍需对应编辑器复拍。
