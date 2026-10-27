# 从旧 Unity 工程迁移

## 2026-10-27 框架收敛

现代 Unity 接入继续使用 `com.awithgameframe.unity`，该包不再导入旧静态服务或要求 UniTask、DOTween、Mono.Data.Sqlite。旧项目另外安装本地包：

| 包与目录 | 用途 / 前置插件 |
| --- | --- |
| `com.awithgameframe.unity.compatibility` / `adapters/unity-compat` | 旧 Engine/Foundation/调试和本地化入口；需要 UniTask |
| `com.awithgameframe.unity.dotween` / `adapters/unity-dotween` | 默认动画提供者；需要兼容包和真实 DOTween |
| `com.awithgameframe.unity.sqlite` / `adapters/unity-sqlite` | 数据库连接工厂；需要真实 Mono.Data.Sqlite 与目标平台原生库 |

样例 manifest 已安装上述完整组合。旧 Mono、Scene、Inspector 配置和多数 Unity 类型接口的自定义 asmdef 引用改为 `AwithGameFrame.Unity.Legacy`；DebugConsole 与 UnityLocalizationBootstrap 使用 `AwithGameFrame.Unity.Compatibility`。Foundation、现代 Unity/Extensions 的程序集名保持不变；DOTweenProvider、SQLiteDatabaseProvider 分别归属 `AwithGameFrame.Unity.DOTween` / `AwithGameFrame.Unity.SQLite`。源码 namespace 和 .meta/GUID 保留，程序集限定类型名、反射或 linker 配置需同步更新。

`UnityPlatform` 仍安装时间/日志/目录；安装兼容包才启动旧静态帧驱动和本地化单例。现代 GameHostBehaviour 自己驱动会话。缺少动画/数据库可选接入时，使用相应能力会明确失败；自定义动画仍可设置 `ProviderManager.AnimationProvider`，数据库仍可显式注册 `DatabaseFactory`。默认提供者通过可选包注册，无反向插件依赖。

Core 的旧 Base/Bootstrap/DI/Event 物理路径移至 `Runtime/Compatibility`，namespace 与程序集不变。`BaseManager.GetInstance/ResetInstance` 使用同一同步锁，修复半初始化发布及获取返回 null；Reset 仍只适用于停用旧使用者后的测试/替换阶段，不能延长已借出引用的生命周期。新增实例模块由静态检查约束使用 GameContext，不混用旧全局表和事件。

`ViewHandle.View`、`ViewHandle.Resources`、`SceneHandle.Resources` 改为内部属性。应用通过现有 `CloseAsync/UnloadAsync/DisposeAsync` 触发服务负责的有序清理；原生初始化/绑定在 factory/backend 的创建回调内完成，不自行释放服务借出的 scope。布局规则可通过 `ViewLayoutRules.Calculate` 独立计算，服务负责提交和调用原生视图。

`DataPersistenceAPI.InitializeAsync(config)` 现在捕获独立会话配置，包含默认路由、序列化选项、StorageConfigs 字典和内置 StorageConfig。初始化后修改外部 config 不再热切换提供者；需 Shutdown 后重新初始化。自定义 opaque 选项对象的内部快照由第三方提供者在初始化时捕获。

`HttpClientTransport` 原构造签名保留，响应正文默认最多 16 MiB，超限抛出 HttpRequestException（ProtocolClient 报 NetworkError）。可传 `maxResponseBodyBytes` 配置；零只允许空正文。大文件继续使用流式内容下载，不通过协议响应缓冲。

Godot 宿主关闭由每树统一协调，窗口关闭等待所有宿主；最后一个宿主停止后恢复原 AutoAcceptQuit。启动中暂停状态会在启动后回放，重复恢复不再对 Running 宿主调用 Resume。删除节点前仍应显式等待 ShutdownAsync；退出树回调本身无法阻塞等待异步清理。

## Agent 开发工具与模板

