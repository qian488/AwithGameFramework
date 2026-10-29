# Gameplay

跨引擎的视图/场景、导航、输入/音频、本地化、设置与版本化存档，依赖 Core 和 Data；公共 API 使用 `Task`。引擎对象和设备后端通过 [adapters](../../adapters/README.md) 接入，服务按宿主实例组合。

| 要做的事 | 实例入口 | 说明 |
| --- | --- | --- |
| 加载语言目录、回退和绑定文本 | `LocalizationService` / `LocalizationModule` | [本地化](Runtime/Localization/README.md) |
| 恢复与保存语言、音量和输入偏好 | `UserSettingsService` / `UserSettingsModule` | [用户设置](Runtime/Settings/README.md) |
| 管理窗口、模态、焦点和属性绑定 | `ViewService` / `PresentationModule` | 下文视图与绑定 |
| 加载和卸载本服务拥有的场景 | `SceneService` / `SceneModule` | 下文场景所有权 |
| 组合页面流程与返回历史 | `NavigationStack` | 下文导航 |
| 读取输入、维护动作和上下文 | `InputService` / `InputModule` | [输入](Runtime/Input/README.md) |
| 管理播放、音量、并发和资源 | `AudioService` / `AudioModule` | [音频](Runtime/Audio/README.md) |
| 原子保存进度、恢复备份和逐版迁移 | `VersionedSaveService<T>` | 下文版本化存档 |

存档显式传入目录、`ISerializer` 和数据结构版本，不依赖全局 `DataPersistenceAPI`。设置与视图布局采用纯规则计算、服务提交和执行效果的分工，见 [函数式核心指南](../../docs/guides/functional-core.md)。共享组合示例见 [Shared](../../samples/Shared/README.md)，验证入口见 [参考](../../docs/reference/verification.md)。

## 视图与布局

`Presentation.ViewService` 用 `IViewFactory` 创建视图并拥有每个视图的 `ResourceScope`；按 layer/创建顺序排序、选择焦点，最上层 modal 阻断下层输入。重复 key 复用 handle，选项变化需先关闭再打开。`ViewFlow` 可接下方导航栈，Exit 关闭视图、再次 Enter 重新创建；导航使用的 key 应由该流程独占，避免手动打开与导航共享同一 handle。`PresentationModule` 默认依赖 `resources` 模块，宿主停止时先关闭视图再释放资源。不要在视图生命周期回调中重入当前服务，可通过宿主 Dispatcher 排队下一次操作。

`ViewLayoutRules.Calculate(items)` 是不调用原生视图的纯计算入口：输入 `ViewLayoutItem(instanceId, request)`，返回按 layer / instanceId 排列的只读 `ViewLayoutEntry` 集合及各自 `ViewState`，不修改输入。最上层 modal 以下的视图被遮挡并禁用输入，焦点只从该 modal 及其上方选择；没有可聚焦视图时不产生焦点。服务只负责捕获事实、应用结果与管理生命周期。

应用通过 `ViewHandle` 读取 `InstanceId`、`Request`、`IsOpen`、`IsFocused`，通过 `CloseAsync()`（或 `DisposeAsync()`）请求服务关闭；`SceneHandle` 提供只读标识、`IsLoaded` 与 `UnloadAsync()`（或 `DisposeAsync()`）。句柄不公开原生实例或资源 scope。工厂和场景后端收到的 `ResourceScope` 是借用资源入口，只能由服务释放；引擎控件初始化和绑定在工厂创建的视图内部完成，先销毁原生实例再归还资源。

## 输入与视图路由

`InputService(host.Events, new ViewInputRouter(views))` 在发布玩法事件前路由视图，`ViewInput.Payload` 保留完整 `InputActionEvent`。视图处理动作或顶层 modal 都会消费输入；已经交付玩法的持键会收到一次 `Released`，关闭 modal 后继续持键保持抑制，物理释放后再次按下才恢复。`InputUpdateResult.Events` 只含实际交付玩法的事件，`Errors` 收集路由和监听异常；路由异常按已消费处理，并释放此前玩法状态。输入与路由必须在创建线程运行；路由或监听回调不能重入输入修改，视图输入回调不能直接打开/关闭视图，应通过 Dispatcher 排队。

`CaptureBindings()` 返回只读的完整绑定快照；`ApplyBindings(snapshot)` 在全部验证通过后一次替换，支持同 context 内交换控件，不改变 context 的优先级、modal 或启用状态。快照必须精确覆盖所有已有 action；未知、重复、缺失、冲突与非法参数均在提交前拒绝。成功提交释放旧按下状态；释放监听异常在提交后报告，绑定已更新。

## 音频与资源清理

`AudioModule(audio)` 默认依赖 `resources`，保证宿主停止时先销毁原生 voice 再归还资源。资源由宿主外部管理时显式使用 `new AudioModule(audio, Array.Empty<string>())`；自定义资源模块可通过 params 指定依赖。`pauseWithHost: false` 的声音在宿主暂停时继续播放，并通过暂停维护检测自然结束、释放 voice/资源与并发容量；手动暂停或跟随宿主暂停的声音保留。暂停维护只清理，不推进模拟与游戏时钟。`HasBus(name)` 可在应用设置前确认音频总线存在。

