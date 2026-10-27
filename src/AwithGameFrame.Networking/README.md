# AwithGameFrame.Networking

可选的 HTTP/协议、文件内容更新与 WebSocket 客户端基础包，依赖 Core 与 BCL，目标 `.NET Standard 2.1`。运行时只放在 `Runtime`，.NET 项目和 UPM 引用同一份源码；不依赖引擎、UI、Data 或额外 JSON 包。

```csharp
using System.Net.Http;
using AwithGameFrame.Networking;

using var transport = new HttpClientTransport();
using var client = new ProtocolClient(
    new Uri("https://your-service.example/api/"), transport,
    new DataContractJsonProtocolCodec(), defaultTimeout: TimeSpan.FromSeconds(5));
client.BeginSession(new Dictionary<string, string> { ["Authorization"] = "Bearer " + token });
var endpoint = new ProtocolEndpoint<PlayRequest, PlayResponse>(HttpMethod.Post, "level/play");
var result = await client.SendAsync(endpoint, new PlayRequest { LevelId = 7 }, cancellationToken: cancellationToken);
if (result.IsSuccess && client.IsCurrentEpoch(result.SessionEpoch)) Apply(result.Data);
```

`HttpClientTransport` 可注入 `HttpMessageHandler`，用于平台集成和实际 HTTP 管线测试；它持有并释放自己的 HttpClient，默认也释放 handler，可设 `disposeHandler: false`。内置 HttpClient 超时关闭，由上层请求的独立计时器统一控制；默认 handler 禁止自动重定向。自定义 handler 的重定向和重试策略由注入方负责。`ProtocolClient` 不释放共享 transport，宿主先释放 client，再释放 transport。

`IHttpTransport.SendAsync(TransportRequest, token)` 每次发送一个绝对 HTTP(S) 请求，返回真实状态码和字节正文；连接错误和取消可抛出 BCL 异常。内置实现使用 `ResponseHeadersRead`，向发送及正文读取传播取消。请求头按请求设置，不修改共享 DefaultRequestHeaders。TransportRequest 复制传入字节和请求头；调用方应将公开的 Body 视为不可变。

`HttpClientTransport` 默认最多缓冲 16 MiB 响应正文（`DefaultMaxResponseBodyBytes`），包含非 2xx 响应。可用 `new HttpClientTransport(maxResponseBodyBytes: 1024 * 1024)`，或向注入 handler 的构造函数传入同名参数；`MaxResponseBodyBytes` 可读取配置，0 仅允许空正文，负数拒绝。先检查 `Content-Length`，再累计实际读取的字节，因此缺少或低报长度也不能绕过上限。超限抛出说明限制字节数的 `HttpRequestException`，立即释放响应；`ProtocolClient` 将其报告为 `NetworkError`。取消仍传播 `OperationCanceledException`，不改写为超限。

`ProtocolEndpoint<TRequest,TResponse>` 显式定义 HTTP 方法、相对路由和是否发送正文。GET/HEAD 等无正文请求指定 `hasRequestBody: false`，可使用 `EmptyRequest`。基础 URI 按标准 Uri 规则合并路由，目录前缀应以 `/` 结尾；查询参数由宿主正确编码。绝对路由、authority 路由及 fragment 会被拒绝。`SendAsync(endpoint, data, timeout?, token)` 默认超时 15 秒，单次可覆盖，`Timeout.InfiniteTimeSpan` 关闭超时。

`NetworkResult<T>` 分开表示三类结果：

- `Failure`：Timeout、CallerCancelled、SessionChanged、ClientDisposed、NetworkError、HttpError、EncodingError、InvalidResponse 或 None。调用方取消返回 CallerCancelled，不会伪装成超时，也不会将正常取消写入日志。
- `HttpStatusCode`：收到完整 HTTP 响应时保留真实状态；非 2xx 返回 HttpError，不将 401/500 映射为业务错误或客户端异常，也不尝试解码其正文。
- `BusinessStatus`：仅成功解码协议封装后存在；0 为业务成功，其他值保留服务端状态和 Message。业务失败时 Failure 仍为 None，Data 不交付。`IsSuccess` 同时要求无传输错误与业务状态 0。

空正文、缺少必填状态和非法响应返回 InvalidResponse。端点、超时、请求头等编程参数错误仍可抛出异常。此包没有 UI 提示、登录凭证失效策略、奖励处理或服务器业务码；由应用层根据结果处理。

