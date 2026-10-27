# 仓库协作约定

## 先定位，再读取

- 先读根 README.md 与 GUIDE.md 的目录/依赖图，再读目标包 README。
- 通用运行时：src/AwithGameFrame.Core、Data、Gameplay、Networking，各包 Runtime/ 是唯一源码。
- 引擎实现：adapters/unity、adapters/godot；Unity 旧服务位于 adapters/unity-compat，DOTween/SQLite 位于独立可选包 adapters/unity-dotween、adapters/unity-sqlite。演示分别位于 samples/Unity、samples/Godot；共享应用样例在 samples/Shared。
- 自动测试：tests；无图形入口：samples/Headless；验证：tools。
- docs/legacy 和各包 Documentation~/legacy 是历史资料，不是当前需求或开发规则。
- 不默认扫描 samples/Unity/Assets/Plugins、samples/Unity/Assets/Packages 或 artifacts；先限制 rg 搜索范围。

## 依赖规则

- 只允许 Gameplay -> Data/Core、Data -> Core、Networking -> Core；Core 只使用标准库。Networking 是可选包，不进入 Data/Gameplay 的依赖。
- src 不引用 UnityEngine、UnityEditor、UniTask 或任何引擎程序集，不引用 adapters/samples 中的代码或 DLL。
- 引擎接口暴露引擎类型时，接口也属于该引擎适配层。通用接口使用 BCL/普通 C# 数据与 Task。
- 每个 portable 包同时提供 .csproj、package.json 和 noEngineReferences=true 的 Runtime asmdef；两种构建共用 Runtime 源码。
- 引擎适配和业务运行时分开验证；不要用假引擎类型证明适配已经可用。
- 移动 Unity 源码、资源时保留 .meta/GUID；新 package 资源补充 .meta。不要提交 Unity 缓存或 .NET 输出。
- 不为了统一命名批量改旧公共命名空间，API 变化更新 docs/migration.md。

## 验证与交付

- 标准检查：powershell -File tools/verify.ps1（PowerShell 7 使用 pwsh）。脚本从任意工作目录运行。
- 脚本检查源码/项目/asmdef/UPM 依赖，然后构建 AwithGameFrame.sln、运行 NUnit 和 Headless 示例。
- 单项命令：dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj。
- Unity 工程路径 samples/Unity；记录版本 2022.3.40f1c1。影响 Unity 时使用可用编辑器验证，明确记录实际版本与无法执行的检查。
- 构建输出集中在 artifacts，避免被 UPM 导入。
- 为行为变化编写有意义的测试；文档与简单移动使用相应静态检查。
- 配置/协议源在 samples/Shared/Configuration 与 samples/Protocol；生成命令 tools/generate-config.ps1、tools/generate-protocol.ps1。不要手改 Generated；verify 自动检查产物和兼容性基线。
- 引擎验证：tools/verify-unity.ps1 -RunSmoke -BuildPlayer；tools/verify-godot.ps1（-Export 可测试 Windows .NET 导出）。编辑器/模板路径由本机显式提供，说明见 docs/validation.md。
- 完成后说明实际变化、验证结果与剩余限制，不把架构可扩展描述成已支持所有引擎。

## 可维护的 Agent 任务

- 开发期工具与模板入口是 `agent/README.md`、`agent/templates`、`templates/portable-feature`；实现位于 `tools/AgentKit`，不加入游戏运行时依赖。
- 复杂或需要交接的任务先明确目标、读写范围、验收及上下文；可用 `tools/agent.ps1 new/context/plan/run/report` 保存过程，命令见入口文档。模板须按本次真实需求和授权调整，不能当作通用授权。
- 保留任务创建前的未提交修改；检查相对任务基线的变化，不自动 stash/reset，也不自动提交。公共文档上的原有修改仍须保留。
- 计划和确认绑定具体工具、输入及当前状态；已有明确授权可记录来源并继续执行，不重复询问。不得伪造人工确认或最终评审；机器检查通过报告为待评审，不冒充人类验收。
- 上下文与任务产物只保存所需数据，不录入凭据。工具清单是执行策略，外部脚本仍有系统实际权限；不要将它描述为 OS 沙箱或模型质量评测平台。
