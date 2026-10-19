# Gameplay

跨引擎的本地化与存档槽位管理，依赖 Core 和 Data；公共 API 使用 `Task`。

- `LocalizationManager.LoadDataset(LocalizationDataset)` 接收普通 C# 数据；文件或资源加载通过 `ILocalizationSource` 接入。Unity 的 ScriptableObject 和 Resources 实现在 Unity 适配包。
- 新存档使用 `VersionedSaveService<T>`，显式传入存档目录、`ISerializer` 和数据结构版本，不依赖全局 `DataPersistenceAPI`。
- `SaveGameManager` 是保留旧 API 的兼容入口，通过 DataPersistenceAPI 分别保存游戏数据与元信息，未提供自动存档、版本迁移或原子提交。先初始化 Data，再初始化管理器；等待 `Initialization` 后读取槽位列表。可调用 `ReloadSlotsAsync()` 刷新。
- 原 `AwithGameFrame.Extensions.*` 命名空间保留，程序集改为 `AwithGameFrame.Gameplay`。

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
