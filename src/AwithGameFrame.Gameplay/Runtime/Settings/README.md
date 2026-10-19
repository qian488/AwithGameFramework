# 用户设置

`UserSettingsService` 将语言、主音量/静音、各音频 bus 音量/静音和输入重绑定放进同一份用户偏好。运行时只依赖 Core/Data 与 Gameplay 服务；Unity/Godot 原生菜单通过同一实例读写。服务不使用静态 `DataPersistenceAPI`。

```csharp
// 先创建 InputContext 并配置 action，再构造 settings，以便捕获默认绑定。
var saves = new VersionedSaveService<UserSettingsData>(
    settingsDirectory, new JsonSerializer(), currentVersion: 1);
var settings = new UserSettingsService(host.Context,
    new VersionedUserSettingsStore(saves),
    new UserSettingsData { Language = "en", MasterVolume = 0.8f },
    localization, audio, input);
host.AddModule(new UserSettingsModule(settings));

// host.StartAsync 完成后：
settings.SetLanguage("zh-CN");
settings.SetBusVolume("music", 0.4f);
settings.Rebind("game", "jump", "key/j");
await settings.SaveAsync();
```

构造参数和 `Snapshot` 都深拷贝。空 `BusVolumes` 默认配置 `sfx`、`music`，缺少的 mute 值补 false；空 `InputBindings` 捕获构造时已有全部 action。可显式提供部分 action，后续动态创建且未纳入设置的 action 会保留，但不能与它们的控制键冲突。同一 context 内每个 action/control 唯一。需要交换两个控制键时，修改一份 `Snapshot` 后调用 `Update`，以便整份验证和提交。添加新默认 bus/action 后，旧存档缺项会合并默认值并保持 dirty；移除或重命名旧项应提供显式 schema 迁移。

`UserSettingsModule` 默认依赖 `localization`，绑定音频/输入服务时还依赖 `audio`、`input`。因此启动先准备目录和运行时，再恢复设置，关闭先保存再释放依赖。自定义模块 ID 时，传入完整依赖列表。可不使用模块而显式调用 `InitializeAsync` / `StopAsync`；异步加载完成后通过 Dispatcher 在宿主线程应用，启动/关闭期间宿主必须继续泵送。加载失败、损坏和未来 schema 不会静默变成默认设置。

音量必须为 `[0,1]` 有限数；语言须被目录支持；bus 和可配置 action 须存在；重绑定须无冲突，scale 为 `[-1,1]` 非零有限数，dead zone 为 `[0,1)`。完整验证通过才提交偏好，然后更新音频、输入、本地化并发布宿主 `UserSettingsChanged`。仅改音量不会释放已按下的输入。运行时或监听器抛错时，已提交的偏好仍为 dirty，可修复原因后 `Reapply`；监听器内不能重入设置修改。常规修改必须在宿主线程，快照/保存可跨线程调用。

`SaveAsync` 串行持久化，每次只确认实际写入的 revision；较早的写入不会清除保存期间的新修改。排队的保存取得锁之后才捕获最新值。失败会保留 dirty 和 `LastSaveError`，成功后清除错误。`StopAsync` 拒绝新修改，等待前次保存，再保存最新版本；错误会传播且允许再次调用 Stop 重试。应用可在确认操作、失焦或显式策略下保存，框架没有隐藏的每帧写盘。

`VersionedUserSettingsStore` 使用 `VersionedSaveService<UserSettingsData>` 提供的校验和、原子替换、备份恢复和逐版迁移；目录和 schema 版本由应用明确选择。升级旧 DTO 的示例：

```csharp
var migration = new SaveMigration(1, 2, async (bytes, cancellationToken) => {
    cancellationToken.ThrowIfCancellationRequested();
    var old = await serializer.DeserializeAsync<SettingsV1>(bytes);
    return await serializer.SerializeAsync(new UserSettingsData {
        Language = old.Locale, MasterVolume = old.Volume
    });
});
var saves = new VersionedSaveService<UserSettingsData>(
    settingsDirectory, serializer, 2, new[] { migration });
```

迁移须逐版连续，返回目标版本 DTO 的序列化字节；服务负责升级文件后再返回。设置保存是用户偏好闭环，账号云同步、冲突规则和鉴权由产品集成层实现。
