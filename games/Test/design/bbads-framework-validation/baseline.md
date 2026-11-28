# BBads 专项基线

2026-11-27 在 Git HEAD `230688bed61e77f04c8deba533e3e934f2c5d5eb` 的工作区执行。该工作区包含 BBads 的未提交迁移和工具改动，工单的 `baseline.json` 保留了对应路径与指纹。六张源 CSV 的 SHA-256 随本轮报告保存在 `bbads-config.sha256`。

## 当前工作区的隔离验证

```powershell
$editor = '<Unity 2022.3.16f1 的 Unity.exe>'
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -RunTests
```

实际使用 Unity **2022.3.16f1**。外部程序集与 Test 服务端构建通过，六份配置副本检查通过。Unity 导入通过，EditMode **16/16**、PlayMode **11/11**。两批测试覆盖现有战斗和粒子预览、Rain Pass、粒子场景及宿主行为，测试程序集可作为后续 PlayMode 切片的入口。增加直接引用 BBads 运行时的测试时，需要在共用测试 asmdef 登记一次 `BBads.Runtime` 引用。证据位于 `artifacts/game-unity/Test/20261127-114016-4b449ba4`，结果见 `summary.json`、`EditMode.xml` 与 `PlayMode.xml`。 [现有测试程序集](../../client/Unity/Assets/Tests/PlayMode/Test.Client.PlayMode.Tests.asmdef)

## 既有 Player 的 BBads 行为基线

复用 2026-11-26 的 Windows Player 并以无图形模式复跑。入口为 `CombatShowcase`，冒烟转入 `BBadsBattle` 并走既有重开、退出到 `SampleScene` 及再次进入的流程。构建与复跑的完整命令见 [迁移验收](../bbads-integration/verification.md)。

| 对局 | 敌方生成 | 道具生成与摆放 | 召唤 | 伤害事件 |
| --- | ---: | ---: | ---: | ---: |
| 首次进入 | 7 | 3 / 3 | 1 | 18 |
| 重开 | 7 | 3 / 3 | 1 | 21 |
| 退出后重进 | 7 | 3 / 3 | 1 | 18 |

三轮均拒绝非法落点，基地毁坏事件各发生一次，停止刷怪后额外生成数为零。伤害事件数量会随对局路径变动，现有用例断言发生实际伤害。证据位于 `artifacts/unity-player-smoke/20261127-034101-3a9975dc901e40828894b48cc395198c`，结果见 `evidence.json` 与 `player.log`。

现有 Player 冒烟直接调用部分按钮回调和摆放接口。当前源代码的 BBads PlayMode 对局、射线与拖拽、暂停期间的并发行为与退出时的异步租约，由后续工单补齐。 [现有冒烟入口](../../client/Unity/Assets/BBads/Runtime/BBadsSmokeProbe.cs)
