# PvP 对框架的验证

- `GameHost` 驱动 Test.Server 固定步和模块生命周期。两种模式复用 AWRT 与 `RealtimeClient`。PvP 需要主动向两端推送，Test.Server 为此使用每个连接独立的有界发送队列。公共 `RealtimeServerSession` 继续服务现有请求应答入口。
- Rain Pass 验证有序输入帧、跨运行时本地演算、逐步校验和从模拟步游标补帧。BBads 验证服务端演算、完整快照应用和断线后的状态恢复。
- 协议生成器原先拒绝具名 DTO 内嵌套记录数组。双人输入帧复现了该限制，ConfigGen 已补上生成和兼容性测试。 [协议生成说明](../../../../tools/ConfigGen/README.md)
- 输入缺失策略、对局历史和 BBads 状态模型由 Test 管理。 [实测入口](integration.md)
