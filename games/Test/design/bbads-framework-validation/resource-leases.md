# BBads 资源租约

战斗启动时，`CombatSpawner` 通过 `ResourceScope`、`ResourceStore` 和 `UnityResourceLoader` 预加载 `BBads/Unit`。预加载期间到达的刷怪与召唤按原顺序排队，战斗恢复运行且 Prefab 就绪时创建单位。Inspector 或调用方指定的 Prefab 在战斗运行时仍可同步创建单位。退出时先取消待交付请求并移除单位死亡订阅，再销毁单位池及其中的活动和闲置实例。观察到 Unity 完成销毁后归还作用域租约，最后关闭资源存储。 [单位加载与清理入口](../../client/Unity/Assets/BBads/Runtime/Combat/CombatSpawner.cs)

共享 Sprite 选用 `BBads/Items/Cat_Gangster1`，该资源由背包物品表和单位表共同引用。测试通过两个独立作用域租用资产，并将 Sprite 绑定到场景实例。测试加载器从独立的内层资源存储取得真实 Unity 租约，保持该租约并控制外层请求的交付时点。这样可以在原生加载完成后重现晚交付，并检查 Unity 线程、重新加载和后端释放。 [PlayMode 用例](../../client/Unity/Assets/Tests/PlayMode/BBadsResourceLeaseTests.cs)

## 验收用例

| 编号 | PlayMode 用例 | 检查项 |
| --- | --- | --- |
| A5 | `TwoConsumersShareRealAssetsAndTheLastReleaseAllowsReacquire` | 两个消费者各持有独立租约，每种资产只加载一次。第一个消费者销毁和关闭作用域后，第二个仍能使用 Prefab 和 Sprite。第二个销毁后，按取得租约的反序调用后端释放。从同一存储重新获取两项资源，各产生一次新的加载请求。 |
| A6 | `CancelledScopeReleasesLateSpriteWithoutInvalidatingTheNewScope` | 旧作用域关闭后取消等待，随后从同一存储重新请求同键 Sprite。新作用域取得资源后交付旧结果，旧租约仅归还一次，新 Sprite 仍存活。新租约归还一次，之后读取租约值抛出 `ObjectDisposedException`。 |
| A6 | `SpawnerCancelsOnExitAndDestroysActiveAndIdleUnitsBeforeRelease` | 从 `BBadsBattle` 切至 `SampleScene` 取消旧请求，再进入 `BBadsBattle`。暂停期间交付新结果，恢复后按顺序创建两次，再交付旧结果且本局单位数不变。击杀其中一个单位以确认回池，销毁 spawner 时检查池根节点和活动、闲置单位均先于租约销毁。 |

Prefab 归还租约会结束后端所有权，原生内存回收由 Unity 的 `UnloadUnusedAssets` 管理。适配器在最后一个 Sprite 所有者归还租约时调用 `UnloadAsset`。最初的两个 Sprite 用例误用 `sprite == null` 判断释放。Unity 2022.3.16f1 通过租约归还和直接用 `Resources.Load<Sprite>` 后调用 `Resources.UnloadAsset`，两条路径的旧引用比较都返回 `false`。验收依据是后端归还次数、关闭租约后的访问异常以及重新加载。此次验证未复现公共资源所有权缺口。 [Unity 资源契约](../../../../adapters/unity/README.md#实例宿主与资源)

## 重跑命令

```powershell
$editor = 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe'
powershell -File tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -RunTests -TestMode PlayMode -TestFilter Test.Client.Tests.BBadsResourceLeaseTests
powershell -File tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -RunTests -KeepProject
powershell -File tools/game.ps1 build -Name Test
powershell -File tools/verify-docs.ps1
powershell -File tools/verify.ps1
```

PlayMode 使用真实 Test 场景和资源。退出与重进由测试的 Unity 场景加载入口触发，组合工单覆盖现有 UI 入口。

## 证据

- 独立资源筛选运行通过 **3/3**。Unity **2022.3.16f1**，使用上述 `-TestMode PlayMode -TestFilter Test.Client.Tests.BBadsResourceLeaseTests` 命令。结果和顺序见 `artifacts/game-unity/Test/20261127-123345-389dc655` 下的 `summary.json`、`PlayMode.xml` 与 `PlayMode.log`。
- Unity **2022.3.16f1**，无图形、隐藏窗口。从共享工作区重新导入的副本中，资源切片 PlayMode **3/3**，全量 EditMode **16/16**，全量 PlayMode **24/25**。唯一失败为转场切片的 `EntryRestartExitAndReentryLoadOnceAndRetireOldHosts`，报告为 `Cannot activate Unity scene: Assets/BBads/Scenes/BBadsBattle.unity`。`artifacts/game-unity/Test/20261127-122910-f4b9d8e0` 下的 `PlayMode.xml`、`EditMode.xml` 和 `PlayMode.log` 保存了结果及 `[BBadsResourceLease]` 事件。
- 首次错误的 Sprite 断言可在 `artifacts/game-unity/Test/20261127-122002-448c1d05/PlayMode.xml` 查看。对照结果记录在后续 `PlayMode.log`，输出 `lease=False, direct Unity=False`。验证范围为 Unity 编辑器 PlayMode 的资源所有权与生命周期。
- `game.ps1 build -Name Test` 和 `verify-docs.ps1` 均通过。`verify.ps1` 的公共测试 **578/578**、功能模板测试 **13/13**，Headless 两项通过。对应日志位于 `artifacts/bbads-framework/resource-leases-game-build.log`、`resource-leases-docs.log` 和 `resource-leases-verify.log`。
