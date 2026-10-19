# AwithGameFrame

一个以纯 C# 为基础的游戏框架。通用逻辑独立于引擎，Unity 作为宿主与适配层接入。

## 目录导航

| 目录 | 职责 |
| --- | --- |
| `src/AwithGameFrame.Core` | 模块生命周期、服务注册、事件、日志、时间契约与通用工具 |
| `src/AwithGameFrame.Data` | Task 持久化接口、JSON/二进制文件、序列化与数据库驱动契约 |
| `src/AwithGameFrame.Gameplay` | 本地化数据与语言切换、存档槽位 |
| `adapters/unity` | Unity 时间与日志接入、Mono、场景、UI、音频、输入、资源、对象池、PlayerPrefs |
| `samples/Headless` | 无图形 .NET 宿主，验证共享逻辑 |
| `samples/Unity` | 原 Unity 工程，包含场景、资源、第三方插件和演示脚本 |
| `tests` | 无需 Unity 的自动化测试 |
| `tools` | 构建与架构边界检查 |
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

通用包以 `.NET Standard 2.1` 和 C# 9 为兼容基线，可由兼容的 C# 引擎、工具或服务端引用。目前提供 Unity 适配和 .NET 无图形示例；其他引擎需要实现自己的宿主接入。C++/JavaScript 引擎仍需要语言桥接。

开发入口见 [GUIDE.md](GUIDE.md)，迁移旧项目见 [迁移说明](docs/migration.md)，agent 从 [AGENTS.md](AGENTS.md) 开始。
