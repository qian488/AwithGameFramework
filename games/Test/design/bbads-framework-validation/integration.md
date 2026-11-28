# BBads 专项集成验收

2026-11-27 使用 Test 当前源码与 Unity **2022.3.16f1**，由 AgentKit 运行隔离后台验证。EditMode **16/16**，PlayMode **28/28**，包含 17 个 BBads 用例和 11 个原有 Rain Pass、粒子及宿主用例。证据位于 `artifacts/game-unity/Test/20261127-124247-2ac1547a`，其中 `summary.json`、`EditMode.xml` 和 `PlayMode.xml` 记录运行范围与结果。

| 工单 | 集成结果 |
| --- | --- |
| 基线 | 固定了迁移工作区、编辑器版本、六份 CSV 指纹与已有 Player 行为。 [基线](baseline.md) |
| 宿主与转场 | 双击进入、重开、退出、重进，四次有效操作各加载一次。激活故障清理候选场景并恢复旧场景和输入，使用原按钮可重试。 [转场](host-transition.md) |
| 资源租约 | 两个消费者共享真实资源。取消后交付的旧结果归还一次。新场景保留自己的资源，活动和闲置单位先于 Prefab 租约销毁。 [资源](resource-leases.md) |
| 时间与事件 | 有待推进的波次、充能和在途投射物同时冻结，恢复后依序处理一次。退出后的旧宿主定时器和订阅被释放。 [时间与事件](time-events.md) |
| 对象池 | 三轮真实对局的单位与投射物峰值稳定，退出后实例归零。旧投射物对借用同一对象的新单位不提交单体伤害。 [原生对象池](pool-performance.md) |
| 输入 | 使用场景的 Unity 事件和射线覆盖放置、非法落点、退托盘和召唤。框架输入模态、暂停及失焦均取消旧拖拽。 [拖拽](drag-input.md) |
| 内容工具 | 注入并恢复过期配置、坏字段、缺失资源键、断开的 Sprite 和 HUD 异常。布局和像素检查均得到预期结果。 [工具故障注入](content-tools.md) |
| 框架补强 | Unity 场景重复激活恢复幂等。游戏验证支持定向后台回归和失败阶段报告，AgentKit 可以运行游戏自己的 Unity 测试。 [框架补强](framework-fixes.md) |
| 集成 | 从 Test 的粒子场景进入 BBads，首次进入、重开、退出后重进三轮均通过，每轮记录 7 个敌方生成、3 次道具出现和摆放、1 次召唤、实际伤害，以及败北后停止刷怪。另一个缩短的真实配置时间轴只完成一次。 [集成用例](../../client/Unity/Assets/Tests/PlayMode/BBadsFrameworkIntegrationTests.cs) |

`tools/verify.ps1` 完成文档、架构、AgentKit 契约、构建、578 项框架测试、13 项模板测试和 Headless 示例。专项的日志为 `artifacts/bbads-framework/verify-current.log`。隐藏 Direct3D11 批处理复跑了当前场景 HUD，540×960 的正常与恢复 PNG 哈希相同，隐藏波次文字后像素有变化。截图和 XML 位于 `artifacts/game-unity/Test/20261127-122002-448c1d05/final-hud-captures` 和同级 `final-hud.xml`。

正式看板的 `accepted` 需要独立的人类评审。实现和机器证据在本报告中核对。人工检查可在 Test 的 `SampleScene` 点击 **Open BBads**，关闭自动摆放后核对真实鼠标拖放、多分辨率反馈、转场音画与暂停观感。 [工单看板](board.json)
