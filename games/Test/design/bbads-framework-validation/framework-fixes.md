# BBads 复现驱动的框架补强

| 归属 | 复现 | 修改与回归 |
| --- | --- | --- |
| Unity 适配 | 直接打开 Test 的 `SampleScene`，再通过场景服务激活其管辖的同路径句柄。服务重复激活时，Unity 对已经活动的场景返回 `false`，旧适配器报 `Cannot activate Unity scene`。 | `UnitySceneBackend` 将已加载且已经活动的目标视为成功，仍校验无效或未加载句柄。复用调用继续取得同一场景句柄。 [复现与集成用例](host-transition.md) |
| 开发工具 | 合并切片首次导入发生 `CS0234`，失败目录 `artifacts/game-unity/Test/20261127-121823-cb9feb77` 只有导入日志，无法从统一 JSON 读取失败阶段。每次局部复现还要运行两批测试。 | 游戏验证脚本新增测试模式和夹具筛选，记录同步、导入、测试、审计或构建的失败阶段及报告路径。以不存在的夹具筛选复现零用例，命令失败且 `summary.json` 记录 `failedStep=PlayMode`、零用例和 XML 路径。证据为 `artifacts/game-unity/Test/20261127-123021-097cdff8/summary.json`。AgentKit 可直接调度游戏自己的 Unity 测试。 [验证接口](../../../../docs/reference/verification.md) |
| Test 接线 | 加载期退出、场景短暂重叠、拖拽失焦、暂停以及复用对象存在各自的状态与清理时机。 | 在 Test 内绑定所属场景的 UI、接入会话时钟和资源作用域、处理输入取消并隔离复用单位的出场身份。已有资源租约和时钟契约通过专项测试。 [资源](resource-leases.md) · [时钟](time-events.md) · [拖拽](drag-input.md) · [对象池](pool-performance.md) |
