# Test PvP 客户端会话

`PvpClientSession` 组合公共 `RealtimeClient`、AWRT 编解码器和 Test 协议。

- Rain Pass 从第 0 步初始化本地模拟，逐步应用服务器汇总的双方输入。客户端计算状态和表现事件，并逐步核对服务端状态哈希。
- BBads 将服务器快照提交给表现层。快照包含双方背包、准备状态、倒计时、生命和表现事件。

Unity 通过会话订阅表现数据，并在主线程更新角色和 HUD。 [操作与验证](integration.md)
