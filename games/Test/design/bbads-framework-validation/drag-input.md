# BBads 拖放输入验证

2026-11-27 使用 Unity **2022.3.16f1 (d2c21f0ef2f1)**，在 Test 隔离工程以 Windows 隐藏后台模式运行 PlayMode，参数为 `-batchmode -nographics`。测试加载 `BBadsBattle`，关闭自动摆放，以场景原配置推进时间轴至鱼和火箭猫入托盘。自动摆放用例单独重新启用原定时摆放。 [PlayMode 测试](../../client/Unity/Assets/Tests/PlayMode/BBadsDragInputTests.cs)

测试在场景的 `StandaloneInputModule.inputOverride` 注入鼠标位置和按键边沿，使用原 `EventSystem`、战斗 Canvas 的 `GraphicRaycaster` 和输入模块的鼠标拖拽派发。拖拽起点断言命中的图形沿父级找到预期 `ItemView`。空格区域的 `Cell_*` 图形关闭射线，射线命中场景背景，落格由 Presenter 根据指针坐标计算。失焦用例同时向场景 EventSystem 和 Presenter 发送 `OnApplicationFocus`，并让原输入模块执行失焦时的释放分支。暂停用例调用场景 `BackpackController.Clock` 的 `SetPlaybackSpeed`。

| 验收 | 操作与断言 | 修复前 | 修复后 |
| --- | --- | --- | --- |
| A11 | 将 `Fish1` 放入 `(3,2)`、`(3,3)`，再将 `CatRocket1` 拖到同一位置。鱼保留两格，猫保留原托盘 ID，只有一次成功摆放，召唤为零。 | 通过 | 通过 |
| A11 | 将火箭猫放入与鱼相邻的 `(4,1)`、`(4,2)`，观测一次真实召唤。把鱼拖回托盘，两格清空，鱼的冷却保留，猫继续占两格。再等 3 秒，召唤仍为一次。 | 通过 | 通过 |
| A12 | 拖到合法落点时失焦，再恢复焦点并松开。要求鱼回托盘起点、信息卡关闭、摆放和召唤均为零，新拖拽可成功一次。 | 失焦释放将鱼提交到背包 | 通过 |
| A12 | 拖拽中暂停，暂停期间再次尝试拖拽，恢复后松开。要求前两次均无摆放和召唤，信息卡关闭，新的按下和拖放可提交一次。 | 暂停后信息卡仍打开 | 通过 |
| A12 | 单独启用场景背包 `InputService` 的高优先级模态上下文。已开始的拖拽收到 `Released` 并取消，模态期间的新按下被阻断，关闭模态后按住的手势仍取消，重新按下才能摆放一次。 | 背包未接入框架输入上下文 | 通过 |
| 时间联动 | 开始自动飞行动画，暂停 0.55 秒，保持位置且不提交，恢复后飞行完成并提交一次。 | 暂停期间位置从约 `(-0.37, -3.16, 90)` 移到 `(0, -0.42, 90)` | 通过 |
| 场景重开 | 同时加载两份 `BBadsBattle`，向本场景的 Presenter 注入另一场景的信息卡引用，再用指针拖放。只有本场景的卡显示，放置后关闭。 | 本场景的信息卡未显示 | 通过 |

`BackpackPresenter` 使用场景内的 `BackpackDragInput`，将 Unity 拖拽手势提交给 `InputService` 的 `bbads.backpack` 上下文。现有 `BackpackController.Clock` 暂停时启用优先级更高的 `bbads.pause` 模态上下文。路由产生的 `Pressed` 或 `Held` 允许拖拽。模态释放当前手势后，Presenter 恢复原位置并等待 Unity 下一次开始拖拽。Unity 继续负责指针坐标、射线命中和拖放事件，`Clock.Delta` 推进自动摆放和物品表现。失焦和回收仍会清理手势。独立模态测试在同一个 `InputService` 注册观察路由，覆盖按下、释放、阻断和恢复。 [输入接线](../../client/Unity/Assets/BBads/Runtime/Backpack/BackpackDragInput.cs) · [Presenter](../../client/Unity/Assets/BBads/Runtime/Backpack/BackpackPresenter.cs) · [ItemView](../../client/Unity/Assets/BBads/Runtime/Backpack/ItemView.cs)

## 复跑

从仓库根运行。测试程序集依赖父工单登记的 `BBads.Runtime` 引用，运行时依赖父工单实现的战斗时钟。

```powershell
$editor = 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe'
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -RunTests
```

仅复跑本切片时，使用上面命令加 `-KeepProject` 保留隔离工程，从验证输出取得路径。

```powershell
$project = '<隔离工程绝对路径>'
$result = '<测试结果 XML 绝对路径>'
$log = '<日志绝对路径>'
$argsForUnity = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'),
    '-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'Test.Client.Tests.BBadsDragInputTests',
    '-testResults', ('"' + $result + '"'), '-logFile', ('"' + $log + '"'))
if (!$env:ALLUSERSPROFILE) {
    $env:ALLUSERSPROFILE = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
}
Start-Process -FilePath $editor -ArgumentList $argsForUnity -WindowStyle Hidden -Wait
```

对照证据位于 `artifacts/game-unity/Test/20261127-121809-617dee58`。两个过滤运行使用同一测试和隔离项目，切换前后的两个背包表现源码。

- 修复前 `Drag-baseline.xml` 为 **2/5**，日志为 `Drag-baseline.log`。原 `ItemView.cs` 和 `BackpackPresenter.cs` 保存在同目录 `before-source`。
- 修复后 `Drag-fixed.xml` 为 **5/5**，日志为 `Drag-fixed.log`。
- `tools/verify.ps1` 通过，框架测试 **578/578**，模板测试 **13/13**。日志为 `verify-repo.log`。

合并快照的复测位于 `artifacts/bbads-framework/drag-input-merged-122828`。`PlayMode.xml` 记录背景命中修正后 **5/5**，日志为 `PlayMode-retry.log`。同一个隔离项目在信息卡场景归属修复前复现 **0/1**，证据为 `Scene-scope-baseline.xml` 和 `Scene-scope-baseline.log`。最终 `Drag-final.xml` 记录 **6/6**，日志为 `Drag-final.log`。最终仓库检查通过，日志为 `verify-repo-final.log`。

接入 `InputService` 后，同一隔离项目连续两次通过 **7/7**，包括原六项拖放测试与新增模态测试。最终结果为 `A12-PlayMode.xml`，日志为 `A12-PlayMode.log`。仓库检查日志为 `A12-verify-repo.log`。

## 待人工验收

在 `BBadsBattle` 场景的 `BattleDemoBootstrap` 关闭 **Auto Place Items**，从 `SampleScene` 点击 **Open BBads** 进入。用实体鼠标拖到相邻空格、重叠格和托盘，拖拽中切换窗口后再回来松开。检查不同分辨率的视觉反馈、退回托盘和恢复后的新拖放。本轮执行范围为无图形 PlayMode。

