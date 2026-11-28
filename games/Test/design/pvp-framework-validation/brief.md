# Test PvP 对战与框架能力验证

把 Test 的 Rain Pass 单人试炼改造成由本机 Test.Server 支持的双客户端实时 PvP 原型，再将验证过的对战会话能力用于 BBads 的双方背包对战。第一阶段使用两个本机客户端和回环 Test.Server，固定 Unity 2022.3.16f1、Test.Server 运行版本、地图、角色、武器、输入采样和模拟频率。真实公网部署、匹配服务、账号和生产反作弊不进入本次需求。

## 已确认的玩家路径

1. 两个客户端连接本机 Test.Server，创建或加入同一对战房间。
2. 双方各控制一个 Rain Pass 角色，在固定地图中移动、锁定、攻击、格挡、弹反、闪避、弓箭和处决。
3. Rain Pass 使用帧同步。服务器以固定步汇总并广播双方输入，两端运行同一份战斗规则生成各自的表现数据。服务端也运行规则以校验与裁决。
4. 一方生命或架势达到现有规则的结束条件后，服务器只提交一次胜负结果。双方可以退出、重连或按规则重新开始。
5. 关闭一个客户端或注入网络延迟、乱序、重复、断线和服务器重启时，另一方收到明确的房间状态。Rain Pass 重连时补发缺失输入帧，BBads 重连时下发最新完整快照。
6. BBads 使用状态同步。服务端推进双方背包准备、倒计时、自动战斗和结果结算，客户端应用权威状态。两种模式共用房间、连接和传输。

## 当前基线与需要验证的候选缺口

Networking 已有真实 WebSocket transport、Ping/Pong、有限重连、会话 epoch、推送序号补查和不自动重放业务消息的边界。Test.Server 已有回环 HTTP/WebSocket 宿主、固定步长驱动和成绩会话。CombatRun 已有 60 Hz 输入消费、确定性的领域规则、战斗快照和 CombatCue，但当前模型主要是本地玩家对 AI 或关卡流程。 [Networking](../../../../src/AwithGameFrame.Networking/README.md) · [Test.Server](../../server/Test.Server/README.md) · [战斗应用](../../src/Test.Application/README.md)

本次先确认以下问题，再把确认的公共缺口落到 Core、Networking、Gameplay 或 Unity 适配层。没有复现证据的能力继续由 Test 组合。

- 是否需要通用的固定步输入帧、权威快照、确认号和房间生命周期契约。
- RealtimeClient 的补查和 epoch 隔离能否覆盖两个客户端的对战状态恢复。
- GameHost 的 Tick、暂停、停止和 Dispatcher 能否承载服务器固定步与客户端渲染帧，且不把平台时间带入领域模拟。
- Test.Server 当前模块能否管理房间、两个连接、输入顺序、迟到和重复输入、结果一次性提交及重连。
- Unity 客户端能否把 InputService 的动作事件转换为输入帧，并按场景宿主正确释放连接、订阅和资源。
- 现有 CombatRun 是否需要扩展参与者数量、输入来源、权威快照和事件序号，或者应建立独立的 PvP 应用编排层。
- BBads 的准备阶段、双方资源、自动战斗和结果同步是否复用 Rain Pass 对战会话契约。

## 职责边界

- Test.Domain 持有可确定性验证的 PvP 规则、输入帧处理、状态转移和结果规则。
- Test.Application 持有房间对局、服务器命令、客户端状态提交和 Rain Pass/BBads 的业务编排。
- Test.Server 持有本机房间、连接、固定步驱动、输入队列和故障注入。
- Unity 客户端持有原生输入、画面、动画、声音、场景和网络会话接线。两客户端的表现状态由权威应用状态驱动。
- Networking 只提供通用传输、协议编解码、会话、序号和恢复接口。房间、玩家、战斗快照和反作弊规则不直接进入公共 Networking。
- Core/Gameplay 只有在多个真实调用方证明需要时才提取固定步、会话或生命周期契约。不要为了本次 PvP 把 Rain Pass 规则提前泛化成游戏框架业务。

## 大需求切片

详细读写范围和验收见 [AgentKit 看板](board.json)。

| 工单 | 交付 |
| --- | --- |
| `baseline` | 用现有 Test.Server、CombatRun、RealtimeClient 和两个客户端入口固定对战基线与框架缺口。 |
| `combat-dual-control` | Rain Pass 两名人类角色的领域规则、输入帧、权威快照、结束条件和确定性测试。 |
| `pvp-protocol` | 房间、加入、输入帧、快照、确认号、事件和恢复的协议源、生成检查和兼容性基线。 |
| `server-match` | Test.Server 双客户端房间、固定步、输入窗口、迟到/重复处理、结果一次提交和故障注入。 |
| `client-session` | Unity/Test.Application 两客户端连接、会话 epoch、快照应用、输入发送、退出和重连。 |
| `pvp-presentation` | Rain Pass PvP 入口、连接状态、双方 HUD、输入路由、结束与重赛表现。 |
| `determinism-replay` | 两客户端相同输入和服务器回放的状态 hash、Cue 顺序、快照序号与跨运行一致性。 |
| `recovery` | 延迟、乱序、重复、断线、服务器重启和旧会话迟到消息的恢复验证。 |
| `bbads-pvp` | 使用已验证的对战会话验证 BBads 双方准备、倒计时、自动战斗和结果同步。 |
| `framework-fixes` | 按真实复现结果为公共框架和工具建立最小修复及现有回归。 |
| `integration` | 后台运行 Test.Server、两个客户端和 Test 原有粒子、Rain Pass、BBads 回归，整理机器和人工验收。 |

## 后台验证与人工验收

默认使用两个隐藏 Test 客户端、一个隐藏 Test.Server 和注入 transport。至少记录服务器版本、客户端版本、协议 hash、输入帧范围、状态 hash、服务器 tick、连接 epoch 和证据路径。真实 Unity 验证使用隔离工程，客户端工程退出后自动删除验证副本和 Unity 缓存。

机器验收覆盖配置、协议、Domain/Application 测试、服务器房间、双客户端 PlayMode、断线恢复、确定性回放和既有 Test 回归。人工验收从 `CombatShowcase` 的 PvP 入口开始，检查两端角色响应、延迟下的表现、结束与重赛、断线提示和返回 Test 其他场景。随后从 BBads 入口检查双方准备和自动战斗。人工观察不代替服务器、领域和协议测试。
