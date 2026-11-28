# BBads 宿主与转场验证

现有 **Open BBads**、**Restart** 和 **Exit** 按钮通过 `BBadsSceneActions` 转场。每个由导航载入的场景使用独立的 `GameHost`，由 `SceneModule` 和 `ResourceModule` 管理 `SceneService` 及资源作用域。Unity 后端异步加载并激活目的场景，装载成功后卸载来源。直接打开的场景由 Unity 持有，离开时按原生句柄卸载。战斗模拟由该场景的 `BBadsBattleClock` 驱动，场景销毁时停机并清空订阅。 [转场入口](../../client/Unity/Assets/BBads/Runtime/UI/BBadsSceneActions.cs)

转场的互斥范围包含来源场景卸载和回滚清理。脚本可等待 `BBadsSceneActions.IsTransitioning` 变为 `false` 后再确认目的场景和执行下一次操作。加载或激活失败时记录目的路径和异常，清理已加载的候选场景，恢复原场景，原按钮可重试。冒烟探针在 `sceneLoaded` 订阅新战斗，在源场景卸载完成后才进入下一步。 [冒烟探针](../../client/Unity/Assets/BBads/Runtime/BBadsSmokeProbe.cs)

## 验收

| 条目 | PlayMode 断言 |
| --- | --- |
| A3 | 从 `SampleScene` 点击两次 **Open BBads**，再分别点击两次 **Restart** 和 **Exit**，最后重进。四次有效操作共收到四个原生 additive 加载事件。每次生效后核对活动场景句柄、来源卸载、场景服务持有的句柄和导航宿主状态。旧战斗宿主停机，生命周期令牌取消，事件订阅归零。新宿主恢复到初始订阅数。 |
| A4 | 直接打开战斗，包装实际的 `UnitySceneBackend`，在新战斗完成原生激活后抛异常。确认候选场景卸载、候选战斗宿主停止，来源仍为活动场景且保留原宿主、`ScenarioRunner` 和订阅。用同一 **Restart** 按钮重试，仅额外加载一个新场景并清理原对局。 |

双击用例在同一帧调用两次现有 uGUI 按钮的 `onClick`。该证据覆盖按钮回调和原生转场时序。 [PlayMode 用例](../../client/Unity/Assets/Tests/PlayMode/BBadsHostTransitionTests.cs)

## 同路径重开

`SceneService.LoadAsync` 按键复用场景句柄，`Single` 也遵循该规则。每个已载入场景使用独立的导航宿主，因此 Restart 能加载同路径的新实例并在激活失败时保留来源。 [场景服务契约](../../../../src/AwithGameFrame.Gameplay/README.md)

在 Unity 2022.3.16f1 中，用 `UnitySceneBackend` 加载并激活 `SampleScene` 后，再用同一服务加载相同路径。原生 `SceneManager.SetActiveScene` 对已经活动的场景返回 `false`，使原适配器抛出 `Cannot activate Unity scene`。适配器现把有效、已加载且已经活动的同一场景视为激活成功。加载失效的句柄仍会失败。隔离工程中的 `SceneSameKeyProbe.ReloadAlreadyActive` 在修复前失败，修复后复用原句柄。报告分别为 `artifacts/game-unity/Test/HostTransition-20261127-122842/same-key.xml` 与同目录的 `fixed.xml`。 [Unity 适配器](../../../../adapters/unity/Runtime/Features/Scenes/UnitySceneBackend.cs)

## 复跑

从仓库根目录运行。Unity 版本为 **2022.3.16f1**。

```powershell
powershell -NoProfile -File tools/game.ps1 build -Name Test
powershell -NoProfile -File tools/verify-docs.ps1
powershell -NoProfile -File tools/verify-game-unity.ps1 -Name Test -EditorPath 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe' -RunTests
```

检查 `PlayMode.xml` 中 `BBadsHostTransitionTests` 的两个用例，场景故障注入日志应含有 `BBads injected activation failure`。

## 实测结果

- `tools/game.ps1 build -Name Test`、`tools/verify-docs.ps1` 和 `tools/verify.ps1` 通过。仓库标准验证包含 578 个框架测试、13 个模板测试及无引擎场景。
- Unity **2022.3.16f1** 隔离工程使用 `-batchmode -nographics -runTests -testPlatform PlayMode -testFilter "BBadsHostTransitionTests|BBadsFrameworkIntegrationTests"`，四个用例全部通过。证据位于 `artifacts/game-unity/Test/20261127-123133-b2a1a004/HostTransition-final.xml` 和同目录的 `HostTransition-final.log`。
- A3 的四次有效操作各产生一次 native additive 加载。新战斗订阅数均为 0，旧宿主上添加的订阅在停机后归零。A4 的候选失败和重试共加载两次，候选和原局按预期先后销毁。
- 冒烟的首次进入、重开和重新进入都完成既有刷怪、放置、召唤、伤害、基地毁坏和停止刷怪断言。短时间轴用例收到一次完成事件。
- 目的场景加载到来源卸载之间，后台日志记录了两个 EventSystem 和 AudioListener 暂时同时存在。稳定场景及故障回滚由断言验证。此轮使用后台编辑器，人工检查可从 `SampleScene` 点击 **Open BBads**，核对重开和退出期间的画面、音频及真实点击。
