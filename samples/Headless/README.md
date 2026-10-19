# 无图形宿主

在仓库根目录运行：

```powershell
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

示例在普通 .NET 8 进程中引用 Core、Data、Gameplay 和 `samples/Shared`，无需安装 Unity 或 Godot。程序先验证事件、JSON 存取、本地化和显式宿主时间源，再执行与两个引擎共用的[应用场景](../Shared/README.md)。项目将 `samples/Shared/Runtime/Generated/LevelConfig.json` 复制到输出目录的 `Configuration/LevelConfig.json`，共享场景使用同一份配置与业务源码。

场景通过 `ConfigTable<LevelConfig>` 校验 schemaHash 并加载配置，以 `VersionedSaveService<DemoSaveData>` 读取槽位 0 的进度，再通过 `NavigationStack` 完成 Menu → Playing → Menu：

1. 进入关卡 1，以 `GameHost.Tick(1.0 / 60)` 实际运行 60 帧。
2. 暂停后再 Tick 1 秒，确认游戏帧数仍为 60，再恢复运行。
3. 完成关卡，按配置增加 10 金币和一次完成记录；只有保存成功才发布新进度，并返回菜单。
4. 创建使用独立目录的宿主，确认其初始金币为 0，验证实例隔离。
5. 停止原宿主，以原存档目录重新创建宿主，确认金币和完成关卡数恢复一致。

成功输出包含 `SHARED_SCENARIO_PASSED coins=10 frames=60 phase=Menu`，进程返回 0；断言或执行失败时返回非零退出码。`DemoScenario.RunBlocking` 安装并泵送局部 `SynchronizationContext`，让异步生命周期在同一宿主线程执行，结束后恢复原上下文。

每次运行使用系统临时目录下的 `AwithGameFrame.Headless/<随机 ID>`。共享场景的首个主宿主通过 `HostEventJournal` 写入 `Session/host-events.jsonl`，记录启动、暂停、恢复、停止，以及模块阶段、耗时、错误和回滚信息。程序退出时会清理整个临时根目录，因此存档和诊断日志默认都不会保留。
