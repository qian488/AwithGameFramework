# 共享应用示例

`Runtime/DemoApplication.cs` 是 Unity、Godot .NET 和 Headless 共用的应用模块。它使用独立 `GameHost`、配置表、NavigationStack 和 VersionedSaveService 实现菜单 → 关卡 → 完成存档 → 返回菜单，并在重新创建宿主后恢复进度。图形、输入、节点和实际资源后端属于各引擎。

`Runtime/DemoExperience.cs` 是两个图形宿主共用的完整组合：菜单/关卡 `ViewFlow`、真实场景与循环音频、输入先经 UI 消费、本地化绑定与持久用户设置。空格执行关卡动作，Esc 打开模态设置；可修改语言、音量、静音和空格/回车绑定，保存后在新会话恢复。模态设置打开后，持键不会继续交付玩法；关闭时仍按住同一键也不会产生新动作。进度与设置分别存放在宿主提供目录的 `progress`、`settings` 子目录。

`DemoExperience.Install` 接收各引擎的资源、视图、场景、音频和输入后端，安装同一依赖图。停止先关闭视图及绑定，再释放场景/音频，最后关闭资源。`DemoExperienceScenario.RunAsync` 在真实图形宿主中运行关卡加载/卸载、音频播放/清理、模态持键与设置保存断言，成功标记为 `COMPOSED_APPLICATION_PASSED`；NUnit 额外验证新宿主恢复全部设置与进度、打开设置时退出的清理顺序。`DemoInputBackend.ScenarioFrame` 只用于验收的确定帧注入，普通运行直接读取真实引擎输入。

`DemoScenario.RunAsync` 在引擎同步上下文运行真实流程断言；`RunBlocking` 为控制台提供单线程同步上下文。实际项目持续泵宿主 Tick，避免在引擎线程 `.Wait()` / `.Result` 阻塞异步调度。

配置源位于 `Configuration/schema.json` 与 `Configuration/Data`。运行 `tools/generate-config.ps1` 生成类型与两个引擎的数据副本，`-Check` 验证结果。生成目录由工具拥有；删除/重命名表时需显式移除旧产物及相应 `.meta`，多余产物会使检查失败。

流程验收同时写 `host-events.jsonl`，包含实例生命周期、模块耗时、错误及回滚信息。该诊断文件不属于游戏进度封装。
