# Test 游戏工作区

这里维护 Test 的引擎外 C# 代码、Unity 客户端与 .NET 服务端。框架包仍在仓库根 `src`；当前只完成工作区和启动接线，没有玩法或网络业务。

| 区域 | 职责 |
| --- | --- |
| [Domain](src/Test.Domain/README.md) | 游戏自己的纯领域计算，无引擎或框架依赖 |
| [Application](src/Test.Application/README.md) | 状态提交、用例编排、框架服务组合及客户端/服务端共享契约 |
| [Server](server/Test.Server/README.md) | .NET 进程配置、日志、生命周期及专属基础设施接线 |
| `client/Unity` | 已有 Unity 2022.3.16f1 工程，场景、资源、TMP 与最小启动脚本 |

`game.json` 记录游戏名称、引擎和构建入口，生成的项目通过相对路径引用本克隆的框架。客户端位置和接入命令见仓库的游戏工作区指南，Spine、TMP 等组件在实际引擎工程内安装。

在 Unity Hub 中添加 `games/Test/client/Unity`，使用 Unity **2022.3.16f1** 打开。原来的 `games/Test` 工程已整体移入此目录，原场景 GUID 保留。无需再次创建工程。

Domain 当前为空。需要玩法功能时先在游戏中实现，经过实际维护后再考虑提取为公共框架能力。独立功能的规则/应用分工参考 [portable-feature 模板](../../templates/portable-feature/README.md)。

从仓库根目录执行：

```powershell
powershell -File tools/game.ps1 paths -Name Test
powershell -File tools/game.ps1 build -Name Test
powershell -File tools/game.ps1 sync -Name Test
powershell -File tools/game.ps1 run-server -Name Test
```

修改外部代码后先 `build`，再 `sync`。生成的 `Packages/com.awithgameframe.workspace` 不提交，由这两个命令重建；手写脚本放在客户端 `Assets` 中。

客户端的 `Assets/Scenes/SampleScene.unity` 已加入 Build Settings，场景中的 `GameHost` 挂载 `Assets/Scripts/Bootstrap/TestBootstrap.cs`，注册外部 `TestModule`。模块目前为空，因此场景只提供启动接线，没有游戏内容。后续场景切换或销毁宿主前应等待 `ShutdownAsync`。

Unity 原生导入与脚本编译已通过；PlayMode、断点与 Player 发布尚未验证。具体证据见[本次接入记录](../../docs/validation/2026-11-03-test-unity-integration.md)。完整工作区步骤见[游戏工作区指南](../../docs/getting-started/game-workspace.md)。
