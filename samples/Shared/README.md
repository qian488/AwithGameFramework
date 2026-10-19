# 共享应用示例

`Runtime/DemoApplication.cs` 是 Unity、Godot .NET 和 Headless 共用的应用模块。它使用独立 `GameHost`、配置表、NavigationStack 和 VersionedSaveService 实现菜单 → 关卡 → 完成存档 → 返回菜单，并在重新创建宿主后恢复进度。图形、输入、节点和实际资源后端属于各引擎。

`DemoScenario.RunAsync` 在引擎同步上下文运行真实流程断言；`RunBlocking` 为控制台提供单线程同步上下文。实际项目持续泵宿主 Tick，避免在引擎线程 `.Wait()` / `.Result` 阻塞异步调度。

配置源位于 `Configuration/schema.json` 与 `Configuration/Data`。运行 `tools/generate-config.ps1` 生成类型与两个引擎的数据副本，`-Check` 验证结果。生成目录由工具拥有；删除/重命名表时需显式移除旧产物及相应 `.meta`，多余产物会使检查失败。

流程验收同时写 `host-events.jsonl`，包含实例生命周期、模块耗时、错误及回滚信息。该诊断文件不属于游戏进度封装。
