# 游戏时间与定时器

每个 `GameHost` 创建独立的 `Clock` 和 `Timers`，同一实例可从 `GameContext` 访问，也注册到本宿主的 `Services`。`host.Tick(unscaledSeconds)` 输入未缩放的秒数；模块 `Tick` 收到本帧缩放后的秒数，默认 `TimeScale = 1` 保持原行为。

`Clock.ScaledTime` / `ScaledDeltaTime` 乘以 `TimeScale`，`UnscaledTime` / `UnscaledDeltaTime` 不乘。时间缩放只能在宿主线程设置，必须为有限非负数。时间只在宿主 `Running` 时推进；启动、暂停、恢复过渡及关闭期间两个游戏时间域都冻结，帧间隔置零，主线程调度器继续处理工作。`TimeScale = 0` 仅冻结缩放域；宿主暂停同时冻结两个域。这里没有暂停期间仍推进的真实时间域。

```csharp
using AwithGameFrame.Core.Timing;

var once = host.Timers.Schedule(0.5, SpawnEnemy);
var repeating = host.Timers.Repeat(1, RefreshCounter, GameTimeDomain.Unscaled);
await host.Context.Timers.DelayAsync(2, GameTimeDomain.Scaled, cancellationToken);
repeating.Cancel(); // Dispose 也取消；停止宿主会取消所有待执行任务。
```

`Schedule` 和 `DelayAsync` 接收有限非负秒数；零秒在下次运行中的定时器轮次完成，不会同步调用。`Repeat` 的间隔必须大于零。创建定时器及泵由宿主线程执行；`Cancel`、`Dispose` 和令牌取消可从其他线程请求。不要在启动或暂停/关闭回调中等待游戏时间 Delay，这些阶段不会推进游戏时间；需要现实时间等待时使用宿主自己的计时设施。

宿主先处理 dispatcher，再更新时间、运行到期定时器，最后 Tick 模块。同一轮到期工作在同一时间域内按截止时间、相同截止时间按注册顺序执行；不承诺不同域之间的语义先后。回调快照不会执行回调中新注册的定时器；每帧至多 `timerCallbackBudgetPerTick` 次回调（默认 64），预算外的到期工作保留，并优先于以后轮次新到期的工作，避免小预算下被重复定时器饿死。重复定时器每帧至多一次，错过的次数合并为一次，下一次截止时间设为本帧当前游戏时间加间隔，不进行逐次追赶。

`TimerHandle.Completion` 表示一次回调的成功/失败或取消；重复定时器仅在取消或失败时完成。取消在一次回调开始前阻止它执行；已经开始的一次回调不会被撤回，其 `Cancel()` 返回 false。重复回调可以在执行中取消自身，之后不会再执行。取消/关闭不会强行终止正在执行的用户代码。关闭宿主立即取消待执行定时器和 Delay；正常关闭、启动失败和暂停回滚失败都会关闭调度器。

回调异常终止该定时器，使其 Completion 失败；其他到期回调和模块仍继续，最后由 `host.Tick` 聚合报告。调用方须观察 TimerHandle 的 Completion 和宿主 Tick 的异常。回调必须同步、快速返回；不要将异步 void 回调传给 `Action`。可在回调中启动并单独观察 Task，或通过宿主 dispatcher 请求后续工作。
