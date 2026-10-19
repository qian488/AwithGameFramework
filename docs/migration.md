# 从旧 Unity 工程迁移

## 实例宿主与 Godot

新应用使用 `AwithGameFrame.Core.Hosting.GameHost`，通过 `IGameModule.Id/Dependencies` 声明异步启动依赖；普通服务注册在 `host.Services`，模块通过 `GameContext` 访问。旧 `FrameworkBootstrap/ServiceLocator` 保留兼容，旧静态管理器没有自动获得实例隔离。每个会话创建新宿主，同一宿主停止后不重启。

Unity 可继承 `AwithGameFrame.Unity.Hosting.GameHostBehaviour`；Godot .NET 使用 `GameHostNode`。启动/暂停/停止期间也需在创建线程持续 Tick（秒），处理原生资源释放和主线程投递。删除宿主对象、切走所属场景或主动退出前先等待 `ShutdownAsync`；引擎销毁回调无法保证等待所有异步操作。

共享示例为 Unity `Assets/SharedDemo.unity`、Godot `samples/Godot/Main.tscn` 和 Headless。导航使用 `NavigationStack`，flow 的 Enter/Exit 管理页面与资源；返回或回滚会重新 Enter。

## Unity 可靠性修正

旧 `FrameworkEntry` 暂停时保留模块，暂停/恢复输入和音频。模块开关在启动时消费，修改需重启会话；运行参数由帧驱动每 0.25 秒检查。

池的异步创建直接登记借出，重复归还忽略；预热只实例化，不改变 Prefab。Clear 销毁池拥有的空闲及借出对象，旧引用随后失效。UI 同名在途打开共用加载；Hide、失败或过期请求回调 null，迟到对象回收。音效并发上限包含在途加载，超额请求回调 null；音频与输入 Shutdown 解除订阅。

新增资源租约与旧 Pool/ResourcesManager 分开。UnityResourceLoader 跨 store 计数；直接 Resources.Load 的外部持有者不在所有权记录内，应统一加载入口。Resources 无法原生取消请求或逐个卸载 Prefab：最后租约归还后非 Prefab 资产实际 Unload，Prefab 等待宿主选择 UnloadUnusedAssets。Godot 后端同步加载，使用独立缓存模式并释放原生包装。

## 版本化存档与生成契约

`VersionedSaveService<T>(root, serializer, currentVersion, migrations)` 使用 `slot-N.save` 单文件封装数据、元信息和 SHA-256，原子替换并保留上一版 `.bak`。损坏/缺失主档可恢复备份；未来版本、缺迁移或迁移失败明确抛错。迁移逐版本执行并提交升级数据。

新服务不会自动读取旧 `SaveSlot_N` 与 `_Meta` 分离格式。项目需显式用旧提供者读取 DTO、转换并写入新服务，验证完成后决定旧文件处理。旧 SaveGameManager 保留基础槽位 API，未新增自动存档调度。

配置定义/数据见 `samples/Shared/Configuration`，协议及兼容性基线见 `samples/Protocol`。修改源后运行生成脚本，禁止手改 Generated；删除/重命名配置需显式移除旧产物和 meta。Networking 是可选包，HTTP/业务状态、超时/调用取消分别处理；不隐式重试，切换会话后旧响应不交付。已有协议可提供自有 codec。

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

## 模块启动与失败恢复

在 Bootstrap 前完成模块注册。运行中新增模块、生命周期转换期间重入启动或初始化途中关闭，现在会明确抛出 InvalidOperationException。已运行时重复 Bootstrap、已关闭时重复 Shutdown 保持幂等。

Initialize / PostInitialize 失败会逆序关闭已经开始初始化的模块，包含初始化到一半抛错的模块。自定义 IModule.Shutdown 必须能清理部分初始化状态。回滚后保留注册，可修正依赖后重试；回滚自身也失败时以 AggregateException 同时报告启动和清理异常。正常 Shutdown 会继续关闭其他模块，再清空注册并集中报告清理异常；尚未启动的模块只移除注册。

