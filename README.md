# AwithGameFrame

一个以纯 C# 为基础的游戏框架。通用逻辑独立于引擎，Unity 和 Godot .NET 通过各自的宿主接入。

## 目录导航

| 目录 | 职责 |
| --- | --- |
| `src/AwithGameFrame.Core` | 实例宿主、异步模块依赖、流程、调度、资源租约与现有兼容 API |
| `src/AwithGameFrame.Data` | 持久化、序列化、数据库契约、强类型配置表与结构校验 |
| `src/AwithGameFrame.Gameplay` | 本地化、版本化存档/迁移/备份恢复、应用导航 |
| `src/AwithGameFrame.Networking` | 可选 HTTP/协议客户端、超时取消、会话隔离与 codec |
| `adapters/unity` | Unity 时间与日志接入、Mono、场景、UI、音频、输入、资源、对象池、PlayerPrefs |
| `adapters/godot` | Godot .NET Node 宿主、暂停恢复、原生资源后端 |
| `samples/Shared` | 三种宿主共用的配置、菜单、关卡、存档与恢复流程 |
| `samples/Headless` | 无图形 .NET 宿主，验证共享逻辑 |
| `samples/Unity` | 原 Unity 工程，包含场景、资源、第三方插件和演示脚本 |
| `samples/Godot` | 可运行的原生 Control 界面与 Windows 导出预设 |
| `samples/Protocol` | 协议定义、兼容性基线及生成的 DTO/endpoint |
| `tests` | 无需 Unity 的自动化测试 |
| `tools` | 配置/协议生成、兼容性检查、构建与双引擎验收 |
| `docs` | 架构决策、迁移指南、历史调研 |

## 开始使用

安装 .NET 9 SDK 与 .NET 8 Runtime，然后在仓库根目录运行：

```powershell
dotnet build AwithGameFrame.sln
dotnet test AwithGameFrame.sln --no-build
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

完整检查（架构边界、编译、测试、无图形示例）：

```powershell
powershell -File tools/verify.ps1
# PowerShell 7 / CI: pwsh -File tools/verify.ps1
```

Unity Hub 打开 `samples/Unity`。工程记录的编辑器版本为 **2022.3.40f1c1**，通过本地 UPM 引用同一份 `src` 源码与 Unity 适配包。

运行 `Assets/SharedDemo.unity` 体验共享流程。Godot **4.5.2 .NET** 打开 `samples/Godot/project.godot`，构建 C# 后运行 `Main.tscn`。两端使用同一份 `DemoApplication`，UI 和资源仍由各引擎负责。

修改配置/协议源后执行 `tools/generate-config.ps1`、`tools/generate-protocol.ps1`。标准检查使用 `-Check` 验证生成结果与协议兼容性，不会自动覆盖手改产物。

通用包以 `.NET Standard 2.1` 和 C# 9 为兼容基线。Godot 接入使用 .NET 8；其他语言通过独立桥接/实现复用数据与协议契约。平台 SDK、远程资源发布、云存档、联网业务和代码热更新按产品需求独立接入，当前基础能力不代表所有平台都已验证。

开发入口见 [GUIDE.md](GUIDE.md)，迁移旧项目见 [迁移说明](docs/migration.md)，agent 从 [AGENTS.md](AGENTS.md) 开始。

建设范围和剩余工作见 [路线图](docs/roadmap.md)，实际验收入口见 [验证说明](docs/validation.md)。
