# 新增或修改功能

先读根 [README](../../README.md)、[GUIDE](../../GUIDE.md) 和 [AGENTS](../../AGENTS.md)，定位目标包 README，再读取相关源码。历史资料只用于解释旧行为，当前规则以入口和所在包为准。

## 选择修改位置

| 功能 | 入口 |
| --- | --- |
| 生命周期、服务注册、事件 | [Core](../../src/AwithGameFrame.Core/README.md) 的 `Runtime/Hosting`、`Events` |
| 游戏时间、定时器、普通对象池、资源租约 | Core 的 `Runtime/Timing`、`Pooling`、`Resources` |
| 文件、序列化、数据库、配置表 | [Data](../../src/AwithGameFrame.Data/README.md) |
| 视图/场景、导航、输入/音频、设置/本地化、存档 | [Gameplay](../../src/AwithGameFrame.Gameplay/README.md) 及对应 Runtime 子目录 |
| HTTP、协议、内容更新、WebSocket | 可选 [Networking](../../src/AwithGameFrame.Networking/README.md) |
| 引擎 API 或需要引擎类型的接口 | [Unity](../../adapters/unity/README.md) / [Godot](../../adapters/godot/README.md)；旧 Unity 服务见 [compat](../../adapters/unity-compat/README.md) |
| 跨宿主应用组合、引擎演示 | [samples](../../samples/README.md) 的 Shared / Headless / Unity / Godot |
| 配置/协议生成 | [ConfigGen](../../tools/ConfigGen/README.md)，源位于 `samples/Shared/Configuration`、`samples/Protocol` |
| 复用功能骨架 | [portable-feature](../../templates/portable-feature/README.md) |
| 开发期任务、工具和上下文 | [AgentKit](../../agent/README.md)，实现位于 `tools/AgentKit` |

通用 API 使用普通数据、BCL 类型与 Task。依赖只允许 Gameplay → Data/Core、Data → Core、Networking → Core；Core 只使用标准库。引擎适配可引用通用包，通用包不能反向引用适配、示例或插件 DLL。完整图见[架构总览](../architecture/overview.md)。

有实际独立分发或依赖需求时才新增项目。portable 包同时维护 `.csproj`、`package.json` 和 `noEngineReferences=true` asmdef，共用 `Runtime/` 唯一源码。复制模板后按模板说明重建包 GUID；移动 Unity 源码和资源保留原 `.meta/GUID`。

新模块同时维护根 `module.json`：使用唯一 ID，填写实际职责、源码目录、README/入口文件、仓库内模块依赖及验证工具 ID。路径使用仓库相对路径并留在模块内。运行 `tools/agent.ps1 modules --id <id>` 和 `tools/agent.ps1 contracts` 检查发现与契约；字段和上下文选择见 [AgentKit](../../agent/README.md)。

## 划分规则与效果

能独立计算的规则先定义输入、事实和结果，再由应用服务采集事实、校验、提交状态、调用设备与保存。具体约束和已有例子见[规则与应用编排](functional-core.md)。新增外部依赖通过构造参数、配置或宿主注册显式接入。

涉及宿主模块时，使用稳定 ID 和 Dependencies，启动前完成注册，部分启动也须能清理。停止顺序与依赖相反，必要的后台任务由 `StopAsync` 取消并等待。每个会话创建新宿主，创建线程驱动未缩放秒数，启动、暂停和关闭期间持续泵调度队列。生命周期冻结游戏时钟，不能在这些回调里等待游戏时间 Delay。

视图、场景与音频先销毁原生实例，再归还 scope，最后关闭资源模块。模块默认依赖、线程约束、输入/UI 路由和后端细节以各包 README 为准。删除引擎宿主对象前显式等待 `ShutdownAsync`，不依赖销毁回调完成异步清理。

## 验证与交付

1. 行为变化编写有意义的规则/服务测试；文档和简单移动执行相应静态检查。
2. 修改配置/协议源后运行 `tools/generate-config.ps1` / `tools/generate-protocol.ps1`，不手改 Generated；标准验证检查确定性产物及兼容性基线。
3. 运行 `powershell -File tools/verify.ps1`，先验证通用边界、Release 构建、NUnit 与 Headless。
4. 影响适配层时用真实引擎检查，记录实际版本、平台、构建与运行范围；命令见[验证参考](../reference/verification.md)。
5. 同步模块 README；公共 API、程序集或行为变化更新[迁移指南](../migrations/README.md)，实际执行结果与未执行项明确区分。

构建和任务产物进入 `artifacts/`，不提交 Unity 缓存或 .NET 输出。交付说明实际变化、验证结果和剩余限制。跨文件或需要交接的工作可使用 AgentKit 保存目标、读写范围、上下文、计划和报告，保留任务前已有修改；已有明确授权按来源继续，机器检查通过保留为待人工评审。
