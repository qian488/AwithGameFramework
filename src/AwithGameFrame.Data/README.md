# AwithGameFrame.Data

跨引擎持久化库，目标框架为 .NET Standard 2.1。命名空间继续使用 `AwithGameFrame.Foundation.DataPersistence`，异步接口统一为 BCL `Task`。依赖 Core 和 Newtonsoft.Json；不引用 Unity、UniTask、Mono.Data.Sqlite 或示例项目 DLL。

```csharp
var config = new DataPersistenceConfig
{
    DataRootPath = Path.Combine(appDataDirectory, "Data"),
    DefaultStorageType = StorageType.JsonFile
};
var storage = new JsonFileStorage();
await storage.InitializeAsync(config);
await storage.SaveAsync("profile", profile);
var (loaded, result) = await storage.LoadAsync<PlayerProfile>("profile");
storage.Dispose();
```

宿主决定 `appDataDirectory`。库不会访问引擎 API，也不会推断用户目录；相对路径按进程工作目录解析。二进制文件默认位于 `DataRootPath/BinaryData`，可以通过 `BinaryDataRootPath` 指定独立目录。直接实例适合独立测试和多个存储目录；兼容入口 `DataPersistenceAPI.InitializeAsync(config)` 默认注册 JSON 和二进制文件存储。

静态入口的提供者应在初始化前注册。宿主等待业务中的存储操作结束后，可以调用 `await DataPersistenceAPI.ShutdownAsync()` 释放提供者并清除配置，再使用新的目录或提供者重新初始化。`IsInitialized` 可用于宿主生命周期检查；初始化和关闭操作彼此串行。

JSON 保留公开字段，并支持可写属性、字典与基础类型；计算产生的只读属性不会写入存档。已有普通 JsonUtility 对象 JSON 可以继续读取，旧类型的 Unity 私有 `[SerializeField]` 字段需要迁移到公开 DTO 字段或显式 Newtonsoft 注解。文件存储的键必须是单个文件名，不能包含路径分隔符，也不能等于 `.` 或 `..`；旧代码通过目录形式组织键时，应改为不同的存储目录或扁平键名。

`SerializationFormat.Json` 新写入 UTF-8 JSON，并兼容读取旧版 JSON 工厂实际产生的 version 1 二进制字节与 Base64 字符串。`SerializationFormat.Binary` / `CompressedJsonSerializer` 新写入 version 2，信封布局保持 `[版本 int32][压缩标志 byte][长度 int32][UTF-8 JSON 或 GZip 数据]`；读取同时支持 version 1 和 2。version 1 按旧规则解析基础类型，保留 `True`/`False`、原始反斜杠字符串和当前区域数字格式；version 2 使用规范 JSON 转义与数字格式。已有 `BinaryFileStorage` 文件继续使用独立的 version 1 `[版本 int32][长度 int32][数据]` 布局，字节数组存储仍保持原始字节。

引擎存储、数据库驱动和可选编码由宿主注册：

```csharp
DatabaseFactory.RegisterProvider(DatabaseType.SQLite, () => new YourSqliteProvider());
DataPersistenceAPI.RegisterStorageProvider(
    StorageType.Database, new DatabaseStorageProvider(DatabaseType.SQLite));
await DataPersistenceAPI.InitializeAsync(config);
```

通用 ADO.NET 命令、查询、映射与事务实现位于 Data 的 `AdoDatabaseProvider`，由构造函数 `(DatabaseType, Func<string, IDbConnection>)` 注入连接工厂，不依赖具体数据库驱动。宿主也可以注册 `() => new AdoDatabaseProvider(DatabaseType.SQLite, connection => yourConnectionFactory(connection))`。既有表结构辅助逻辑保留 SQLite 方言，其他 SQL 方言应实现相应 `IDatabaseProvider`。

Unity 侧的 `SQLiteDatabaseProvider` 只负责把项目安装的 Mono.Data.Sqlite 插件连接绑定到通用 ADO.NET 提供者；驱动 DLL 不属于 Data 包依赖。没有驱动注册时 `DatabaseFactory.CreateProvider` 返回 `null`，对应存储初始化返回 `Failed`。不要将未实现的 MySQL 示例注册到生产环境。

MessagePack 和 Protobuf 不随包安装，也不会静默回退到 JSON。使用真实编码实现，通过 `SerializerFactory.RegisterSerializer(format, config => yourCodec)` 注册；未注册时创建编码器抛出 `NotSupportedException`。保留的 `MessagePackSerializer` / `ProtobufSerializer` 是已注册实现的兼容代理，不能把代理本身注册为编码器。

配置保留了旧版的备份、云存储和算法字段，以降低源码迁移成本；这些字段不代表功能已实现。现有文件存储只实现 GZip 与旧 XOR 混淆，未实现 AES、LZ4、自动备份或云同步。`IncludeTypeInfo` 不会启用 CLR 类型元数据读取。

从 Unity 迁移时由宿主传入 `Application.persistentDataPath`，并设置 `BinaryDataRootPath = Path.Combine(Application.persistentDataPath, "BinaryData")` 以保留旧二进制目录。`SaveSettingsAsync` / `LoadSettingsAsync` 现在使用配置的默认存储；需要 Unity PlayerPrefs 时显式选择 `StorageType.PlayerPrefs`，并注册 Unity 适配器提供的 `PlayerPrefsStorage`。

```powershell
dotnet build src/AwithGameFrame.Data/AwithGameFrame.Data.csproj
```