原生 voice 同时实现 `IAsyncDisposable` 时，音频清理先 Stop、等待 `DisposeAsync` 完成，再归还 ResourceScope 与并发槽；否则使用同步 Dispose。Stop/释放异常均收集，其他清理继续执行。异步原生释放没有强制超时或取消，退出期间必须继续运行引擎帧；后端不完成时宿主关闭也会等待，不能提前归还仍被原生使用的资源。

## 显式属性绑定

`ViewBindingScope` 把 `INotifyPropertyChanged` 连接到显式 getter/setter，不使用反射。绑定立即同步，之后仅响应指定属性名或 null/空的全部属性通知；scope 或单个绑定 Dispose 时解绑，构造时传入的 token 取消也会解绑。创建、模型通知和显式 Dispose 必须在创建 scope 的宿主线程上；后台数据先通过 Dispatcher 更新模型。token 可从后台取消，只执行事件解绑，因此模型的事件 remove 必须支持取消线程。getter/setter 异常直接传给 Bind 调用方或 PropertyChanged 发布者；初次同步失败会解绑，后续失败保留绑定以允许修复后更新，token 解绑异常由 Cancel 报告。视图销毁前先 Dispose scope，不依赖 GC 或引擎对象失效检查。

```csharp
var title = new ObservableValue<string>("Menu");
using var bindings = new ViewBindingScope(viewLifetimeToken);
bindings.Bind(title, nameof(title.Value), () => title.Value, text => nativeLabel.Text = text);
title.Value = "Playing";
```

`nativeLabel.Text` 由引擎脚本提供：Unity prefab 根可继承 `UnityViewBehaviour`，Godot Control 根可继承 `GodotView`，在 Show 创建绑定、Hide/销毁前释放。通用包不包含引擎控件类型、反射路径、双向绑定或完整 MVVM 系统。

## 场景所有权

`Scenes.SceneService` 通过 `ISceneBackend` 先加载、激活新场景，成功后 Single 卸载本服务拥有的旧场景，Additive 则保留它们。重复 key 复用实例；Active 卸载会激活另一个已拥有场景。服务不销毁 bootstrap 或其他宿主场景。提交前失败/取消会等待原生晚到结果并清理新实例、释放 scope，保留并恢复旧 Active；提交后会完成旧场景清理。原生卸载失败保留 handle/scope 供显式重试，不报告虚假卸载成功。`SceneModule` 默认也依赖 `resources`。

Unity 后端使用独立 uGUI parent、宿主提供的 Canvas/EventSystem、Resources prefab 与原生 additive SceneManager；Godot 后端使用 PackedScene、专用 Control parent，以及持久 Node 下的子场景，不改全局 CurrentScene。两端只能在引擎主线程调用且需持续运行帧循环，Stop 等待原生释放；不可取消的原生操作必须最终完成。真实示例验收位于两端 `PresentationSmoke`，普通测试验证服务行为不能替代引擎验收。

## 导航与返回历史

`AwithGameFrame.Extensions.Presentation.NavigationStack` 提供小型页面/流程返回历史，内部拥有独立的 Core `FlowMachine`，不假设引擎控件、绑定系统或 view 类型：

```csharp
var navigation = new NavigationStack(host.Context);
await navigation.NavigateAsync(new MenuFlow(), cancellationToken);
await navigation.NavigateAsync(new PlayingFlow(), cancellationToken);
await navigation.BackAsync(cancellationToken); // Playing.Exit → Menu.Enter
navigation.Tick(deltaSeconds);
await navigation.StopAsync();
```

`NavigateAsync` 成功后把此前提交的 flow 压入历史；`ReplaceAsync` 替换当前 flow 并保留返回历史；`BackAsync` 成功才移除最近目的地，无历史时返回 false。`History` 是按旧到新排列的只读快照，`Current` 反映实际进入完成的 flow，过渡中可为 null，`CanGoBack` 和 `IsStopped` 只读。对同一个活跃实例重复 Navigate/Replace 不会重复进入或新增历史。历史保存 flow 实例；返回和失败恢复会再次 Enter，因此 flow 必须支持重新进入并自行拥有、释放其资源 scope。

导航遵循最新请求优先：新请求取消尚未完成的旧请求，等待其部分 Enter 清理结束后再运行。已排队的请求取消后仍等待此前清理完成，由最新请求恢复页面；发起前已经取消的请求不会打断当前导航。历史仅在成功后提交；进入失败或调用方取消时尝试重新进入最后提交的 flow，保持历史不变。若已被新请求或 Stop 取代则不恢复，后续请求基于最后提交的历史继续。恢复本身可被下一次请求取消；真实恢复失败与原导航错误一起聚合，`Current` 保持实际状态，不假装旧页面仍活跃。Flow 的 Exit 必须能清理部分进入状态；已失败的清理不会由导航栈代替修复。

