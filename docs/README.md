# 文档索引

按要完成的任务选择入口。当前说明描述现有契约；迁移说明帮助升级；验证记录保存实际执行证据。

| 任务 | 文档 |
| --- | --- |
| 第一次运行，不安装引擎 | [Headless 入门](getting-started/headless.md) |
| 运行双引擎示例 | [Unity](../samples/Unity/README.md)、[Godot](../samples/Godot/README.md) |
| 查找模块和依赖 | [架构概览](architecture/overview.md)、[通用运行时](../src/README.md)、[引擎适配](../adapters/README.md) |
| 新增或修改功能 | [开发指引](../GUIDE.md)、[开发步骤](guides/add-feature.md)、[模板](../templates/README.md) |
| 分离领域规则与应用效果 | [函数式核心](guides/functional-core.md) |
| 构建、分发并加载资源包 | [内容流水线](guides/content-pipeline.md) |
| 使用配置或协议生成器 | [ConfigGen](../tools/ConfigGen/README.md)、[Protocol](../samples/Protocol/README.md) |
| 为人类与 AI 准备任务和上下文 | [AgentKit](../agent/README.md) |
| 编写和维护文档 | [写作约定](guides/documentation.md) |
| 从旧版升级 | [迁移指南](migrations/README.md) |
| 执行检查，确认检查范围 | [验证参考](reference/verification.md) |
| 查看实际验证过的版本与平台 | [最近验证](validation/latest.md)、[代码规范审查](validation/2026-10-30-code-compliance.md)、[仓库重组检查](validation/2026-10-29-repository-organization.md)、[2026-10 历史记录](validation/history/2026-10.md) |
| 了解能力与待补范围 | [路线图](roadmap.md) |

## 决策与资料

- [引擎无关运行时的架构决定](architecture/decisions/0001-engine-independent-runtime.md)保留背景与后果。
- [仓库组织调研](research/repository-organization.md)记录参考项目、固定版本及采纳范围。
- [legacy](legacy/README.md)保存旧 Unity 框架资料，不作为当前开发规则。

模块 API 和限制在各包 README 维护，根文档负责引导。旧 `architecture.md`、`migration.md`、`validation.md` 等地址保留导航，新链接应指向上表的具体文档。
