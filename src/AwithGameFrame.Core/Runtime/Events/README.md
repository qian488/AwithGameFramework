# 实例事件

`AwithGameFrame.Core.Events.EventBus` 与旧的静态 `EventCenter` 独立。每个 bus 拥有自己的强类型订阅表，按发布时显式指定的 `T` 匹配，不自动通知基类或接口订阅。

```csharp
using var events = new EventBus();
using var screen = events.CreateScope(screenLifetimeToken);
using var subscription = screen.Subscribe<ScoreChanged>(message => UpdateScore(message.Score));
EventDispatchResult result = events.Publish(new ScoreChanged(10));
result.ThrowIfFailed(); // 也可逐项记录 result.Errors。
```

`Subscribe<T>(Action<T>, CancellationToken)` 返回幂等 `IDisposable`；取消 token 或关闭所属作用域会自动移除订阅。作用域也支持自身的 lifetime token。移除订阅会释放委托、bus/作用域引用和取消注册；关闭 bus 会同时关闭所有作用域，并拒绝后续订阅、创建作用域和发布。

发布按注册顺序捕获监听器快照，然后在锁外同步调用。允许监听器重入发布；注册、取消、退订、关闭对下一次发布生效，已经捕获的快照仍会执行完。因此 `Dispose` 不等待正在运行的发布，也不能撤回其已捕获的回调。需要保证窗口关闭后绝不执行旧回调时，应先在宿主线程停止发布，再结束作用域。

单个监听器异常不阻止后续监听器，`EventDispatchResult` 返回本次调用次数、按监听顺序排列的异常与 `IsSuccess`。调用方负责观察结果；异步业务应显式管理自己的 Task，`async void` 的后续异常不属于同步派发结果。

订阅表支持并发访问；回调始终运行在 `Publish` 调用线程，同一监听器可能被并发发布同时调用。框架不会创建线程或隐式调度。引擎业务应通过宿主 Dispatcher 将发布送到引擎线程；监听器自身需遵守所接触对象的线程约束。

验证：`dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter FullyQualifiedName~EventBusTests`。
