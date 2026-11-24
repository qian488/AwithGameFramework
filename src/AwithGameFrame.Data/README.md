# AwithGameFrame.Data

`AwithGameFrame.Data.Diagnostics.HostEventJournal(path, hosts)` 是可选的 JSONL 生命周期出口：记录实例 ID、模块、阶段、耗时、错误与回滚；`Attach(host)` 可追加订阅，`Dispose()` 解绑并关闭文件。初始化路径错误直接抛错；运行中的诊断写入错误通过 `LastError` 暴露，不改变宿主执行。调用方负责日志轮转和敏感字段策略。

跨引擎持久化库，目标框架为 .NET Standard 2.1。命名空间继续使用 `AwithGameFrame.Foundation.DataPersistence`，异步接口统一为 BCL `Task`。依赖 Core 和 Newtonsoft.Json；不引用 Unity、UniTask、Mono.Data.Sqlite 或示例项目 DLL。

普通 C# 配置表运行时位于 `AwithGameFrame.Data.Configuration`。`ConfigTable<T>.LoadAsync(bytes, serializer, expectedSchemaHash, row => row.Id, token)` 读取 `{ "schemaHash": "SHA-256结构指纹", "rows": [...] }`，先验证结构指纹，再拒绝空行和重复整数 ID。通过 `table[id]`、`TryGet`、`ContainsId` 和只读 `Rows` 访问数据；加载完成前不会发布半张表。`SchemaHash` 标识结构，`ContentHash` 标识本次加载的原始配置字节，适合写入构建和运行报告。生成工具和具体 DTO 属于宿主/工具，运行时不依赖 Excel、引擎资源或代码生成器。

程序化资产可以使用 `AwithGameFrame.Data.Generation.GeneratedAssetManifest` 记录生成器 ID、生成器版本、输入 hash、可选 seed、输出路径、输出大小和输出 hash。`Fingerprint` 标识整组生成结果，`Matches(...)` 用于判断参数或生成器变化后已有烘焙结果是否过期。生成器、参数资源、Unity/Godot 资产和运行时选择仍归具体项目，清理工具只应删除 manifest 登记的输出。

加载关联表后调用 `items.ValidateReferences(categories, row => row.CategoryId, "CategoryId")`，缺少目标会抛出带源 ID、引用字段和目标 ID 的 `ConfigurationValidationException`。只有显式设置 `allowZero: true` 才将零视为可选引用。默认不限制 ID 正负值。结构哈希是宿主提供的 DTO 契约指纹，不能代替内容校验、签名或验证所有业务规则；DTO 载入后应视为不可变，避免修改 ID 导致索引与行不一致。配置载入检查取消令牌，但现有 `ISerializer` 不支持中途中断。

```csharp
var config = new DataPersistenceConfig
{
    DataRootPath = Path.Combine(appDataDirectory, "Data")
};
var storage = new JsonFileStorage();
await storage.InitializeAsync(config);
await storage.SaveAsync("profile", profile);
var (loaded, result) = await storage.LoadAsync<PlayerProfile>("profile");
storage.Dispose();
```

宿主决定 `appDataDirectory`。库不会访问引擎 API，也不会推断用户目录；相对路径按进程工作目录解析。二进制文件默认位于 `DataRootPath/BinaryData`，可以通过 `BinaryDataRootPath` 指定独立目录。每个实例由调用方初始化并释放。

宿主负责停止业务写入、等待在途操作，再释放存储实例。

`DatabaseStorageProvider` 实现该接口；直接使用它时通过 `await using` 或 `await storage.DisposeAsync()` 确认驱动关闭完成。同步 `Dispose()` 仅发起关闭并记录错误，不阻塞等待异步驱动。数据库建表失败时不会标记初始化成功。

文件存储在初始化时捕获配置。快照复制 `SerializationConfig`、`StorageConfigs` 字典以及其中框架自带的 `StorageConfig` / `SerializationConfig`；第三方选项对象由其提供者在初始化时捕获内部可变内容。会话复制本身不强制内置文件存储的能力限制，各提供者负责验证自己支持的选项。修改配置后需先关闭并重新初始化会话。

JSON 保留公开字段，并支持可写属性、字典与基础类型；计算产生的只读属性不会写入存档。已有普通 JsonUtility 对象 JSON 可以继续读取，旧类型的 Unity 私有 `[SerializeField]` 字段需要迁移到公开 DTO 字段或显式 Newtonsoft 注解。文件存储的键必须是单个文件名，不能包含路径分隔符，也不能等于 `.` 或 `..`；旧代码通过目录形式组织键时，应改为不同的存储目录或扁平键名。

JSON 和二进制文件覆盖写入采用同目录临时文件，完整写入并刷新后才替换目标文件；写入或替换失败时保留原文件并清理临时文件。已有文件需要文件系统支持 `File.Replace`，不支持时返回 `Failed`。同一进程内的多个提供者实例会协调同路径保存和快照读取；删除、清空与保存的并发顺序仍由宿主管理，跨进程事务和断电持久性不在此保证范围内。进程意外终止可能留下 `.awith-save-*.tmp`，这些文件不会作为存档键枚举。

泛型加载会把无法解析的 JSON、非法编码、截断的二进制封装和损坏的 GZip 数据报告为 `Corrupted`。保留旧 BOM 文本和合法的未压缩数据兼容；原始 `byte[]` 存储不增加格式或内容校验，普通 JSON 也不提供内容校验和。

