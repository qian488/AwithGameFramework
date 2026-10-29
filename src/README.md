# 通用运行时

这里的 C# 包提供跨引擎逻辑，唯一运行时源码位于各包的 `Runtime/`。同一份源码由 `.csproj` 和 UPM asmdef 编译，基线为 .NET Standard 2.1 / C# 9。

| 要做的事 | 入口 | 依赖 |
| --- | --- | --- |
| 组合会话、模块和流程，管理事件、时间、普通对象池和资源租约 | [Core](AwithGameFrame.Core/README.md) | 标准库 |
| 读写文件、接入序列化/数据库、加载强类型配置 | [Data](AwithGameFrame.Data/README.md) | Core、Newtonsoft.Json |
| 管理视图/场景、导航、输入/音频、本地化、设置与版本化存档 | [Gameplay](AwithGameFrame.Gameplay/README.md) | Data、Core |
| 接入 HTTP、文件内容更新、WebSocket 与推送恢复 | [Networking](AwithGameFrame.Networking/README.md) | Core；可选 |

新应用从 `GameHost`、`GameModule` 和 `GameContext` 组合实例。Core 的旧全局入口集中在 [Compatibility](AwithGameFrame.Core/Runtime/Compatibility/README.md)，由旧应用独立管理生命周期。Networking 不进入 Data 或 Gameplay 的依赖。

引擎类型、原生资源和设备操作放在 [adapters](../adapters/README.md)，共享应用示例放在 [samples](../samples/README.md)。新增功能先读目标包说明和 [src 协作约定](AGENTS.md)，再按 [开发指南](../docs/guides/add-feature.md) 修改源码、测试和元数据。

在仓库根运行标准检查：

```powershell
powershell -File tools/verify.ps1
```

检查范围与单项命令见 [验证参考](../docs/reference/verification.md)，最近实际执行的版本与平台见 [验证记录](../docs/validation/latest.md)。
