# AwithGameFrame

在 `games/<Name>` 开发游戏，使用独立 C# 运行时、Unity 或 Godot .NET 客户端，以及按需接入的 .NET 服务端。涉及框架能力范围时查阅 [产品方向](docs/product-direction.md)。

## 开始使用

安装 .NET 9 SDK 和 .NET 8 Runtime，在仓库根目录为自己的游戏生成外部代码骨架：

```powershell
powershell -File tools/game.ps1 new -Name MyGame -Engine unity
# 使用 Godot 时将 unity 改为 godot
```

在 `games/MyGame/client/Unity` 或 `games/MyGame/client/Godot` 手动创建选定引擎的工程。游戏代码放在 `games/MyGame/src`，服务端放在 `games/MyGame/server`；外部构建与客户端接入见[游戏工作区入门](docs/getting-started/game-workspace.md)。命令不会代建引擎工程。

如需先了解现有模块如何组合，可运行无引擎示例：

```powershell
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

成功输出包含 `SHARED_SCENARIO_PASSED`。示例执行关卡、暂停、保存与重建恢复，无需安装引擎。步骤和输出含义见 [首次运行](docs/getting-started/headless.md)。

Unity 打开 `samples/Unity`，运行 `Assets/SharedDemo.unity`；Godot **4.5.2 .NET** 打开 `samples/Godot/project.godot`，构建 C# 后运行。两端使用同一份应用逻辑，原生 UI、设备和资源由各自适配器负责。Unity 工程记录版本为 **2022.3.40f1c1**，实际验证版本和平台见 [验证记录](docs/validation/latest.md)。

## 按任务查找

| 要做什么 | 入口 |
| --- | --- |
| 了解产品形态与框架调整依据 | [产品方向](docs/product-direction.md) |
| 开发自己的游戏与服务端 | [游戏工作区](games/README.md)、[创建与接入步骤](docs/getting-started/game-workspace.md) |
| 了解能力、依赖和限制 | [架构概览](docs/architecture/overview.md)、[路线图](docs/roadmap.md) |
| 使用宿主、数据、游戏服务或网络能力 | [通用运行时](src/README.md) |
| 接入 Unity 或 Godot | [引擎适配](adapters/README.md)、[示例](samples/README.md) |
| 修改框架或增加功能 | [开发指引](GUIDE.md)、[功能模板](templates/README.md) |
| 生成配置/协议、构建资源、执行验证 | [工具](tools/README.md) |
| 与 AI 协作并保存任务上下文和验证证据 | [AgentKit](agent/README.md)、[协作约定](AGENTS.md) |
| 查找全部当前文档 | [文档索引](docs/README.md) |

## 验证

```powershell
powershell -File tools/verify.ps1
# PowerShell 7 / CI: pwsh -File tools/verify.ps1
```

标准检查覆盖文档本地链接、依赖/包元数据、生成产物、AgentKit 契约、构建、测试、功能模板和 Headless。真实引擎编译、运行和导出使用独立工具，命令与范围见 [验证参考](docs/reference/verification.md)。
