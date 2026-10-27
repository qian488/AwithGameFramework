# Unity SQLite 接入

可选包 `com.awithgameframe.unity.sqlite`。先安装真实 Mono.Data.Sqlite 插件和对应目标平台的 SQLite 原生库，再安装本包。UPM 不自动提供这些插件。

保留 `AwithGameFrame.Foundation.DataPersistence.SQLiteDatabaseProvider` 名称与脚本 GUID，程序集为 `AwithGameFrame.Unity.SQLite`。运行时在 AfterAssembliesLoaded 将连接工厂注册到通用 DatabaseFactory；不依赖 Unity 兼容包。需要旧 `UnityDataPersistence.InitializeAsync(enableDatabase: true)` 时另行安装兼容包，也可在应用中显式使用数据库提供者。

移除本包不影响现代 Unity 后端或文件/PlayerPrefs 存储。数据库启用仍需显式配置，失败不会悄悄改用其他存储。
