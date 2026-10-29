# AgentKit 工具实现

这是 .NET 8 开发工具，不被游戏运行时、引擎 adapter 或 UPM 引用。使用与任务格式见 [入口](../../agent/README.md)。

| 文件 | 责任 |
| --- | --- |
| `Models`、`TaskFiles` | 版本化任务数据、验证、原子创建和保存 |
| `Workspace` | Git 状态/索引指纹、基线变化、路径边界 |
| `ModuleCatalog` | 有限目录发现、模块路径/依赖/工具契约、模块目录摘要 |
| `ContextBundle` | 有限文本选择、指令来源与内容摘要、新鲜度 |
| `ToolRunner` | 固定配方、具体计划、确认、限时执行与输出 |
| `Evidence`、`Evaluation` | 摘要、本地事件、机器检查与人工评审 |
| `Program` | CLI 路由与每任务互斥，不在这里加入领域规则 |

任务与清单的 `schemaVersion` 当前为 1；修改数据语义时明确升级/迁移，不默默重解释已有确认。执行器只接受登记的 `dotnet` / PowerShell 参数数组，新增工具要维护 `agent/tools.json`、任务模板、超时、确认策略和测试。此工具不提供进程权限隔离或防同账号篡改的日志。

回归位于 `tests/AwithGameFrame.Core.Tests/AgentKitTests.cs`、`AgentWorkspaceTests.cs` 和 `AgentCatalogTests.cs`，使用独立临时 Git 仓库，覆盖真实子进程、工作区边界、模块上下文选择、契约和计划失效，以及既有任务兼容性。标准验证还构建测试功能模板；不要为测试创建虚假的 Unity/Godot 类型。

```powershell
dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter 'FullyQualifiedName~AgentKitTests|FullyQualifiedName~AgentWorkspaceTests|FullyQualifiedName~AgentCatalogTests'
```
