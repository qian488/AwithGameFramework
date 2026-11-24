# AwithGameFrame.Core

面向 `.NET Standard 2.1` / C# 9 的引擎无关内核。可通过项目引用用于 .NET，也可作为本地包用于引擎适配器。`Runtime/AwithGameFrame.Core.asmdef` 禁止引擎引用。

新应用从 `GameHost` 创建独立会话，用 `GameModule` / `GameContext` 组合模块与显式依赖。本包提供实例事件、游戏时钟/定时器、普通对象池、资源租约/缓存与流程。宿主可配置诊断输出。游戏对象、原生场景、输入、音频和引擎资源实现属于 [adapters](../../adapters/README.md)。

| 任务 | 源码 / 说明 |
| --- | --- |
| 组合模块、管理会话与主线程调度 | `Runtime/Hosting`，见下文实例宿主 |
| 切换可取消的应用流程 | `Runtime/Flow`，见下文异步流程 |
| 发布类型事件并绑定订阅作用域 | [Events](Runtime/Events/README.md) |
| 驱动游戏时钟与调度定时器 | [Timing](Runtime/Timing/README.md) |
| 借还普通 C# 对象 | [Pooling](Runtime/Pooling/README.md) |
| 共享资源加载与维护租约、有限缓存 | `Runtime/Resources`，见下文资源租约 |

修改前先在 `Runtime/` 查找对应模块；验证命令：

```powershell
dotnet build src/AwithGameFrame.Core/AwithGameFrame.Core.csproj
```

完整检查与引擎验证见 [验证参考](../../docs/reference/verification.md)。

## 实例宿主与异步流程

`AwithGameFrame.Core.Hosting.GameHost` 用于独立的应用会话，每个宿主拥有自己的服务表。

```csharp
var host = new GameHost(dispatchBudgetPerTick: 64);
host.Services.Register<IMyStorage>(storage);
host.AddModule(new MyDataModule()); // 继承 GameModule，声明 Id 与 Dependencies
var flows = new FlowMachine(host.Context);
host.AddModule(new FlowModule(flows, new BootFlow(), dependencies: new[] { "data" }));
await host.StartAsync(cancellationToken);
// 引擎每帧调用 host.Tick(deltaSeconds)，包括 StartAsync 尚未结束时。
await host.PauseAsync();
await host.ResumeAsync();
await host.StopAsync();
```

示例需引用 `AwithGameFrame.Core.Hosting` 和 `AwithGameFrame.Core.Flow`。`IGameModule` 声明 `Id`、`Dependencies`、`StartAsync`、`StopAsync`；`GameModule` 还提供可重写的 `PauseAsync`、`ResumeAsync`、`Tick`。启动前完成注册；依赖按 ID 拓扑排序，相同条件保持注册顺序，缺失依赖和循环在任何模块启动前报错。模块通过传入的 `GameContext.Services` 交换实例，不能重复注册同一服务类型；资源所有权属于模块，宿主清空注册表不代替模块释放资源。

启动失败会取消会话，并逆序清理所有已开始初始化的模块，包含失败到一半的模块；关闭尽量清理全部模块并聚合异常。清理收到未取消的 token，必须允许部分初始化状态。启动 token 仅控制启动过程；会话后台工作使用 `GameContext.LifetimeToken`。`StopAsync` 会立即取消会话和队列中已有工作，再等待当前启动/暂停回调合作退出，逆序关闭模块，最后关闭调度器。模块必须观察取消，调用方应继续帧驱动直到关闭完成；不会强行终止忽略取消的任务。关闭后和启动失败后均不能重启同一实例。

暂停按依赖顺序通知模块，恢复同样按依赖顺序；部分失败会逆序调用对应恢复/暂停操作以回滚。回滚成功时保持先前状态，回滚失败则关闭会话，避免报告虚假的 Running/Paused 状态。关闭请求可取消正在进行的暂停/恢复，不会随后重新进入 Running。生命周期回调不得重入同一宿主的生命周期；通过调度器请求后续操作。

`Tick(double)` 输入未缩放秒数，拒绝负值、NaN 和 Infinity。Starting、Paused、Stopping 时仍处理调度队列，只有 Running 时推进游戏时钟并执行定时器和模块 Tick；模块收到缩放后的 delta。模块 Tick 异常在其他模块 Tick 完成后聚合返回。宿主与调度器绑定创建线程，Tick 必须在该线程调用；模块生命周期应从宿主线程发起，并由引擎同步上下文维持 await 后的线程。Headless 宿主应提供自己的同步上下文与帧循环；这里不会安装或修改全局同步上下文。

`MainThreadDispatcher.InvokeAsync(Action, token)` 可从工作线程调用，返回的 Task 代表这项操作的完成、异常或取消。每帧最多处理指定数量的已排队项目，取消项目也占预算，回调中新排入的工作延后到下一次泵；单项异常不会阻断其余工作。队列默认容量为 4096，超出容量的请求返回失败 Task。调用方须观察这些 Task；在宿主线程同步等待排队工作会造成死锁。调度器不接收异步 void 回调，异步操作应在排队 Action 中启动并单独等待其 Task。

`FlowMachine.SwitchAsync` 先退出当前流程，再进入新流程；新的切换请求取消尚未完成的旧请求，等待其部分进入状态清理后再开始。进入失败或取消都会调用新流程的 Exit，清理失败与原始异常一起报告；旧流程退出失败时不进入新流程。`StopAsync` 取消正在进入的流程并退出当前流程，停止后不能再次切换。流程必须观察进入 token，Exit 必须能够清理部分进入状态。`FlowModule` 将初始流程、Tick 与最终退出纳入宿主生命周期；单独创建的 FlowMachine 由调用方负责 Stop。流程回调不能重入自身切换/停止；可通过宿主调度器请求下一次切换。

