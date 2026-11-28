# PvP 框架支持基线

2026-11-28 以提交 `95a7eaf` 的 Test 工作区核对。当前标准构建和文档检查通过。GameHost、RealtimeClient、RealtimeServerSession 和 Test.Server 的在线探针是已有可复用基建。

对战改动合入前的依据是此处记录的已有入口。实施后通过 Test.Server 集成测试和双 Unity Player 检查可运行的对战路径。 [后台复跑](integration.md)

## 已有能力

| 能力 | 当前入口 | 结论 |
| --- | --- | --- |
| 宿主固定步 | Test.Server 的 `Program` 使用固定 Tick 和 Dispatcher | 可作为服务器驱动入口。当前没有对战房间和玩家输入状态。 |
| WebSocket 服务端 | `RealtimeServerSession` 和 Test.Server 的 `OnlineProbeModule`、`ContractScoresModule` | 可接受一个 WebSocket 会话，处理 AWRT 帧、Ping/Pong、命令和带序推送。 |
| 客户端会话 | `RealtimeClient`、`ClientWebSocketTransport` 和 Test 的 `ContractScoreSync` | 有 epoch 隔离、有限重连、断线补查和不自动重放业务消息。 |
| 战斗规则 | `CombatRun` 以 60 Hz 消费输入，输出 `FighterView`、`CombatCue` 和箭矢视图 | 已有确定性单局规则和回放基础，主要模型是一个本地玩家与关卡对手。 |
| 输入 | Rain Pass 通过 `InputService` 和 `BattleInputRouter` 绑定动作上下文 | 可接入角色输入。双方玩家身份、输入帧和服务器确认仍未定义。 |
| 验证 | `game.ps1 build`、`verify-game-unity.ps1`、`verify-online.mjs` | 可构建 Test、后台运行 Unity 和验证一个 WebSocket 成绩会话。没有两个客户端 PvP 验证入口。 |

## 未提供的对战能力

当前代码没有可复用的 PvP 房间、匹配、玩家席位、权威战斗状态、固定输入窗口、快照确认、对战结果一次提交或服务器重连恢复。Networking 的会话恢复只维护传输推送序号，不能自动恢复战斗状态。Test.Server 的现有 WebSocket 业务是成绩提交，每个连接独立处理，不共享对局。

当前 `CombatRun` 的 `ICombatSpace` 和内部 fighter 数组支持多参与者的底层形状，但公开操作和结果语义仍围绕本地玩家、AI 目标、试炼和关卡流程。是否直接扩展它，需要通过 `combat-dual-control` 的确定性测试决定。

## 基线结论

框架已经能提供 PvP 所需的传输、宿主、时钟、取消、会话 epoch 和推送恢复基础。Rain Pass PvP 还需要 Test 的房间和战斗编排、协议源、两名玩家输入、权威状态和 Unity 双端接线。服务器房间、权威模拟、输入帧和 PvP 快照没有证据证明属于公共框架，先在 Test 内实现并通过多调用方验证。

两种模式的实测结论见 [框架验证结果](framework-feedback.md)。