`SerializationFormat.Json` 新写入 UTF-8 JSON，并兼容读取旧版 JSON 工厂实际产生的 version 1 二进制字节与 Base64 字符串。`SerializationFormat.Binary` / `CompressedJsonSerializer` 新写入 version 2，信封布局保持 `[版本 int32][压缩标志 byte][长度 int32][UTF-8 JSON 或 GZip 数据]`；读取同时支持 version 1 和 2。version 1 按旧规则解析基础类型，保留 `True`/`False`、原始反斜杠字符串和当前区域数字格式；version 2 使用规范 JSON 转义与数字格式。已有 `BinaryFileStorage` 文件继续使用独立的 version 1 `[版本 int32][长度 int32][数据]` 布局，字节数组存储仍保持原始字节。

引擎存储、数据库驱动和可选编码由宿主注册：

```csharp
DatabaseFactory.RegisterProvider(DatabaseType.SQLite, () => new YourSqliteProvider());
await using var database = new DatabaseStorageProvider(DatabaseType.SQLite);
await database.InitializeAsync(config);
```

通用 ADO.NET 命令、查询、映射与事务实现位于 Data 的 `AdoDatabaseProvider`，由构造函数 `(DatabaseType, Func<string, IDbConnection>)` 注入连接工厂，不依赖具体数据库驱动。宿主也可以注册 `() => new AdoDatabaseProvider(DatabaseType.SQLite, connection => yourConnectionFactory(connection))`。既有表结构辅助逻辑保留 SQLite 方言，其他 SQL 方言应实现相应 `IDatabaseProvider`。

查询按列名匹配公开可写属性或字段，忽略名称大小写，支持数值、枚举、可空类型、Guid、时间和字节数组转换；映射失败返回 `Failed`，不交付部分映射的数据。事务提交、回滚或释放后不能继续执行，会返回 `NotInitialized`；释放未完成事务会回滚，提交失败也会尝试回滚并清理。关闭和重新初始化会释放旧连接。一个提供者及其连接应由宿主顺序调用，这些功能不构成完整 ORM。

Unity 侧的 `SQLiteDatabaseProvider` 只负责把项目安装的 Mono.Data.Sqlite 插件连接绑定到通用 ADO.NET 提供者；驱动 DLL 不属于 Data 包依赖。没有驱动注册时 `DatabaseFactory.CreateProvider` 返回 `null`，对应存储初始化返回 `Failed`。不要将未实现的 MySQL 示例注册到生产环境。

需要 MessagePack 或 Protobuf 时，由项目通过 `SerializerFactory.RegisterSerializer(format, config => yourCodec)` 注册实际编码器。未注册的格式抛出 `NotSupportedException`。

内置文件存储的初始化会校验并复制配置。不支持的主动请求返回 `NotImplemented`，保持 `IsInitialized=false`，不会创建目录或写文件。初始化后的外部配置修改不影响已打开的提供者，切换选项需要重新初始化。重新初始化失败会关闭提供者的读写入口。

| 配置 | 内置实际行为 |
| --- | --- |
| `EnableEncryption=true` | 只接受 `EncryptionAlgorithm.XOR` 与非空密钥；用于读取旧文件的混淆，不提供密码学保密性。AES256 明确拒绝。 |
| `EnableCompression=true` | 只接受 `CompressionAlgorithm.GZip`、`CompressionLevel=6`，其中 6 表示运行时的 `Optimal` 策略。LZ4、Deflate 和其他级别明确拒绝。 |
| 加密或压缩开关关闭 | 对应算法、级别和密钥派生参数不参与运行，不会因未启用的算法字段而拒绝初始化。 |
| 自动备份、安全/完整性策略、云同步、性能监控 | 默认关闭；主动请求时明确拒绝。版本化存档服务的备份和 checksum 是独立能力。 |
| `EnableDataValidation` | 必须为 true；键与文件格式检查始终执行。 |
| 泛型文件存储格式 | 只接受 JSON 负载；其他格式由宿主编码后使用原始 `byte[]` API，原始字节仍不经过压缩或混淆。 |

默认配置关闭加密及未实现功能。`GetPreset(Release)` 与 `GetPreset(Production)` 使用未加密的 GZip；`Debug` 显式使用旧 XOR，`Development` 使用普通 JSON。旧 Release/Production 文件实际使用 XOR/GZip，读取时应显式使用下列兼容配置和旧密钥；需要真实加密、云同步或额外编码时接入相应实现，不会自动降级。

```csharp
var legacy = new DataPersistenceConfig
{
    DataRootPath = oldDataDirectory,
    EnableEncryption = true,
    EncryptionAlgorithm = EncryptionAlgorithm.XOR,
    EncryptionKey = oldKey,
    EnableCompression = true,
    CompressionAlgorithm = CompressionAlgorithm.GZip
};
```

内置 JSON/二进制 JSON 编码器的 `SerializationConfig.IncludeTypeInfo` 默认 false，显式 true 会抛出 `NotSupportedException`；普通 JSON 编码器收到压缩请求也会明确拒绝。格式化、空值忽略与自定义日期格式真正应用于文件和编码器；日期格式默认为 null，使用保留小数秒和时区的 ISO 8601 格式。文件格式化由外层 `PrettyPrint` 控制，文件压缩由外层开关控制，内层 `SerializationConfig.EnableCompression=true` 会被拒绝。旧二进制 JSON version 1、旧 XOR/GZip 文件和旧未压缩回退文件仍可读取。

从 Unity 迁移时由宿主传入 `Application.persistentDataPath`，并设置 `BinaryDataRootPath = Path.Combine(Application.persistentDataPath, "BinaryData")` 以保留旧二进制目录。

```powershell
dotnet build src/AwithGameFrame.Data/AwithGameFrame.Data.csproj
```