新增 `agent`、`tools/AgentKit` 与 `templates/portable-feature`，用于开发任务范围、上下文、受控工具执行、确认、评测、交接及模板复用，不进入游戏运行时依赖。运行时公共 API 与存档格式不受这层工具影响。标准验证现在也检查 Agent 契约并编译测试功能模板，NUnit TRX 位于 `artifacts/TestResults`。

任务/工具清单 `schemaVersion=1`，每次任务创建独立工作区基线；记录的摘要不能替代原文件备份。使用入口见 [AgentKit](../agent/README.md)。

## 用户设置函数式试点

新增 `Gameplay.Settings.UserSettingsRules`，提供默认配置构造、恢复合并、规范化校验、输入绑定覆盖与结构比较，均以普通数据快照为输入并返回独立结果。现有 `UserSettingsService`、模块和存档 DTO/格式保持兼容，调用方可以继续使用现有 API。纯规则不会修改或应用设置，候选要经服务 `Update` 才提交到运行时，持久化仍显式调用 `SaveAsync`。

`LocalizationService.AvailableLanguages` 现在与 `IsLanguageSupported` 一样，在服务释放后抛出 `ObjectDisposedException`，防止采集失效目录后提交设置。试点分层与约束见 [说明](functional-core-pilot.md)。

## 实例宿主与 Godot

新应用使用 `AwithGameFrame.Core.Hosting.GameHost`，通过 `IGameModule.Id/Dependencies` 声明异步启动依赖；普通服务注册在 `host.Services`，模块通过 `GameContext` 访问。旧 `FrameworkBootstrap/ServiceLocator` 保留兼容，旧静态管理器没有自动获得实例隔离。每个会话创建新宿主，同一宿主停止后不重启。

Unity 可继承 `AwithGameFrame.Unity.Hosting.GameHostBehaviour`；Godot .NET 使用 `GameHostNode`。创建线程调用 `host.Tick(unscaledSeconds)`，必须传入未缩放秒数；模块 `Tick` 现在收到 `Clock.ScaledDeltaTime`。自定义驱动不要预先乘引擎时间缩放，否则会重复缩放；默认 TimeScale=1 保持原值。启动/暂停/停止期间也需持续 Tick，处理原生资源释放和主线程投递。删除宿主对象、切走所属场景或主动退出前先等待 `ShutdownAsync`；引擎销毁回调无法保证等待所有异步操作。

共享示例为 Unity `Assets/SharedDemo.unity`、Godot `samples/Godot/Main.tscn` 和 Headless。导航使用 `NavigationStack`，flow 的 Enter/Exit 管理页面与资源；返回或回滚会重新 Enter。

Godot 的 `GameHostNode` 以 `Time.GetTicksUsec()` 单调差值驱动未缩放时间，避免 `_Process` 的 delta 已乘 `Engine.TimeScale` 后重复缩放；应用恢复时重置基线。需要缩放本框架的游戏时间时设置 `Host.Clock.TimeScale`，全局引擎缩放不会自动改动各宿主的独立时钟。

## 新增通用基础服务

`GameHost.Events` / `Context.Events` 是宿主实例的 `Core.Events.EventBus`，按消息类型订阅，支持 IDisposable/token 和 `EventScope` 自动清理。发布在当前线程同步执行，逐个报告监听错误；需要更新引擎对象时先通过 Dispatcher 回到宿主线程。它与旧字符串 `EventCenter` 独立，不会替旧代码转发事件或接管监听器。

`GameHost.Clock/Timers` 提供缩放与未缩放游戏时间、单次/重复定时器及 Delay。只有 Running 推进；宿主暂停与生命周期过渡冻结两个域，调度器继续工作。`TimeScale=0` 只冻结缩放域，未缩放域也不代表暂停期间的现实时间。启动/暂停/关闭回调不能等待游戏时间 Delay；真实超时使用宿主自己的计时。宿主停止取消所有待执行计时器，回调与 Tick 错误需由调用方观察。

