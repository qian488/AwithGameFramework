# 用户设置

`UserSettingsService` 将语言、主音量/静音、各音频 bus 音量/静音和输入重绑定放进同一份用户偏好。运行时只依赖 Core/Data 与 Gameplay 服务；Unity/Godot 原生菜单通过同一实例读写。服务不使用静态 `DataPersistenceAPI`。

设置模块采用函数式规则与命令式服务的分工。`UserSettingsRules` 接收普通数据快照，返回独立候选，不读取服务、不提交状态、不应用设备或写盘。`UserSettingsService` 在宿主线程采集运行时事实，调用规则后提交 revision、执行音频/输入/语言操作并发布事件，负责异步保存和关闭。试点效果与取舍见 [说明](../../../../docs/functional-core-pilot.md)。

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

传入的 `defaults` 和返回的 `Snapshot` 都深拷贝，其他服务依赖仍引用原实例。空 `BusVolumes` 默认配置 `sfx`、`music`，缺少的 mute 值补 false；空 `InputBindings` 捕获构造时已有全部 action。可显式提供部分 action，后续动态创建且未纳入设置的 action 会保留，但不能与它们的控制键冲突。同一 context 内每个 action/control 唯一。需要交换两个控制键时，修改一份 `Snapshot` 后调用 `Update`，以便整份验证和提交。添加新默认 bus/action 后，旧存档缺项会合并默认值并保持 dirty；移除或重命名旧项应提供显式 schema 迁移。

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

## 直接使用纯规则

工具、预览或独立测试可以不创建 `GameHost`：

```csharp
var defaults = UserSettingsRules.CreateDefaults(new UserSettingsData());
var edited = UserSettingsRules.CreateDefaults(
    new UserSettingsData { Language = "FR-ca", MasterVolume = 0.6f });
var candidate = UserSettingsRules.Normalize(edited, defaults, new[] { "en", "fr" });
// candidate.Language == "fr-CA"；edited.Language 仍为 "FR-ca"。
bool changed = !UserSettingsRules.AreEquivalent(defaults, candidate);
```

`CreateDefaults` 补默认值并校验数据形状，暂不要求语言目录存在；`Restore` 将旧存档缺项与已建立的默认配置合并；`Normalize` 严格校验完整编辑候选，不补缺项、不截断非法值。三者均返回深复制的可变 DTO，调用期间输入须保持稳定。`Restore` / `Normalize` 的 defaults 使用 `CreateDefaults` 建立的完整配置；比较 API 只比较已验证的数据，不承担校验。`Restore` 后以 `saved == null || !AreEquivalent(saved, restored)` 判断是否需要保存；内容比较忽略字典和绑定的排列顺序，语言大小写规范化仍是应保存的差异。

可选 `availableAudioBuses` / `runtimeBindings` 为 null 表示没有连接该服务，空集合表示服务存在且没有对应项。语言支持包含父文化目录，例如 `fr` 支持 `fr-CA`，默认回退不会使任意语言合法。`BuildInputBindings` 接收完整运行时快照，只覆盖设置负责的 action，保留动态 action 并拒绝无效结果及冲突；返回的列表和元素均独立，不应用到输入服务。`HaveSameBindings` 用于有效绑定集合的结构比较。
