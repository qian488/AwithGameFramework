# AwithGameFrame.Core

面向 `.NET Standard 2.1` / C# 9 的引擎无关内核。可通过项目引用用于 .NET，也可作为本地包用于引擎适配器。`Runtime/AwithGameFrame.Core.asmdef` 禁止引擎引用。

包含模块生命周期、启动编排、服务定位器、事件、日志、配置数据基类、字符串/时间工具，以及异步/序列化契约。`TaskAsyncProvider` 提供标准 `Task` 实现。游戏对象、场景、输入、资源及宿主回调属于 `adapters/`。

修改前先在 `Runtime/` 查找对应模块；验证命令：

```powershell
dotnet build src/AwithGameFrame.Core/AwithGameFrame.Core.csproj
```

## 宿主接入点

- `LoggingHost.Output`：同步日志回调 `Action<LogLevel, string, object>`，默认写入标准输出/错误。宿主负责自身的线程切换和上下文对象处理。
- `LoggingHost.DataPath`：默认可写数据目录，日志默认写到该目录下的 `Logs/`。在初始化日志前配置，也可向 `LoggingAPI.Initialize` 传入独立日志目录。
- `TimeHelper.TimeSource`：由宿主实现 `ITimeSource`，提供模拟时间、帧间隔、时间缩放和目标帧率。日历/时间戳方法无需宿主；模拟时间方法在未注入时明确抛出异常。
- 每帧调用 `PerformanceMonitor.GetInstance().Tick(unscaledDeltaTime)`，由宿主提供秒数。计时器使用单调 `Stopwatch`；内存/GC 指标使用 BCL。渲染统计在引擎适配器采集。
- 定期调用 `LoggingManager.GetInstance().Update()` 清理历史文件；退出时调用 `ShutdownFileLogger()`。

事件使用标准 `System.Action`（0–4 个参数）；同一事件 ID 必须保持相同参数签名，否则注册、移除或触发时抛出 `InvalidOperationException`。原有方法名和命名空间保留；直接传入引擎委托变量的调用方需转换为标准委托并保存转换后的实例用于注销。

`LogMode.Console` 是宿主控制台模式；`UnityDebug` 保留为过时兼容别名。枚举数值保持兼容。`FrameworkLog` / `Both` 同时向宿主和文件输出，`None` 关闭输出。

这里禁止添加引擎 SDK 和第三方运行时依赖。需要宿主能力时，先在内核声明最小接口，再由适配器实现；保留迁移文件的 `.meta` GUID。

## 生命周期与失败恢复

在启动前注册所有模块。`FrameworkBootstrap.Bootstrap()` 按优先级降序初始化，再执行全部后初始化；已运行时重复调用不会再次初始化。启动中、关闭中或运行后新增模块会抛出 `InvalidOperationException`，结束当前会话后可重新注册。

初始化或后初始化失败时，按逆序关闭所有已经开始初始化的模块，包括初始化到一半抛错的模块；因此 `IModule.Shutdown()` 必须能清理部分初始化状态。注册表保留，可修正依赖后重试。回滚成功时保留原始异常；回滚也失败时抛出 `AggregateException`，第一项为原始启动异常。

`Shutdown()` 尝试关闭全部已启动模块，即使某个模块抛错也继续，然后清空本次注册和启动状态；清理异常统一通过 `AggregateException` 返回。重复关闭不会再次清理模块，尚未启动的注册只会被移除。模块回调不能重入启动或在初始化途中关闭；关闭回调中的重复关闭安全返回。`EventCenter.Shutdown()` 会释放事件监听；派发中的监听变更在下一次派发生效。

`ServiceLocator` 的工厂需要返回非空实例。循环依赖或返回 `null` 会明确抛出 `InvalidOperationException`；失败工厂保留，以便依赖恢复后重试。生命周期编排和服务注册由宿主在同一线程顺序调用，未提供并发注册/解析保证。
