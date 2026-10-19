# 普通对象池

`AwithGameFrame.Core.Pooling.ObjectPool<T>` 管理引用类型，不依赖引擎。容量是全部受管对象的硬上限，包含已借出对象；没有空闲对象且达到容量时，借用会明确失败。

```csharp
using var pool = new ObjectPool<WorkItem>(
    factory: () => new WorkItem(), capacity: 32,
    onRent: item => item.Reset(),
    onReturn: item => item.Clear(),
    destroy: item => item.Dispose());
pool.Prewarm(8);
using (var lease = pool.RentLease())
{
    Process(lease.Value);
}
```

`Prewarm(count)` 确保至少 count 个空闲对象，仅调用工厂，不触发 onRent/onReturn。它先检查借出数量与容量；批量创建失败时销毁本批新增对象，保留原有空闲和借出对象。`CountAll`、`CountAvailable`、`CountRented` 提供当前计数。

`Rent()` / `Return(T)` 提供手动借还，按引用身份检查所有权，拒绝外部对象和重复归还。工厂不能向同一池再次交付曾交付过的引用，包括已经销毁的引用；重复交付不会再次销毁该对象。历史身份使用弱引用跟踪，不保留已销毁对象。原始引用无法证明新旧借用轮次，调用方必须停止使用已归还的引用；推荐使用 `RentLease()`。

租约 `Dispose` 幂等，归还后 `Value` 失效；租约借出的对象必须通过租约归还，不能传入 `Return(T)`。即使归还回调失败，租约也会失效，故障对象会被移出并销毁，后续租约关闭不会重复归还。跨线程误用不会消费租约，可以回到创建线程关闭。

onRent/onReturn 失败时移出并销毁故障对象；原始错误和销毁错误同时发生时以 `AggregateException` 返回。`Dispose` 会关闭池、失效所有租约、尝试销毁全部空闲和借出对象，再集中报告清理错误；重复关闭不会再次销毁。关闭不触发 onReturn。未配置 destroy 时，若对象实现 `IDisposable` 则调用它，否则仅释放池的引用。关闭前应停止使用原始借出引用，框架无法撤回调用方持有的对象。

所有池操作、计数读取和有效租约访问都必须在创建线程进行，跨线程明确抛出异常。工厂及生命周期回调中可以读取计数，但不能重入借还、预热或关闭；需要引擎线程时由调用方显式调度。池不创建线程，也不隐式接入 GameHost 的所有权；模块负责在自己的关闭阶段销毁池。

验证：`dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter FullyQualifiedName~ObjectPoolTests`。