`Core.Pooling.ObjectPool<T>` 是普通引用类型的独立实例池，容量包括借出对象，支持预热、租约及引用所有权检查；重复归还、跨池归还和跨线程操作明确失败。它不会自动替换旧 Unity GameObject 池，迁移时显式提供工厂、重置与销毁回调，并由模块 Dispose；租约归还后停止使用对象。

`Extensions.Presentation.ViewService` 管理 layer、modal、焦点和每视图资源 scope，通过 Unity/Godot 的 `IViewFactory` 创建真实控件。`ViewFlow` 可作为导航目的地，重复 key 复用 handle，导航 key 应由对应流程独占。`ViewBindingScope` 用显式 getter/setter 连接 `INotifyPropertyChanged`，无反射路径；绑定立即同步，按属性名更新，Dispose/token 解绑，getter/setter 异常保持可观察。模型更新与显式 Dispose 在创建线程执行，视图销毁前先解绑；后台取消只解绑，事件 remove 必须支持取消线程。旧 UIManager 不会自动转成这些服务或完整 MVVM。

`Extensions.Scenes.SceneService` 按服务实例管理 Single/Additive 所有权；新场景激活成功后才卸载旧场景，失败/取消清理新实例并恢复旧 Active，不销毁 bootstrap。Unity 后端使用 SceneManager additive；Godot 后端把 PackedScene 放在持久 parent 下，不替换全局 CurrentScene。原生 unload 失败保留 handle 供重试。`PresentationModule`、`SceneModule` 默认依赖 `resources`，需在停止资源服务前清理。

`Gameplay.Input.InputService` 提供动作、优先级/模态上下文、重绑和按下/保持/释放事件，通过 `IInputBackend` 读引擎设备；失焦/暂停释放按住状态。`Gameplay.Audio.AudioService` 提供 master/bus 音量、静音、循环/并发与资源租约，通过 `IAudioBackend` 创建原生 voice，分别保存用户暂停和宿主暂停。两者有宿主模块接入与 Unity/Godot 原生后端，须在创建线程使用并等待异步关闭；旧静态输入/音频 API 的状态不自动迁移或隔离。

将 `new ViewInputRouter(views)` 传给 `InputService(events, router)` 或 `SetRouter`，由 UI 优先消费 `InputActionEvent`。消费中的持键不再交付玩法，已交付动作补发 Released，持续抑制到物理释放；关闭弹窗不会凭空触发 Pressed。视图输入回调不可同步开关窗口/停止服务，使用 Dispatcher 延后操作。输入绑定可通过 `CaptureBindings` / `ApplyBindings` 原子读取和恢复。

`AudioModule(audio)` 现在默认依赖 `resources`，确保 voice、资源 scope 先于资源服务关闭。若外部管理音频资源，显式使用 `new AudioModule(audio, Array.Empty<string>())`；自定义模块 ID 则传完整依赖。暂停 Tick 调用可选 `IGameMaintenanceModule.Maintain`，音频在此回收不随宿主暂停的自然结束播放；普通游戏 Tick、Clock 与 Timers 继续冻结。

新增 `Extensions.Localization.LocalizationService` 是宿主实例服务，旧 `LocalizationManager` 保留兼容且不与之同步。通过 `LocalizationModule` 加载普通数据集，`BindText` 返回可解绑订阅，视图关闭时 Dispose。`Gameplay.Settings.UserSettingsService` 将语言、主/分组音量和输入重绑定统一保存；`UserSettingsModule` 在依赖准备后恢复，在依赖释放前保存。`VersionedUserSettingsStore` 使用版本化存档的迁移/备份机制；旧 PlayerPrefs 或自定义设置格式需要项目显式导入。具体 API 与默认值合并规则见 [设置说明](../src/AwithGameFrame.Gameplay/Runtime/Settings/README.md)。

## Unity 可靠性修正

旧 `FrameworkEntry` 暂停时保留模块，暂停/恢复输入和音频。模块开关在启动时消费，修改需重启会话；运行参数由帧驱动每 0.25 秒检查。

