# 日志与性能监控

日志实现仅依赖 BCL。`LoggingAPI` 提供预设和便捷入口，`LoggingManager` 负责级别/分类过滤、格式化和文件输出。`FrameworkLogger` 保留已有调用方式。

```csharp
LoggingHost.DataPath = writableDataDirectory;
LoggingHost.Output = (level, message, context) => host.WriteLog(level, message, context);
LoggingAPI.Initialize(LoggingAPI.Preset.Development);
LoggingAPI.Info(LogCategory.Core, "Host ready");
```

默认回调使用 `Console.WriteLine` / `Console.Error.WriteLine`；回调同步执行于发起调用的线程。请在日志初始化前设置输出和默认数据目录。文件默认为 `LoggingHost.DataPath/Logs/`，也可在初始化时指定目录。

`PerformanceMonitor` 的计时器使用 `Stopwatch`，内存和 GC 数据使用 BCL。每帧由宿主调用 `Tick(unscaledDeltaTime)` 更新帧率；`CurrentFrameRate` 是最近一帧的帧率，`AverageFrameRate` 是最近完整采样窗口的帧率。渲染指标应在适配器中采集。

`LoggingManager.Update()` 负责定期清理日志，退出时调用 `ShutdownFileLogger()`。宿主生命周期的安排参见包根目录 README。

通过 `LoggingManager` 初始化时，级别、分类、时间戳与堆栈开关以顶层 `LoggingConfig` 为准；运行时通过管理器对应的方法和属性调整。修改配置对象的字段后应重新调用 `Initialize(config)` 应用到文件输出器。`FileConfig.MaxFileSize`、`MaxFiles`、`CleanupInterval` 控制轮转与保留，大小至少 1 MB，保留数至少 1，清理间隔须为有限正数（秒）。重新启用文件日志沿用配置中的目录。