EventCenter.Shutdown 现在清除监听器；重启后需要重新订阅。ServiceLocator 的循环工厂依赖及返回 null 的工厂会明确失败，失败工厂保留以便修正依赖后重试。

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

Unity 自动安装 PlayerPrefs 与 SQLite 驱动工厂；数据库存储仅在 enableDatabase 为 true 时注册。DataPersistenceAPI 的默认提供者只有 JSON/二进制。初始化后不能替换提供者；停止业务继续发起请求，再 await ShutdownAsync，然后重新注册和初始化。

ShutdownAsync 开始后 IsInitialized 立即为 false，新请求被拒绝；通过 DataPersistenceAPI 已经开始的操作完成后才释放提供者。取出提供者直接调用的操作仍由宿主自行等待。初始化失败会释放本次所有提供者并清空注册，重试前重新注册自定义提供者；同一提供者实例注册在多个存储类型下只初始化、释放一次。

SQLiteDatabaseProvider 现仅负责绑定 Unity 工程内的 Mono.Data.Sqlite 连接；数据库操作移至 Data 的 AdoDatabaseProvider，通过连接工厂接入宿主驱动，并保留原 SQLite 查询约定。

数据库对象映射改为按列名匹配公开可写属性/字段，忽略大小写；需要用 SQL 列别名匹配 DTO 成员，不再依赖属性反射顺序。类型转换失败返回 Failed。提交、回滚或 Dispose 后事务已结束，再次提交或执行返回 NotInitialized；重新初始化会关闭旧连接。单个连接仍需顺序使用。

SaveSettingsAsync/LoadSettingsAsync 现在使用配置的默认存储。旧版强制使用 PlayerPrefs。需要继续读取旧 Unity 用户设置时显式调用 SaveAsync/LoadAsync，并传 StorageType.PlayerPrefs；之后可按业务规则迁移到新存储。SaveGame 元数据也从默认存储恢复；旧版只从 PlayerPrefs 推测槽位，未保存可靠的恢复索引，必要时由项目迁移已有槽位。

普通字段 JSON 可读取，新增格式支持可写属性和基础类型；Unity 私有 SerializeField 字段需要公开 DTO 或显式 Newtonsoft 注解。MessagePack/Protobuf 需要注册真实编码器，不再假装实现或静默回退。具体文件格式兼容说明以 Data README 为准。

压缩序列化器写入 v2，继续读取 v1 的布尔值、原始字符串和区域数字；JSON 编码器也能读取旧工厂写出的 v1 二进制/Base64 内容。BinaryFileStorage 的独立文件封装保持 v1。文件存储键现在是单个文件名；旧版带目录分隔符的键应迁移为独立存储目录与扁平键名。

文件覆盖写入改为同目录临时文件完成后替换，保留现有格式；替换失败时保留原文件并返回 Failed。宿主文件系统需要支持 File.Replace。泛型读取空白/非法 JSON、非法编码、截断封装或损坏 GZip 时返回 Corrupted，调用方应据此提示恢复或选择其他存档。原始字节读取不增加内容校验。保存与读取的并发协调只覆盖同一进程内的同路径操作，删除/清空的并发顺序由业务管理。

## 验证

运行 tools/verify.ps1，检查通用依赖、asmdef 图、UPM 路径、编译、自动测试和无图形示例。Unity 编译需要编辑器，可运行：

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath '你的 Unity.exe 路径' -RunSmoke -BuildPlayer
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export
```

Unity 脚本使用 artifacts 中的隔离工程副本和指定编辑器，不改写示例工程记录的版本；编译、PlayMode 和 Player 日志保留在 artifacts/UnityValidation。编辑器许可证与包服务由本机 Unity 提供。Godot 导出需要匹配版本的 Windows 模板，可通过 -TemplatePath 指定。实际版本、验收记录与支持边界见 [验证说明](validation.md)。
