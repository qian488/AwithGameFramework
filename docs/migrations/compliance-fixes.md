# 规范审查修复的兼容性说明

适用于 2026-10-30 规范审查后的代码。公共命名空间、现有方法签名、通用包依赖方向和存档封装格式保持兼容；以下行为需要调用方留意。审查范围和实际检查见[审查记录](../validation/2026-10-30-code-compliance.md)。

## 时间与日志

`TimeHelper.TimestampToDateTime` 及毫秒版本返回 `Kind=Utc`，修复非 UTC 时区下时间戳往返偏移。显示本地时间时调用 `ToLocalTime()`。`DateTimeToTimestamp` 和相对时间方法对 `Kind=Unspecified` 输入继续按本地时间解释；调用方应明确时间来源。

日志管理器的级别、分类、时间戳与堆栈设置会同步到文件输出器。`FileConfig` 中的文件大小、保留数量和清理间隔现在实际生效，重新启用文件日志沿用配置目录。级别和格式继续以顶层 `LoggingConfig` 为准；直接修改配置对象后重新 `Initialize(config)`，运行时调整优先使用管理器方法。

## 数据库与文件键

`DatabaseStorageProvider` 新增 `IAsyncDisposable`。直接使用时通过 `await using` 或 `await storage.DisposeAsync()` 确认驱动关闭；原同步 `Dispose()` 继续发起关闭并记录错误，不阻塞等待异步驱动。关闭失败后可显式重试。

`DataPersistenceAPI.ShutdownAsync()` 及初始化回滚优先等待提供者的异步释放，关闭失败返回 `Failed`。驱动不完成关闭时，调用也会等待，宿主需要保持其所需的调度环境运行。数据库连接或建表失败不会发布已初始化状态。

JSON/Binary 文件存储枚举键时移除完整 `FileExtension`。例如后缀 `.save.json` 的 `profile.save.json` 现在返回 `profile`，可直接传给 Load/Delete；不要再自行拼接或截断一次后缀。磁盘文件名不变。

## Unity 兼容存储

`PlayerPrefsStorage` 的泛型 `int`、`float`、`bool`、`string` 读写使用对应原生值；复杂对象仍使用 JsonUtility，字节接口仍使用 Base64。同一个 key 不应混用这些格式。旧错误实现已丢失的基础类型值无法自动恢复。

提供者持久化按 `KeyPrefix` 隔离的键索引，重开后枚举、统计和清空可以找到此前登记的数据。旧版本和外部直接写入的键无法自动全量发现；通过已知 key 加载或 `ExistsAsync` 确认存在后会补登记。`ClearAsync` 只删除本索引的数据，不调用 `PlayerPrefs.DeleteAll()`。

索引占用完整键前缀 `__AwithGameFrame.PlayerPrefs.Keys.v1__:`；业务键不能使用该命名空间。业务物理键继续沿用 `KeyPrefix_key`，需避免不同前缀与逻辑键组合出同名物理键。索引损坏时初始化失败并保留原数据。

初始化捕获实际使用的配置字段，之后修改调用方配置不会切换当前会话的键前缀或编码方式。需要变更时重新初始化。启用旧 XOR 但密钥为空会初始化失败，加解码异常不会静默返回原文。操作继续要求 Unity 主线程。

## 其他调用变化

`MathHelper.ClampAngle` 和 `ClampAngle180` 使用取模处理有限角度，避免极大值导致主线程循环卡死；NaN/Infinity 现在抛出 `ArgumentOutOfRangeException`。调用方应在使用外部数值时确认其有效性。

`ProtocolClient.BeginSession` / `Dispose` 遇到取消回调异常时，会先尝试取消其余在途请求，再聚合报告错误。会话失效的事实已生效，不能因为异常就继续交付旧会话结果。

AgentKit 任务/工具 ID、配置与协议标识符，以及 Godot 内容版本不再接受尾随换行。正常有效输入的生成格式保持不变。`tools/verify.ps1 -StaticOnly` 仅检查文档、依赖和元数据；完整默认命令仍包含生成检查、构建、测试和 Headless。
