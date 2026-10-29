# 示例与验收宿主

先选择运行环境，再读对应说明。共享业务源码只有一份，原生对象与设备接入由各宿主负责。

| 目的 | 入口 |
| --- | --- |
| 不安装编辑器，验证共享流程 | [Headless](Headless/README.md) |
| 运行 Unity 原生菜单、关卡、模态设置与进度恢复 | [Unity](Unity/README.md)，打开 `Assets/SharedDemo.unity` |
| 运行 Godot .NET 原生 Control 与同一应用逻辑 | [Godot](Godot/README.md)，运行 `Main.tscn` |
| 了解三种宿主共用的流程与双引擎组合应用 | [Shared](Shared/README.md) |
| 修改或生成强类型 HTTP 协议 | [Protocol](Protocol/README.md) |
| 构建并验证独立资源包 | `Content/` 与 [内容流水线](../docs/guides/content-pipeline.md) |

`Shared/Configuration` 保存配置 schema 与原始数据；`Protocol` 保存协议源及兼容性基线。生成命令见 [工具索引](../tools/README.md)，Generated 产物不得手改。

```powershell
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

示例是接入和验证入口，不是通用运行时源码的位置。不要将原生宿主代码反向引用到 `src`，不要从 Unity 插件或包缓存寻找框架业务源码。验证命令见 [参考](../docs/reference/verification.md)，实际平台与版本见 [记录](../docs/validation/latest.md)。
