# BBads PvP

在 `Online PvP` 选择 BBads。双方各有 3×2 格背包，可各放一件武器和一份鱼。点击 Ready 锁定布置。两人就绪后倒计时 3 秒，服务端开始自动战斗。武器提高伤害，鱼与武器相邻时提供额外加成。同一步攻击时，席位 0 先结算。

`BackpackGrid` 负责放置和移动道具，`BBadsCombatRules` 负责伤害。客户端从权威快照读取背包、阶段、生命和结果。 [后台验证](integration.md)
