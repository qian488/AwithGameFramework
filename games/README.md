# 具体游戏工作区

克隆仓库后，在这里维护具体游戏。建议每个游戏使用独立克隆；工具也支持在同一仓库按名称隔离多个游戏。公共框架仍放在 `src`，`samples` 继续作为示例。

```powershell
powershell -File tools/game.ps1 new -Name MyGame -Engine unity
# 或将 unity 改为 godot
```

命令生成游戏外部 C# 骨架及 .NET 服务端，不创建 Unity/Godot 工程。由开发者在以下目录创建选定引擎的工程：

| 引擎 | 工程创建位置 |
| --- | --- |
| Unity | `games/MyGame/client/Unity` |
| Godot .NET | `games/MyGame/client/Godot` |

游戏规则放在 `games/MyGame/src`，服务端入口放在 `games/MyGame/server`。配置和协议源分别放在 `configuration`、`protocol`；Spine、TMP 等引擎组件由开发者在客户端工程中安装。

创建、构建、客户端接入、调试及服务端启动的完整步骤见[游戏工作区入门](../docs/getting-started/game-workspace.md)。目录和产物边界的原因见[工作区架构决定](../docs/architecture/decisions/0002-game-workspace-and-binary-integration.md)。
