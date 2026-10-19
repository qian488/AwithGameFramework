# 长连接与推送恢复

`RealtimeClient` 只依赖 BCL，组合可注入的 `IWebSocketTransportFactory`、`IRealtimeCodec` 与 `IPushRecovery`。`ClientWebSocketTransportFactory` 创建真正的 `System.Net.WebSockets.ClientWebSocket`，每次连接独立持有 socket。一次发送和一次接收可以并行；同方向操作串行。消息以完整 binary WebSocket message 交付，分片累加大小及发送大小都受限，text frame 被拒绝。

```csharp
using AwithGameFrame.Networking.Realtime;

await using var realtime = new RealtimeClient(
    new Uri("wss://your-service.example/realtime"),
    new ClientWebSocketTransportFactory(), new BinaryRealtimeCodec(),
    yourPushRecovery,
    new RealtimeOptions(maxReconnectAttempts: 3));

Task session = realtime.RunSessionAsync(savedCursor, ApplyPushAsync,
    new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
    cancellationToken);
// 会话拥有者观察 session；连接和初次补查完成后 IsConnected 才为 true。
// 发送只尝试一次，调用方处理失败和服务端幂等键。
if (realtime.IsConnected) await realtime.SendAsync(encodedBusinessRequest, cancellationToken);

await realtime.StopAsync();
try { await session; }
catch (OperationCanceledException) { }
```

`RunSessionAsync(lastAppliedSequence, applyPushAsync, headers?, token)` 的返回任务代表整个会话生命周期，必须观察其故障。再次调用立即递增 epoch，使旧会话失效并取消它。`StopAsync`/`DisposeAsync` 等待当前及所有被替换会话的连接清理；Stop 本身不重复抛出会话故障，错误保留在对应的会话任务。`LastAppliedSequence` 保留最终成功游标，下一次 Run 显式传入自己的起始游标。

连接、收包、恢复和回调等待都会响应取消。忽略取消的 transport/业务任务会被放弃等待，其迟到异常会被观察；注入的 transport 仍须在 DisposeAsync 中真正中断 I/O 并释放资源。默认 transport 以 Abort/Dispose 结束等待，关闭输出使用独立有限超时，不承诺所有退出都完成双向关闭握手。注入的 DisposeAsync 必须有界并完成资源清理。

业务回调在后台延续中执行，需要主线程时由项目显式调度，并在真正应用结果前检查取消。库防止失效 epoch 的后续回调和游标提交；已经开始且忽略取消的回调仍可能产生自己的副作用，无法被撤销。回调应合作取消或按 sequence 幂等，持久化业务状态和游标的一致性由项目保证。

## 推送顺序

推送序号是同一逻辑会话内从 1 开始的连续 `long`。`sequence <= LastAppliedSequence` 被忽略，下一条连续消息在回调成功后才提交游标。推送串行处理，收包/Ping/Pong 不等待业务回调；有界待处理队列溢出会明确失败，避免无限增长。

每次成功连接（含第一次）及发现序号缺口时，调用 `IPushRecovery.RecoverAsync(lastAppliedSequence, token)`。该服务由项目实现，可以通过 HTTP 或其他接口补查。返回 `PushRecoveryBatch(throughSequence, pushes)` 必须包含 `(lastAppliedSequence, throughSequence]` 的全部推送，按连续序号排列；空批次只能声明 `throughSequence == lastAppliedSequence`。整个批次先校验，再逐条应用。若正在补缺口，high watermark 至少到达触发消息的前一条；批次也可以包含触发消息及更多消息，后续 live duplicate 会自然忽略。

例：已应用 10，收到 13。补查需返回 `[11, 12]` / watermark 12，或 `[11, 12, 13, ...]` / 对应 watermark。空批次、`[11, 13]`、逆序或过期 watermark 均以 `PushRecoveryException` 结束会话。回调失败以 `PushApplicationException` 结束会话，失败消息不提交游标；先前成功消息保持已提交。库不向服务器发送推送 ACK，也不提供服务端实现或游标持久化。

## 心跳与重连

心跳是应用协议中的 Ping/Pong，不依赖底层 WebSocket keepalive。客户端定期发送带正整数 ID 的 Ping，只接受 ID 相同的 Pong；服务端 Ping 会收到对应 Pong。发送及确认共享 heartbeat deadline；超时使当前连接释放并按策略重连。

`RealtimeOptions` 配置单条消息大小、队列容量、连接超时、心跳间隔/超时、关闭超时和重连次数。重连预算按整个 Run 会话累计，成功连接不会重置预算。第 n 次重试延迟为 `min(initialDelay * 2^(n-1), maxDelay)`，默认最多 3 次、从 250ms 到最多 10s；等待可取消。连接/收包 I/O 失败、对端关闭或心跳超时可重连；协议、恢复或业务回调错误直接结束会话。项目负责凭证更新与再次启动会话。

**业务消息不自动重放。** Send 失败或取消只说明本地等待结束，服务端可能已执行。连接切换后，排队等待发送的旧连接调用也不会转移到新连接；项目通过业务幂等键、查询状态和服务端契约决定是否重试。

## 内置 AWRT v1 协议

`BinaryRealtimeCodec` 是明确的示例协议，已有服务端可替换 `IRealtimeCodec`。布局按以下顺序，整数采用 little endian：

| 字段 | 字节 | 规则 |
| --- | --- | --- |
| magic | 4 | ASCII `AWRT` |
| version | 1 | 1 |
| kind | 1 | Business=1 / Push=2 / Ping=3 / Pong=4 |
| sequence | 8 | Push 为正；其他为 0 |
| heartbeatId | 8 | Ping/Pong 为正；其他为 0 |
| payloadLength | 4 | 非负，必须等于后续字节长度 |
| payload | N | Ping/Pong 必须为空 |

客户端 Business 由服务端按业务协议处理；服务端交付给客户端的业务内容统一使用 sequenced Push。框架串行调用 codec，字节数组交付后应视为不可变。

## 验证范围

自动测试包含可注入 transport 的断线/退避/心跳/取消/补查/业务失败/旧会话隔离，以及 .NET 8 localhost 真 WebSocket 的握手、分片、发送、限制、取消和完整 RealtimeClient 心跳交互。通用源码以 C# 9 / netstandard2.1 编译；引擎导入和 AOT 可编译不能证明所有目标平台实际联网可用。浏览器/WebGL、移动设备、TLS/代理、证书和实际业务后端需要对应平台验收；可替换 transport 接入平台专用实现，浏览器握手也可能限制自定义请求头。
