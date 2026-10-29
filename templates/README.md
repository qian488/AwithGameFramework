# 功能模板

模板提供可复制、可构建的最小结构，不作为游戏运行时依赖。按真实功能范围选择模板，复制后维护新包的名称、依赖、GUID 和验收条件。

| 模板 | 适用场景 | 边界 |
| --- | --- | --- |
| [portable-feature](portable-feature/README.md) | 用纯规则计算状态，由应用服务提交状态并通知显式效果 | .NET Standard 2.1 / C# 9；无引擎依赖，不预设玩法或全局模块 |

复制、改名、重建 `.meta` GUID、构建和测试步骤以模板 README 为准。移动现有资源须保留 GUID；复制模板创建新包须生成新 GUID，两者不同。

任务、评审和交接模板在 [agent](../agent/README.md)，不与功能源码模板混用。分层取舍见 [函数式核心指南](../docs/guides/functional-core.md)，新增包的工作步骤见 [开发指南](../docs/guides/add-feature.md)。
