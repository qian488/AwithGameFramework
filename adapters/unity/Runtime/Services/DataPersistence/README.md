# Unity 持久化适配

通用持久化代码位于 `src/AwithGameFrame.Data`。此目录保留 Unity PlayerPrefs、Unity 宿主配置，以及 Mono.Data.Sqlite 插件的薄绑定；MonoBehaviour 示例位于 `samples/Unity/Assets/Examples/DataPersistence`。接口均返回 `Task`，可以从 Unity 异步代码直接 `await`。

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

PlayerPrefs 操作必须由 Unity 主线程调用。需要数据库时，由宿主显式启用驱动并注册存储：

```csharp
DatabaseFactory.RegisterProvider(DatabaseType.SQLite, typeof(SQLiteDatabaseProvider));
DataPersistenceAPI.RegisterStorageProvider(
    StorageType.Database, new DatabaseStorageProvider(DatabaseType.SQLite));
```

数据库默认使用配置的 `DataRootPath/DatabaseName`。通过 `config.StorageConfigs[StorageType.Database] = new StorageConfig { databasePath = existingDatabasePath }` 指定已有数据库。`SQLiteDatabaseProvider` 仅在构造函数中把项目安装的 `Mono.Data.Sqlite.dll` 连接注入 Data 的 `AdoDatabaseProvider`；命令、查询、映射与事务逻辑均位于通用 Data 包。驱动 DLL 不属于通用 Data 包依赖。

历史设计与 ORM 文档保留在 Data 包的 `Documentation~/legacy`，MySQL 接口实现示例位于 `Documentation~/Examples`。历史文档可能描述尚未实现的功能，以当前包 README 与代码为准。