`BeginSession(headers)` 复制新会话头、递增 epoch，并取消所有旧在途请求。响应在解码前及交付前核对 epoch，旧会话不会交付 Data；即使 transport 忽略取消，调用等待也会结束，迟到任务的异常会被观察。底层仍须尽力合作取消以释放网络资源。已经完成的结果随后被排队到主线程时，应用应在实际应用结果的时刻检查 `IsCurrentEpoch(result.SessionEpoch)`；库不能撤回调用方已经拿走的数据。Dispose 取消等待中的调用并禁止新调用。

取消原因同时出现时依次优先：客户端关闭、会话变化、调用方取消、超时。超时/取消只代表本地停止等待，不能证明服务端没有执行操作；**不自动重试任何请求**，尤其支付、购买、领取等有副作用请求。业务可通过幂等键及服务端契约设计显式重试。

协议扩展点是 `IProtocolCodec`：`Encode<T>` 产生正文，`Decode<T>` 产生 `ProtocolEnvelope<T>`，`ContentType` 指定媒体类型。内置纯 BCL `DataContractJsonProtocolCodec` 使用 `{ "status": 0, "message": "optional", "data": ... }`；status 必填。DTO 推荐以 `[DataContract]` / `[DataMember(Name = "wireName")]` 固定字段名，Dictionary 使用简单 JSON 对象格式，日期与枚举遵循 DataContractJsonSerializer 规则。已有 JSON/MessagePack/Protobuf 协议可由宿主实现 codec，不能假设不同 JSON 库的日期和多态规则相同。

运行时包负责 HTTP 传输、类型化协议契约、编解码器和会话隔离，不依赖代码生成工具。仓库提供可选的 schema 驱动工具 `tools/ConfigGen/ProtocolCompiler.cs`：`samples/Protocol/schema.json` 定义 endpoint、HTTP 方法/相对路径及请求响应字段，`baseline.json` 保存兼容性审查基线，产物 `Generated/GameProtocol.cs` 包含 `[DataContract]` DTO 和 `ProtocolEndpoint<TRequest,TResponse>`。

在仓库根目录执行：

```powershell
powershell -File tools/generate-protocol.ps1
powershell -File tools/generate-protocol.ps1 -Check
```

`-Check` 检查生成代码是否过期，并校验协议兼容性：已有 endpoint 不可删除，HTTP 方法和路径不可改变；已有字段不可删除、改类型或改必填性；新增字段必须可选；协议版本不可降低。基线调整属于明确的协议升级决策。当前生成器支持固定路径 HTTP 和 `int`、`long`、`bool`、`double`、`string` 标量字段，GET/DELETE 只支持空请求；查询参数绑定、复杂对象/集合及跨语言生成尚未实现。详见[协议示例](../../samples/Protocol/README.md)。

`Runtime/Realtime` 提供真实 `ClientWebSocketTransport` 和可注入 transport factory，`RealtimeClient` 管理应用 Ping/Pong、有限指数退避重连与 epoch 隔离。`RunSessionAsync(cursor, applyPushAsync, headers?, token)` 返回会话生命周期任务；每次连接及推送缺序均通过显式 `IPushRecovery` 补查连续序号，只有应用回调成功才提交游标。`SendAsync` 始终单次发送，断线后不重放业务消息。Stop/Dispose 等待包括被替换会话在内的连接清理。项目提供恢复接口、鉴权、服务端协议、业务幂等和游标持久化，必须观察会话任务的异常。完整用法、AWRT v1 格式及取消边界见[长连接说明](Runtime/Realtime/README.md)。

`Runtime/Content` 提供 raw 文件发布清单、增量复用、HTTP Range 续传、hash 校验、原子激活/回滚及固定版本快照。此链路与引擎资产构建分开，见[内容更新说明](Runtime/Content/README.md)。

此包不提供推送 ACK 或服务器进程。HttpClient/ClientWebSocket 在具体引擎、AOT 和目标平台上的支持由宿主验证；浏览器平台可以替换 transport，不能把包的可编译性当作已验证所有平台联网。忽略取消的业务回调可能保留自身副作用，项目应合作取消或幂等；客户端只保护后续交付和游标提交。

```powershell
dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter "FullyQualifiedName~NetworkingTests|FullyQualifiedName~RealtimeTests|FullyQualifiedName~WebSocketTransportTests|FullyQualifiedName~ContentUpdateTests"
```
