# Rain Pass 双人控制切片

## 范围

本切片把 Rain Pass 的双人固定步战斗留在 Test 游戏代码中。Domain 持有规则和状态转移，Application 持有服务器未来可调用的固定步入口。现有 `CombatRun` 继续服务单人试炼和关卡流程，本切片没有改动它、协议、Test.Server、Unity 或公共框架。

## 入口与数据边界

`PvpInputFrame` 是一次固定步的玩家输入，包含 `Tick`、`PlayerId`、`PvpButtons`、移动向量和瞄准向量。每次 `PvpCombatMatch.Advance` 必须同时收到玩家 0 和玩家 1 的下一帧。帧号必须从 1 开始连续递增，玩家编号必须匹配输入槽位。服务器可以在缺少输入时提交 `PvpInputFrame.Neutral`，从而保持模拟步数连续。

`PvpAuthoritativeSnapshot` 是 Application 对外的只读状态边界，包含双方生命、架势、武器、箭数、位置、朝向、攻击阶段、格挡、闪避、移动、破势和锁定目标，以及飞行中的箭。快照带有由固定二进制字段顺序计算的 SHA-256 `StateHash`。哈希同时覆盖恢复、攻击和处决所需的内部计时字段，重复运行同一输入序列时可以比较完整状态。

`PvpStepResult.Cues` 只返回本固定步产生的表现事件。每个 Cue 带有单调递增的 `Sequence`、发生 `Tick`、攻击者、目标、`ContactResult` 和弓箭标记。`PvpStepResult.Result` 只在首次完成的固定步返回结果。终局后再次调用 `Advance` 返回同一终局快照、空 Cue 和空结果，避免重复结算。

## 固定步规则

- 模拟频率固定为 60 Hz。双方输入先按玩家编号处理，再按玩家 0、玩家 1 的顺序检查近战接触和箭矢命中，顺序属于该切片的确定性约定。
- 近战沿用 `CombatRules.Resolve` 的生命、架势、格挡、弹反、闪避和破势窗口。Rain Pass 单手剑使用现有单人攻击参数，弓箭使用 `BowRules.Shot` 和 `BowRules` 的飞行推进。
- `Guard` 是持续输入，首次按住的固定步记录弹反起点。`Dodge` 使用现有 14 步窗口。`Finish` 在目标破势且距离满足条件时按现有 21 步持续输入触发 `CombatRules.Finish`。
- `EquipSword`、`EquipBow`、`Attack`、`AttackPressed` 和 `Lock` 是当前固定步命令。攻击只在角色可行动时启动，弓箭在有效帧发射并消耗一支箭。
- 玩家生命归零时创建一次结果，记录结束步、胜者和败者。结果创建后模拟不再推进，也不会重新发出终局 Cue。

## 验收

`CombatDualControlTests` 覆盖相同双输入序列的状态哈希和 Cue 顺序、普通格挡、弹反、闪避、弓箭命中、破势处决、一次性终局结果和输入帧边界。验证命令为

```powershell
dotnet test games/Test/tests/Test.Application.Tests/Test.Application.Tests.csproj --no-restore --filter FullyQualifiedName~CombatDualControlTests
```

该入口只验证 Test.Application 的新增 PvP 组合，不要求协议或引擎接入。后续 `server-match` 可以直接消费 `PvpCombatMatch.Advance`，再自行负责房间、连接、输入窗口和传输协议。
