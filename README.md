# AwithGameFrame

一个以纯 C# 为基础的游戏框架。通用逻辑独立于引擎，Unity 和 Godot .NET 通过各自的宿主接入。

## 目录导航

| 目录 | 职责 |
| --- | --- |
| `src/AwithGameFrame.Core` | 实例宿主、流程、事件作用域、游戏时间/定时器、普通对象池、资源租约与有限缓存 |
| `src/AwithGameFrame.Data` | 持久化、序列化、数据库契约、强类型配置表与结构校验 |
| `src/AwithGameFrame.Gameplay` | 实例本地化、用户设置、版本化存档、应用导航、视图/绑定/场景、输入动作与音频策略 |
| `src/AwithGameFrame.Networking` | 可选 HTTP、文件内容更新、WebSocket 会话/心跳/重连与显式推送恢复契约 |
| `adapters/unity` | 无旧插件依赖的 Unity 实例宿主、UI/场景/输入/音频后端、Resources/AssetBundle |
| `adapters/unity-compat` | 旧 Unity 配置/Mono/静态服务、PlayerPrefs 与 Foundation 兼容入口，需要 UniTask |
| `adapters/unity-dotween`、`adapters/unity-sqlite` | 可选插件接入，分别需要真实 DOTween、Mono.Data.Sqlite |
| `adapters/godot` | Godot .NET 宿主、后台资源加载、PCK、Control/场景/输入/音频后端 |
| `samples/Shared` | 三种宿主的共享流程，以及双引擎共用的完整交互应用 |
| `samples/Headless` | 无图形 .NET 宿主，验证共享逻辑 |
| `samples/Unity` | 原 Unity 工程，包含场景、资源、第三方插件和演示脚本 |
| `samples/Godot` | 可运行的原生 Control 界面与 Windows 导出预设 |
| `samples/Protocol` | 协议定义、兼容性基线及生成的 DTO/endpoint |
| `tests` | 无需 Unity 的自动化测试 |
| `tools` | 配置/协议生成、AssetBundle/PCK 构建、内容发布安装、兼容性检查与双引擎验收 |
| `agent` | 开发任务/上下文约定、工具目录、权限与确认规则、任务和交接模板 |
| `templates` | 可编译测试的功能结构模板，供人类与 Agent 复制扩展 |
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

运行 `Assets/SharedDemo.unity` 体验共享流程。Godot **4.5.2 .NET** 打开 `samples/Godot/project.godot`，构建 C# 后运行 `Main.tscn`。两端使用同一份 `DemoExperience`，组合菜单、关卡、模态设置、语言/音量/按键保存与存档恢复；控件、原生实例和设备操作由各引擎后端负责。Headless 保留同一共享应用的无图形流程。

修改配置/协议源后执行 `tools/generate-config.ps1`、`tools/generate-protocol.ps1`。标准检查使用 `-Check` 验证生成结果与协议兼容性，不会自动覆盖手改产物。

## 基础能力与边界

本仓库也提供开发期 [AgentKit](agent/README.md)，帮助人类与 AI 协作维护游戏项目：任务有读写范围和工作区基线，上下文按路径和预算收集，固定工具配方产生计划、确认记录、执行日志和评测报告。机器检查与人工验收分别记录，不依赖特定模型或 API 账号。可从 [任务模板](agent/templates/tasks/docs-only.json) 和 [函数式领域/命令式应用功能模板](templates/portable-feature/README.md) 开始；入口为 `powershell -File tools/agent.ps1 help`。它位于开发工具层，游戏运行时无需加载。

每个 `GameHost` 拥有独立的服务表、类型事件总线、游戏时钟和定时器。`host.Tick(unscaledSeconds)` 输入未缩放秒数，模块收到缩放后的 delta；暂停时两个游戏时间域都冻结，主线程调度和维护回调继续工作，使允许继续播放的音效能正常回收。普通对象池按引用检查所有权，可通过租约归还。旧静态 API 保留兼容，使用实例宿主不会自动隔离旧静态管理器。

`ResourceStore` 提供共享加载、作用域、可选的有限 idle LRU 与预加载；预算只约束没有活跃租约的资源，原生内存大小由后端估算。Unity 提供 Resources 和 AssetBundle 后端，Godot 提供同步/后台加载与 PCK 挂载。`ViewBindingScope` 通过显式 getter/setter 绑定 `INotifyPropertyChanged`；本地化文本和视图绑定随作用域解绑。输入先经 UI 消费，再广播未消费的玩法动作。

`tools/build-unity-content.ps1` / `build-godot-content.ps1` 生成引擎资源及地址目录，`publish-content.ps1` 发布文件和大小/SHA-256 清单，`Networking.Content` 校验下载、增量复用、续传并原子激活。双引擎脚本通过真实 HTTP 安装后加载资源包，接入和生命周期见 [资源流水线](docs/content-pipeline.md)。`Networking.Realtime` 提供 WebSocket 传输、心跳和有界重连，通过 `IPushRecovery` 恢复推送顺序；实际鉴权、恢复接口、服务端实现与业务幂等由项目接入。

通用包以 `.NET Standard 2.1` 和 C# 9 为兼容基线。Godot 接入使用 .NET 8；其他语言通过独立桥接/实现复用数据与协议契约。平台 SDK、Addressables、云存档、联网业务和代码热更新按产品需求接入；实测平台与剩余范围见验证记录。

开发入口见 [GUIDE.md](GUIDE.md)，迁移旧项目见 [迁移说明](docs/migration.md)，agent 从 [AGENTS.md](AGENTS.md) 开始。

建设范围和剩余工作见 [路线图](docs/roadmap.md)，实际验收入口见 [验证说明](docs/validation.md)。