池的异步创建直接登记借出，重复归还忽略；预热只实例化，不改变 Prefab。Clear 销毁池拥有的空闲及借出对象，旧引用随后失效。UI 同名在途打开共用加载；Hide、失败或过期请求回调 null，迟到对象回收。音效并发上限包含在途加载，超额请求回调 null；音频与输入 Shutdown 解除订阅。

新增资源租约与旧 Pool/ResourcesManager 分开。UnityResourceLoader 跨 store 计数；直接 Resources.Load 的外部持有者不在所有权记录内，应统一加载入口。Resources 无法原生取消请求或逐个卸载 Prefab：后端释放所有权后非 Prefab 资产实际 Unload，Prefab 等待宿主选择 UnloadUnusedAssets。Godot 可选择同步 `GodotResourceLoader` 或后台 `GodotThreadedResourceLoader`，通过宿主 Dispatcher/同步上下文管理独立原生所有权；不可中断的请求仍需等待晚到成功并释放。

## 资源缓存与原始文件内容更新

`ResourceStore` 默认不保留 idle 项。启用 `ResourceCacheOptions(maxBytes, maxEntries)` 后，最后租约归还的资源可进入有限 LRU；只保留大小估算为正且符合预算的项，活跃租约不会因预算被驱逐。后端 `LoadedResource` 可提供 `estimatedSizeBytes`；默认零表示未知，不缓存。`PreloadAsync<T>` 要求已启用缓存，`TrimCacheAsync` 可主动释放空闲项，宿主关闭释放全部。旧代码若依赖“最后租约关闭立即 native unload”，迁移时保持默认缓存配置或显式 trim；预算不代表引擎总内存硬上限。

`tools/publish-content.ps1` 从文件目录生成大小/SHA-256 清单和版本文件，输出使用新目录。`Networking.Content.ContentUpdateService` 负责增量复用、断点续传、校验和原子激活，`ContentSnapshot` 固定一个版本的 key/path，`Core.Resources.FileResourceLoader` 可据此加载 byte[]。创建新版本 store/scope 时使用新快照，已有引用继续使用原版本。

新增 `build-unity-content.ps1` / `build-godot-content.ps1` 负责原生 AssetBundle/PCK 构建、依赖和地址目录，输出可直接交给上述发布工具。Unity 使用 `UnityAssetBundleLoader` 与 `UnityBundleSceneBackend`，最后归还租约会 `Unload(true)`，须先销毁实例。Godot 使用 `GodotPackResourceLoader` 校验并挂载不可变 namespace；PCK 不能进程内卸载，更新需新 namespace，旧磁盘包通常在重启后回收。接入命令见 [资源流水线](content-pipeline.md)。Addressables、发布者认证和 CDN 仍需独立接入。

## 版本化存档与生成契约

`VersionedSaveService<T>(root, serializer, currentVersion, migrations)` 使用 `slot-N.save` 单文件封装数据、元信息和 SHA-256，原子替换并保留上一版 `.bak`。损坏/缺失主档可恢复备份；未来版本、缺迁移或迁移失败明确抛错。迁移逐版本执行并提交升级数据。

新服务不会自动读取旧 `SaveSlot_N` 与 `_Meta` 分离格式。项目需显式用旧提供者读取 DTO、转换并写入新服务，验证完成后决定旧文件处理。旧 SaveGameManager 保留基础槽位 API，未新增自动存档调度。

配置定义/数据见 `samples/Shared/Configuration`，协议及兼容性基线见 `samples/Protocol`。修改源后运行生成脚本，禁止手改 Generated；删除/重命名配置需显式移除旧产物和 meta。Networking 是可选包，HTTP/业务状态、超时/调用取消分别处理；不隐式重试，切换会话后旧响应不交付。已有协议可提供自有 codec。

ConfigGen 现在支持 `enums`、命名 `types` 嵌套结构与 `T[]`（可多层），递归校验必填、类型、枚举及外键；每张表选择一个 UTF-8 JSON/CSV 来源。旧标量 schema 的 hash 和格式保持不变，增加结构定义后需同时重新生成 DTO 与 JSON。CSV 数组/结构单元格使用 JSON 字面量，Excel 导出选择 UTF-8 CSV，详见 [ConfigGen](../tools/ConfigGen/README.md)。

