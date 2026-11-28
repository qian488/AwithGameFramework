# Test PvP 确定性与回放基础

`Test.Domain.Combat.Pvp.PvpCombatSimulation` 接收同一固定 Tick 的两个 `PvpInputFrame`，按席位处理输入并生成只读状态。`Test.Application` 生成权威快照，并以固定字段顺序计算 SHA-256 `StateHash`。

`CombatDualControlTests` 使用相同输入序列运行两份模拟，比较每一帧状态哈希与 Cue 序列，同时覆盖格挡、弹反、闪避、弓箭、处决和终局幂等。服务器探针再比较两个实时客户端收到的相同 `StateHash`。

当前回放以固定输入序列重复驱动应用模拟并逐步比较状态哈希与事件，服务端与客户端通过同局推送核对终局。

Rain Pass 的正常对局以及补帧均用应用模拟生成状态。初始对照发现 Mono 和 .NET 8 对向量的带符号零编码有差异，斜向发箭时的速度也存在末位差异。哈希统一零编码，战斗向量的长度、内积、归一化和射箭初速度使用明确的标量计算顺序。Windows Unity Player 和 .NET 8 Test.Server 以实跑验证跨运行时一致性。
