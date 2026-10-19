# 从旧 Unity 工程迁移

## 打开工程与找源码

Unity Hub 打开 samples/Unity，原 ProjectSettings、场景、资源和插件都在该目录。仓库根 AwithGameFrame.sln 用于通用库、NUnit 和无图形宿主；Unity 自己生成的解决方案仍位于 Unity 工程目录。

| 旧位置 | 新位置 |
| --- | --- |
| Assets、Packages、ProjectSettings | samples/Unity 下同名目录 |
| Core 的 Base/Bootstrap/DI/Event/Logging 和通用接口工具 | src/AwithGameFrame.Core/Runtime |
| Core 的 Mono/Scene、Unity 类型接口和 Inspector 配置 | adapters/unity/Runtime/Engine |
| Foundation 的通用持久化、NewtonsoftJsonProvider | src/AwithGameFrame.Data/Runtime |
| Foundation 的 UI/音频/输入/资源/对象池与 Unity 提供者 | adapters/unity/Runtime/Services |
| Extensions 的 SaveGame、Localization 管理逻辑 | src/AwithGameFrame.Gameplay/Runtime |
| ScriptableObject 本地化资源、Unity 数据源、调试面板 | adapters/unity/Runtime/Features |
| 框架内 Examples、Extensions/Tests | samples/Unity/Assets/Examples |
| Assets/Test/Unit | tests/AwithGameFrame.Core.Tests |
| Doc、旧 README/GUIDE | docs/legacy |

原脚本和资源的 .meta/GUID 随文件迁移。新文件补充独立 .meta。原 Core asmdef 的 GUID 随可移植 Core 移动；自定义 asmdef 使用 Unity 管理器时，需要额外引用 AwithGameFrame.Unity；使用持久化时引用 AwithGameFrame.Data；使用存档槽位或本地化时引用 AwithGameFrame.Gameplay。依赖 Unity UI 的程序集显式引用 UnityEngine.UI。

多数公共命名空间保持不变，例如 AwithGameFrame.Foundation.DataPersistence。按程序集反射、保存程序集限定类型名或维护 linker 配置的代码，需要同步更新程序集归属。

## 异步与事件

持久化和存档槽位 API 返回 Task。现有 await 调用可继续使用；显式声明为 UniTask 的变量或使用其扩展方法的代码需要修改，在 Unity 边界可用 AsUniTask() 转换。

EventCenter 使用 System.Action 及泛型 Action。方法组与 lambda 可继续传入；显式 UnityAction 变量需换成 Action。不要用临时 lambda 包装后直接移除 listener，应保留同一个委托实例。相同事件名使用不同参数签名时，现改为明确抛出 InvalidOperationException。

原 UniTaskProvider 实际基于 Task 实现，已改为 Core.Providers.TaskAsyncProvider；Unity 提供过时名称的兼容类。

## 日志和时间

UnityPlatform 自动接入 Unity 日志、Application.persistentDataPath 和 Unity 时间；UnityFrameDriver 驱动性能监控与日志刷新。其他宿主设置 LoggingHost.Output、LoggingHost.DataPath、TimeHelper.TimeSource。纯日历时间和性能计时无需游戏时钟；游戏时间方法在没有宿主时明确失败。

LogMode.Console 保持原 UnityDebug 的枚举值，旧名称为兼容别名。通用性能监控不读取引擎渲染统计。

## 存储目录与提供者

普通宿主显式设置 DataPersistenceConfig.DataRootPath。默认相对路径 Data 按进程工作目录解析，二进制默认在其 BinaryData 子目录。

Unity 可调用：

```csharp
var config = UnityDataPersistence.CreateConfig();
await UnityDataPersistence.InitializeAsync(config, enableDatabase: true);
```

CreateConfig / UseUnityPaths(config) 使用旧 JSON 目录 persistentDataPath/Data 和旧二进制目录 persistentDataPath/BinaryData。要指定其他目录，直接传入完整配置，InitializeAsync 不改写显式配置。

默认数据库在 DataRootPath/DatabaseName；旧数据库可能位于工程工作目录，迁移前通过 config.StorageConfigs[StorageType.Database] = new StorageConfig { databasePath = existingPath } 指定已有文件。本仓库原 game_data.db 随示例工程移到 samples/Unity/game_data.db。

Unity 自动安装 PlayerPrefs 与 SQLite 驱动工厂；数据库存储仅在 enableDatabase 为 true 时注册。DataPersistenceAPI 的默认提供者只有 JSON/二进制。初始化后不能替换提供者；等待业务操作结束并调用 ShutdownAsync，再注册和初始化。

SQLiteDatabaseProvider 现仅负责绑定 Unity 工程内的 Mono.Data.Sqlite 连接；数据库操作移至 Data 的 AdoDatabaseProvider，通过连接工厂接入宿主驱动，并保留原 SQLite 查询约定。

SaveSettingsAsync/LoadSettingsAsync 现在使用配置的默认存储。旧版强制使用 PlayerPrefs。需要继续读取旧 Unity 用户设置时显式调用 SaveAsync/LoadAsync，并传 StorageType.PlayerPrefs；之后可按业务规则迁移到新存储。SaveGame 元数据也从默认存储恢复；旧版只从 PlayerPrefs 推测槽位，未保存可靠的恢复索引，必要时由项目迁移已有槽位。

普通字段 JSON 可读取，新增格式支持可写属性和基础类型；Unity 私有 SerializeField 字段需要公开 DTO 或显式 Newtonsoft 注解。MessagePack/Protobuf 需要注册真实编码器，不再假装实现或静默回退。具体文件格式兼容说明以 Data README 为准。

压缩序列化器写入 v2，继续读取 v1 的布尔值、原始字符串和区域数字；JSON 编码器也能读取旧工厂写出的 v1 二进制/Base64 内容。BinaryFileStorage 的独立文件封装保持 v1。文件存储键现在是单个文件名；旧版带目录分隔符的键应迁移为独立存储目录与扁平键名。

## 验证

运行 tools/verify.ps1，检查通用依赖、asmdef 图、UPM 路径、编译、自动测试和无图形示例。Unity 编译需要编辑器，可运行：

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath '你的 Unity.exe 路径'
```

该脚本使用 artifacts 中的隔离工程副本和指定编辑器，不改写示例工程记录的版本；导入日志保留在 artifacts/UnityValidation。编辑器许可证与包服务由本机 Unity 提供。
