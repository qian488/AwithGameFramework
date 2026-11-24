# 文档索引

按要完成的任务选择入口。验证记录保存实际执行证据。

| 任务 | 文档 |
| --- | --- |
| 了解产品形态、范围与框架调整依据 | [产品方向](product-direction.md) |
| 建立自己的游戏、接入客户端和启动服务端 | [游戏工作区入门](getting-started/game-workspace.md)、[games](../games/README.md) |
| 第一次运行，不安装引擎 | [Headless 入门](getting-started/headless.md) |
| 运行双引擎示例 | [Unity](../samples/Unity/README.md)、[Godot](../samples/Godot/README.md) |
| 查找模块、依赖和数据流 | [架构概览](architecture/overview.md)、[当前架构和数据流](architecture/current-system.md)、[通用运行时](../src/README.md)、[引擎适配](../adapters/README.md) |
| 新增或修改功能 | [开发指引](../GUIDE.md)、[开发步骤](guides/add-feature.md)、[模板](../templates/README.md) |
| 分离领域规则与应用效果 | [函数式核心](guides/functional-core.md) |
| 构建、分发并加载资源包 | [内容流水线](guides/content-pipeline.md) |
| 使用配置或协议生成器 | [ConfigGen](../tools/ConfigGen/README.md)、[Protocol](../samples/Protocol/README.md) |
| 在浏览器中编译、生成和测试 | [开发控制台](../tools/DevPanel/README.md) |
| 为人类与 AI 准备任务和上下文 | [AgentKit](../agent/README.md) |
| 进行 AI 协作、诊断和代码评审 | [AI 协作与代码评审](guides/ai-collaboration.md) |
| 编写和维护文档 | [写作约定](guides/documentation.md) |
| 执行检查，确认检查范围 | [验证参考](reference/verification.md) |
| 维护 Unity 程序化资产 | [程序化资产工作流](reference/unity-procedural-assets.md) |
| 查看实际验证过的版本、平台及往期记录 | [验证记录](validation/latest.md) |
| 了解能力与待补范围 | [路线图](roadmap.md) |

## 决策与资料

- [引擎无关运行时的架构决定](architecture/decisions/0001-engine-independent-runtime.md)保留背景与后果。
- [游戏工作区与外部程序集接入](architecture/decisions/0002-game-workspace-and-binary-integration.md)记录游戏、客户端、服务端和生成产物的边界。
- [仓库组织调研](research/repository-organization.md)记录参考项目、固定版本及采纳范围。

模块 API 和限制在各包 README 维护。新链接指向上表对应的文档。
