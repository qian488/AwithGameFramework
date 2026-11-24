# 仓库协作约定

## 先定位，再读取

- 定位任务时，先查根 README 的入口和 GUIDE 的依赖图，再读目标模块 README。
- 调整框架结构、能力范围或开发流程时，以产品方向和路线图界定范围。用源码和实测确认当前能力。 [产品方向](docs/product-direction.md) · [路线图](docs/roadmap.md)
- 开发具体游戏时，从 `games/<Name>/game.json` 确认工作区，并读本游戏 README 和 AGENTS。`samples` 供共享示例和验证使用。
- 查找实现时，先把 `rg` 限定在目标源码目录。插件、生成文件和历史文档按任务需要查阅。

## 依赖规则

- 公共包只允许 `Gameplay -> Data/Core`、`Data -> Core`、`Networking -> Core`。Core 使用 BCL。`src` 保持引擎无关，依赖限定在通用层。
- 公共包的 `.csproj` 和 `noEngineReferences=true` 的 asmdef 共用 `Runtime` 源码，每包提供 `package.json`。通用接口使用普通 C# 数据与 `Task`。引擎类型及暴露它们的接口放在相应适配层。
- 游戏外部项目由 `.csproj` 将 `Runtime` 源码编译为 DLL。Domain 放纯规则，Application 组合领域与框架，客户端负责引擎接线，服务端负责进程基础设施。公共框架的依赖止于通用层。
- API 变更时更新模块当前用法，保留与本次变更无关的公共命名空间。核对旧入口的实际调用方和已有数据。只有实际工程或数据需要迁移时才写迁移步骤。已迁移的旧入口连同专用依赖和样例一起删除。
- 修改 Unity 资产或脚本时，随文件保留 `.meta` 和 GUID，并为新包资源补齐 `.meta`。

## 验证与交付

- 行为变更先验证受影响的入口，再运行标准检查 `powershell -File tools/verify.ps1`。文档和简单移动使用对应静态检查。 [验证命令](docs/reference/verification.md)
- 涉及引擎时，另用真实引擎验证原生行为。默认后台批处理，桌面验证按用户要求执行。记录实测版本和结果。待人工验收项附启动入口和检查项。 [实测记录](docs/validation/latest.md)
- 通过 `tools/game.ps1` 更新游戏客户端接入。每个程序集选择一个来源。`Packages/com.awithgameframe.workspace` 和 `Framework` 由同步命令管理。 [工作区构建与接入](docs/getting-started/game-workspace.md)
- 修改配置或协议时编辑源文件，再运行生成器。构建输出留在 `artifacts`，提交源码和应追踪的资源。 [生成与验证工具](tools/README.md)

## 可维护的 Agent 任务

- 澄清需求时先查仓库和对话。向维护者确认仍缺失且会改变结果的决定。新增地图、Boss、UI 等内容时，先确认玩家体验与验收路径。 [内容澄清](.agents/skills/awith-content-design/SKILL.md)
- 诊断故障时先从受影响的入口复现。 [诊断技能](.agents/skills/awith-diagnose/SKILL.md)
- 操作 Unity 时先定位工程和目标资产，再选择实时或后台命令。 [Unity 编辑器技能](.agents/skills/awith-unity-editor/SKILL.md)
- 任务复杂或需要交接时，记录目标、读写范围和验收。模块入口变更时同步更新 `module.json`。 [AgentKit](agent/README.md)
- 保留已有工作区修改，按授权决定是否提交。任务上下文只存所需数据，排除凭据。现有授权可继续使用，机器检查与人工评审分别记录。
