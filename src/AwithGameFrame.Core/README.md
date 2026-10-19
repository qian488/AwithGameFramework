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
