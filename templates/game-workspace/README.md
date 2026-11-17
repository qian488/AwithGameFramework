# __GAME__ 游戏工作区

这里维护本游戏的引擎外 C# 代码与 .NET 服务端。框架包仍在仓库根 `src`；本游戏不预置玩法或网络业务。在 `templates/game-workspace` 中阅读本文件时，它是待 `tools/game.ps1 new` 替换名称的模板，不参与根解决方案构建。

| 区域 | 职责 |
| --- | --- |
| [Domain](src/__GAME__.Domain/README.md) | 游戏自己的纯领域计算，无引擎或框架依赖 |
| [Application](src/__GAME__.Application/README.md) | 状态提交、用例编排、框架服务组合及客户端/服务端共享契约 |
| [Server](server/__GAME__.Server/README.md) | .NET 进程配置、日志、生命周期及专属基础设施接线 |

`game.json` 记录游戏名称、引擎和构建入口，生成的项目通过相对路径引用本克隆的框架。客户端位置和接入命令见仓库的游戏工作区指南，Spine、TMP 等组件在实际引擎工程内安装。

开发者按选定引擎自行在仓库的 `games/__GAME__/client/Unity` 或 `games/__GAME__/client/Godot` 创建工程；创建命令不生成这些引擎文件。通常只创建一个主引擎客户端。

本模板中的 Domain 没有示例业务。需要玩法功能时先在游戏中实现，经过实际维护后再考虑提取为公共框架能力。独立功能的规则/应用分工参考另一个 [portable-feature 模板](../../templates/portable-feature/README.md)。

从仓库根目录执行：

```powershell
powershell -File tools/game.ps1 paths -Name __GAME__
powershell -File tools/game.ps1 build -Name __GAME__
powershell -File tools/game.ps1 sync -Name __GAME__
powershell -File tools/game.ps1 run-server -Name __GAME__
```

先由开发者创建客户端，再执行 `sync`。完整创建、引用与调试步骤见[游戏工作区指南](../../docs/getting-started/game-workspace.md)。
