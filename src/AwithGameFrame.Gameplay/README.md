# Gameplay

跨引擎的本地化与存档槽位管理，依赖 Core 和 Data；公共 API 使用 `Task`。

- `LocalizationManager.LoadDataset(LocalizationDataset)` 接收普通 C# 数据；文件或资源加载通过 `ILocalizationSource` 接入。Unity 的 ScriptableObject 和 Resources 实现在 Unity 适配包。
- `SaveGameManager` 通过 DataPersistenceAPI 保存游戏数据与槽位元数据。先初始化 Data，再初始化管理器；等待 `Initialization` 完成后读取槽位列表。可调用 `ReloadSlotsAsync()` 刷新。
- 原 `AwithGameFrame.Extensions.*` 命名空间保留，程序集改为 `AwithGameFrame.Gameplay`。
