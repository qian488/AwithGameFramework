# AwithGameFrame

以纯 C# 为基础的游戏框架，提供独立于引擎的运行时，以及 Unity、Godot .NET 的适配和共享应用示例。

## 开始使用

安装 .NET 9 SDK 和 .NET 8 Runtime，在仓库根目录运行：

```powershell
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

成功输出包含 `SHARED_SCENARIO_PASSED`。示例执行关卡、暂停、保存与重建恢复，无需安装引擎。步骤和输出含义见 [首次运行](docs/getting-started/headless.md)。

Unity 打开 `samples/Unity`，运行 `Assets/SharedDemo.unity`；Godot **4.5.2 .NET** 打开 `samples/Godot/project.godot`，构建 C# 后运行。两端使用同一份应用逻辑，原生 UI、设备和资源由各自适配器负责。Unity 工程记录版本为 **2022.3.40f1c1**，实际验证版本和平台见 [验证记录](docs/validation/latest.md)。

## 按任务查找

| 要做什么 | 入口 |
| --- | --- |
| 了解能力、依赖和限制 | [架构概览](docs/architecture/overview.md)、[路线图](docs/roadmap.md) |
| 使用宿主、数据、游戏服务或网络能力 | [通用运行时](src/README.md) |
| 接入 Unity 或 Godot | [引擎适配](adapters/README.md)、[示例](samples/README.md) |
| 修改框架或增加功能 | [开发指引](GUIDE.md)、[功能模板](templates/README.md) |
| 生成配置/协议、构建资源、执行验证 | [工具](tools/README.md) |
| 与 AI 协作并保存任务上下文和验证证据 | [AgentKit](agent/README.md)、[协作约定](AGENTS.md) |
| 从旧版升级 | [迁移指南](docs/migrations/README.md) |
| 查找全部当前文档 | [文档索引](docs/README.md) |

通用包使用 .NET Standard 2.1 / C# 9，运行时源码统一位于各包的 `Runtime`。`src` 不引用引擎程序集；引擎类型与生命周期位于 `adapters`。开发期 AgentKit、工具和模板不进入游戏运行时依赖。

## 验证

```powershell
powershell -File tools/verify.ps1
# PowerShell 7 / CI: pwsh -File tools/verify.ps1
```

标准检查覆盖文档本地链接、依赖/包元数据、生成产物、AgentKit 契约、构建、测试、功能模板和 Headless。真实引擎编译、运行和导出使用独立工具，命令与范围见 [验证参考](docs/reference/verification.md)。