`GameHost.Id` 标识宿主实例，`Lifecycle` 事件提供 `HostEvent`：HostId、Start/Pause/Resume/Stop 阶段、Started/Completed/Failed 状态、ModuleId（宿主操作为 null）、ElapsedSeconds、TimestampUtc、State、Error 和 IsRollback。事件按操作顺序同步发出，耗时使用单调时钟；可直接转为 JSONL 供工具或 agent 分析。监听器的异常逐项隔离，不影响模块启动/清理，也不进入全局日志。监听器应快速返回，文件写入并发与队列策略由诊断宿主负责。

## 资源租约与作用域

`AwithGameFrame.Core.Resources` 提供引擎无关的资源生命周期。宿主实现 `IResourceLoader.LoadAsync(string key, Type resourceType, CancellationToken)`，返回 `LoadedResource(value, releaseAsync, estimatedSizeBytes)`；release 必须真实释放该次加载的后端所有权。`FileResourceLoader` 可将固定内容快照的路径解析器接为 `byte[]` 资源，不解释引擎资产格式。Unity/Godot 原生后端分别位于 adapters。

```csharp
var resources = new ResourceStore(loader, host.Dispatcher);
host.AddModule(new ResourceModule(resources)); // Start 将 store 注册到本宿主 Services
// 需要资源的模块在 Dependencies 中声明 "resources"。
await using var scope = new ResourceScope(resources);
var lease = await scope.LoadAsync<MyAsset>("asset-key", cancellationToken);
UseAsset(lease.Value);
// scope 关闭会取消尚在等待的请求，并逆序归还已有租约。
// host.StopAsync 关闭 ResourceModule；无 module 时需主动 await resources.DisposeAsync()。
```

同一 key 和请求类型的并发加载只调用一次后端，每个调用方取得独立租约；默认最后一个等待者和租约离开后释放资源。传入 `ResourceCacheOptions(maxBytes, maxEntries)` 可保留有限的空闲 LRU 缓存；预算只统计空闲资产的后端估算，不限制正在使用的资产，也不代表进程实际内存硬上限。估算为零表示未知，不缓存；单个资产超过预算也不缓存。`PreloadAsync<T>` 要求正缓存预算，`Cache` 查看当前空闲条目和估算字节，`TrimCacheAsync(targetBytes: 0)` 主动清空空闲缓存。

一个调用方取消只结束自己的等待，全部等待者取消才取消后端加载。后端不支持取消时，晚到成功值仍会释放。加载失败允许重试；返回值类型错误时先释放实际值，再报告类型错误。租约释放幂等，最后一次释放 Task 会报告后端错误；关闭 store 会尝试全部清理并聚合错误。

传入 dispatcher 时，后端 Load 调用与 release 回调在创建线程执行；异步后端自身也必须在其 await 后遵守引擎线程规则。未传 dispatcher 时由后端自行处理线程。内部原生资源释放工作不受普通队列容量限制，并在 CancelPending 后保留，仍受每帧执行预算限制；停止期间继续 Tick，直到异步清理完成。必须先关闭资源服务再最终关闭 dispatcher。

`ResourceScope` 取消尚在等待的请求，并等待它们退出本作用域；不会等待已经退订且不响应取消的底层加载。store 负责此类晚到资源，最终 `DisposeAsync` 会等待加载完成并释放。因此后端必须最终完成或响应取消，框架不会强行终止无限任务。store 关闭后所有租约的 Value 访问失效，但无法撤回调用方已另行保存的对象引用；调用方须在释放后停止使用该资源。资源实例化、场景对象归还和 UI 导航属于上层服务。

## 实例事件、游戏时间与对象池

`host.Events` / `context.Events` 是独立的强类型 `EventBus`，也注册到本宿主 Services。订阅返回可释放句柄，`CreateScope(token)` 将一组订阅绑定到页面/会话取消。`Publish<T>` 同步调用精确类型的监听器，逐项收集错误到 `EventDispatchResult`；需要失败即报错时调用 `ThrowIfFailed()`。派发使用快照，发布中修改订阅影响下一次发布。停止宿主在模块清理结束后关闭事件总线。细节见 [Events](Runtime/Events/README.md)。

`host.Clock` / `host.Timers` 提供缩放与未缩放游戏时间、一次/重复回调和 `DelayAsync`。只有 Running 推进两个时间域；宿主暂停同时冻结它们，`TimeScale = 0` 只冻结缩放域。重复执行每 Tick 最多一次、合并错过次数；到期回调受每帧预算限制。停止或启动失败取消全部定时器；不要在启动/暂停回调中等待游戏时间。细节见 [Timing](Runtime/Timing/README.md)。

需要在暂停期间轮询外部资源完成状态的模块可额外实现 `IGameMaintenanceModule.Maintain(GameContext)`。宿主仅在 Paused 的 Tick 中按已启动顺序调用维护，收集异常并继续其他维护模块；普通模块 Tick、游戏时钟和定时器仍冻结。此入口只用于清理，不应推进玩法模拟或自行累计游戏时间。

`ObjectPool<T>` 按引用身份检查借还，容量包含空闲和借出，支持预热、重置/销毁回调和 `RentLease()`；池关闭时清理自己拥有的全部对象，包括尚未归还者。池绑定创建线程，适用于纯 C# 缓冲或业务对象；它不替代引擎 GameObject/Node 的专用实例化策略。细节见 [Pooling](Runtime/Pooling/README.md)。

## 日志输出

`LoggingHost.Output` 接收日志级别、消息和可选宿主上下文。回调在调用线程同步执行，默认写入标准输出或标准错误。 [日志接入](Runtime/Logging/README.md)