`StopAsync` 取消导航和恢复、等待 flow 清理、清空历史；重复调用返回同一任务，失败也不会重新打开导航栈。宿主退出时必须等待 Stop。导航不安装同步上下文，宿主应从自己的线程发起并维持 await 后的线程；flow 回调不得重入导航，应通过宿主调度器请求后续操作。没有退出合作取消的 flow 会阻塞后续导航，库不会强行中断其代码。

## 版本化存档

```csharp
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;

var codec = new JsonSerializer();
var saves = new VersionedSaveService<PlayerProgress>(saveDirectory, codec, currentVersion: 1);
await saves.SaveAsync(0, new PlayerProgress { Level = 7 }, "boss entrance", cancellationToken);
var loaded = await saves.LoadAsync(0, cancellationToken);
if (loaded.Status == SaveLoadStatus.NotFound) { /* 明确创建新游戏 */ }
else { player.Level = loaded.Data.Level; }
```

宿主提供 `saveDirectory`，可以为每个玩家或测试创建独立实例。槽位从 0 开始，默认上限 20；`ListSlotsAsync` 验证、恢复和迁移所有占用槽位，再返回元信息；`DeleteAsync` 删除主档和备份。列表不是多槽事务快照。数据 DTO 只使用普通 C# 字段/属性；序列化器由调用方显式选择，重新打开时使用相同编码，服务不会推断或切换编码。

每槽是一个 `slot-N.save`：`AWGS` 签名、封装版本、结构版本、槽号、UTC 保存 ticks、描述、数据长度与序列化字节，最后附上整个前文的 SHA-256。数据和元信息一起写入同目录临时文件，刷新后以 `File.Replace` 提交；第一次保存使用移动。覆盖提交保留 `.save.bak` 上一版，替换失败不先删除主档。不支持原子替换的文件系统会抛出 IO 异常，不降级成删除后重写。

主档缺失、校验失败或数据无法反序列化时，尝试校验并恢复备份，返回 `RecoveredFromBackup`，同时原子修复主档且保留健康备份。两份都不存在才返回 `NotFound`；占用槽位无法恢复时抛出 `SaveCorruptedException`，不能静默当作新档。结构版本较新、缺失迁移或迁移失败抛出 `SaveVersionException`，不会用旧备份掩盖兼容问题。SHA-256 用于发现损坏，不提供加密或真实性认证。

结构迁移必须连续，每步 `SaveMigration(from, from + 1, migrate)` 接收旧序列化字节并产生下一版字节。例如：

```csharp
var migrations = new[]
{
    new SaveMigration(1, 2, async (bytes, token) =>
    {
        token.ThrowIfCancellationRequested();
        var old = await codec.DeserializeAsync<ProgressV1>(bytes);
        return await codec.SerializeAsync(new PlayerProgress { Level = old.Experience / 10 });
    })
};
var savesV2 = new VersionedSaveService<PlayerProgress>(saveDirectory, codec, 2, migrations);
var upgraded = await savesV2.LoadAsync(0, cancellationToken);
```

迁移完成并反序列化成功后才提交新档，保留原始保存时间与描述；`WasMigrated` 表示本次执行了迁移，重开后无需重复迁移。迁移委托不应产生外部副作用；升级链或最终反序列化失败时保留原文件。旧 `SaveGameManager` 的分离文件格式不会被自动识别，需显式读取旧 DTO，再由新服务保存。

同进程的服务实例按规范路径协调同槽的保存、读取、迁移和删除，等待锁可取消；不同槽位可能并发使用同一个序列化器，因此编码实现应支持并发或由宿主隔离。`ISerializer` 没有取消参数，服务在其前后检查取消，迁移委托直接接收令牌。临时文件提交前取消会清理临时文件并保留主档；原子替换完成后返回成功。跨进程写入、符号链接别名、浏览器虚拟文件系统及断电后磁盘持久性需要宿主另外验证。异常退出遗留的 `.awith-slot-*.tmp` 不会作为存档读取。

## 兼容入口

- `LocalizationManager.LoadDataset(LocalizationDataset)` 接收普通 C# 数据；文件或资源加载通过 `ILocalizationSource` 接入。Unity 的 ScriptableObject 和 Resources 实现在 Unity 适配包。
- `SaveGameManager` 是保留旧 API 的兼容入口，通过 DataPersistenceAPI 分别保存游戏数据与元信息，未提供自动存档、版本迁移或原子提交。先初始化 Data，再初始化管理器；等待 `Initialization` 后读取槽位列表。可调用 `ReloadSlotsAsync()` 刷新。
- 原 `AwithGameFrame.Extensions.*` 命名空间保留，程序集改为 `AwithGameFrame.Gameplay`。

新应用使用上文的实例服务。旧管理器不与它们同步状态或事件，迁移时显式处理格式和生命周期，见 [迁移说明](../../docs/migrations/README.md)。