`Networking.Realtime.RealtimeClient` 是另行创建的长连接会话，提供可替换 WebSocket transport/codec、心跳、配置次数与退避的重连、旧会话隔离。`IPushRecovery` 明确要求补查最后应用序号之后的完整推送快照，应用成功后才提交游标；恢复失败、缺序或业务处理失败需观察会话任务。项目实现鉴权、恢复 HTTP/其他接口、服务端消息协议、幂等处理与游标持久化，客户端不会自动重发业务消息。此处已提供客户端与可测试恢复契约，未声明已对接实际业务后端或所有平台的 WebSocket 实现。

## 打开工程与找源码

Unity Hub 打开 samples/Unity，原 ProjectSettings、场景、资源和插件都在该目录。仓库根 AwithGameFrame.sln 用于通用库、NUnit 和无图形宿主；Unity 自己生成的解决方案仍位于 Unity 工程目录。

| 旧位置 | 新位置 |
| --- | --- |
| Assets、Packages、ProjectSettings | samples/Unity 下同名目录 |
| Core 的 Base/Bootstrap/DI/Event | src/AwithGameFrame.Core/Runtime/Compatibility |
| Core 的 Logging 和通用接口工具 | src/AwithGameFrame.Core/Runtime |
| Core 的 Mono/Scene、旧 Unity 类型接口和 Inspector 配置 | adapters/unity-compat/Runtime/Engine |
| Foundation 的通用持久化、NewtonsoftJsonProvider | src/AwithGameFrame.Data/Runtime |
| Foundation 的 UI/音频/输入/资源/对象池与旧提供者门面 | adapters/unity-compat/Runtime/Services |
| Extensions 的 SaveGame、Localization 管理逻辑 | src/AwithGameFrame.Gameplay/Runtime |
| ScriptableObject 本地化资源、Unity 数据源 | adapters/unity/Runtime/Features |
| 旧本地化启动器、调试面板 | adapters/unity-compat/Runtime/Features |
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

内置文件提供者现在严格验证并复制配置：仅支持旧 XOR 混淆与 GZip，主动请求 AES256、LZ4、其他压缩级别或未实现的备份/云同步等选项时返回 `NotImplemented`，不再静默使用其他算法。内置编码器不支持的类型元信息/压缩选项同样明确拒绝。默认关闭未实现功能，Release/Production preset 改为未加密 GZip；旧 preset 写出的文件实际是 XOR/GZip，读取旧档必须显式选择 XOR、GZip 和旧密钥，不能直接套用新 preset。读取后可经应用迁移到新策略，详见 [Data 兼容说明](../src/AwithGameFrame.Data/README.md)。版本化存档的备份与 checksum 是独立能力，不受旧提供者的备份配置影响。

文件覆盖写入改为同目录临时文件完成后替换，保留现有格式；替换失败时保留原文件并返回 Failed。宿主文件系统需要支持 File.Replace。泛型读取空白/非法 JSON、非法编码、截断封装或损坏 GZip 时返回 Corrupted，调用方应据此提示恢复或选择其他存档。原始字节读取不增加内容校验。保存与读取的并发协调只覆盖同一进程内的同路径操作，删除/清空的并发顺序由业务管理。

## 验证

运行 tools/verify.ps1，检查通用依赖、asmdef 图、UPM 路径、编译、自动测试和无图形示例。Unity 编译需要编辑器，可运行：

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath '你的 Unity.exe 路径' -RunSmoke -BuildPlayer
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export
```

Unity 脚本使用 artifacts 中的隔离工程副本和指定编辑器，不改写示例工程记录的版本；编译、PlayMode 和 Player 日志保留在 artifacts/UnityValidation。编辑器许可证与包服务由本机 Unity 提供。Godot 导出需要匹配版本的 Windows 模板，可通过 -TemplatePath 指定。实际版本、验收记录与支持边界见 [验证说明](validation.md)。
