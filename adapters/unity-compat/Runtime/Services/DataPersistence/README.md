# Unity 持久化适配

通用持久化代码位于 `src/AwithGameFrame.Data`。此兼容目录保留 Unity PlayerPrefs 与 Unity 宿主配置；Mono.Data.Sqlite 薄绑定已移至独立 `adapters/unity-sqlite` 可选包。MonoBehaviour 示例位于 `samples/Unity/Assets/Examples/DataPersistence`。接口均返回 `Task`，可以从 Unity 异步代码直接 `await`。

Unity 宿主需要传入存档目录，并在初始化前注册引擎存储：

```csharp
var config = new DataPersistenceConfig
{
    DataRootPath = Path.Combine(Application.persistentDataPath, "Data"),
    BinaryDataRootPath = Path.Combine(Application.persistentDataPath, "BinaryData")
};
DataPersistenceAPI.RegisterStorageProvider(StorageType.PlayerPrefs, new PlayerPrefsStorage());
await DataPersistenceAPI.InitializeAsync(config);
await DataPersistenceAPI.SaveAsync("settings", settings, StorageType.PlayerPrefs);
```

PlayerPrefs 操作必须由 Unity 主线程调用。提供者会持久化按 `KeyPrefix` 隔离的托管键索引，重开后 `GetAllKeysAsync`、`GetStatisticsAsync` 与 `ClearAsync` 继续作用于这些键；清空只删除本索引记录的数据，不调用 `PlayerPrefs.DeleteAll()`。业务数据仍使用原来的 `KeyPrefix_key`（无前缀时直接用 key），请避免不同前缀/逻辑键组合成相同的物理键。

索引元数据使用保留命名空间 `__AwithGameFrame.PlayerPrefs.Keys.v1__:`；业务数据的完整键若以它开头，会返回失败，不能占用索引。旧版本没有索引，Unity 也没有公开的全键枚举 API：已知旧 key 仍可直接加载，加载或 `ExistsAsync` 确认存在时将其登记到索引；未被读取的历史键和外部直接写入的键无法自动发现。索引损坏时初始化失败，保留原数据供宿主恢复。`Dispose` 释放会话状态并保留索引。

直接调用提供者的泛型方法时，`int`、`float`、`bool` 与 `string` 使用对应 PlayerPrefs 原生值，复杂对象继续使用 JsonUtility。字节方法继续存取原有 Base64；不要混用同一个 key 的字节和泛型格式。旧错误实现已经丢失的基础类型值无法凭格式修复自动恢复。

初始化会复制实际使用的配置字段，修改调用方配置对象不会切换当前会话的前缀或编码。旧泛型文本路径保留 XOR 混淆的格式兼容；启用时密钥不能为空，失败不会改为写入原文。该路径仍受 JsonUtility 的对象格式限制；通用序列化、加密策略应通过 Data 层配置和字节接口使用。

需要数据库时先安装 SQLite 可选包及真实插件；其运行时入口注册连接工厂。宿主也可显式注册并启用存储：

```csharp
DatabaseFactory.RegisterProvider(DatabaseType.SQLite, typeof(SQLiteDatabaseProvider));
DataPersistenceAPI.RegisterStorageProvider(
    StorageType.Database, new DatabaseStorageProvider(DatabaseType.SQLite));
```

数据库默认使用配置的 `DataRootPath/DatabaseName`。通过 `config.StorageConfigs[StorageType.Database] = new StorageConfig { databasePath = existingDatabasePath }` 指定已有数据库。`SQLiteDatabaseProvider` 仅在构造函数中把项目安装的 `Mono.Data.Sqlite.dll` 连接注入 Data 的 `AdoDatabaseProvider`；命令、查询、映射与事务逻辑均位于通用 Data 包。驱动 DLL 不属于通用 Data 包依赖。

历史设计与 ORM 文档保留在 Data 包的 `Documentation~/legacy`，MySQL 接口实现示例位于 `Documentation~/Examples`。历史文档可能描述尚未实现的功能，以当前包 README 与代码为准。
