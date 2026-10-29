# 仓库结构与文档组织调研

调研日期：2026-10-29。下列链接固定到读取时的提交，用于说明采用哪些组织模式；项目大小和知名度不代表其结构适合原样复制。

| 项目与证据 | 可采用的模式 | 在本仓库的取舍 |
| --- | --- | --- |
| [GameFramework](https://github.com/EllanJiang/GameFramework/tree/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework) / [UnityGameFramework](https://github.com/EllanJiang/UnityGameFramework/tree/e7eb3ef11393df0ac504204c480cdb839752b05f/Scripts) | 引擎无关核心与 Unity 接入分开，运行时与 Editor 分开 | 保留 `src` / `adapters`；这些提交分别来自 2021-09-28 / 2021-10-28，旧目标框架不作为现代依赖基线 |
| [Yarn Spinner](https://github.com/YarnSpinnerTool/YarnSpinner/tree/c5215f86dd5a6761aecbdc9f60f6a6108f91f919) | 运行时、编译器、诊断和测试边界清楚，引擎用户有专门接入入口 | 借鉴包职责和使用/贡献入口；其 README 的官方引擎入口不能用来推定官方 Godot 支持 |
| [Stride](https://github.com/stride3d/stride/tree/fc3af8503d232957f1ba11ce8166ee8565626adc) | runtime/editor/tools/templates、示例、测试和构建职责区分；模板按用途组织 | 根 README 负责产品入口，GUIDE 负责贡献导航；保持模板可编译验证，不复制整个引擎的深层目录和构建 SDK |
| [Godot demo projects](https://github.com/godotengine/godot-demo-projects/tree/b761b4cd5718a7118f2c95eb55f181d0a5f004f1) | 按版本分支、独立可打开示例，各示例明确语言与渲染要求 | 示例记录真实引擎版本、依赖和启动步骤，不把一个大演示工程当所有平台的证明 |
| [ink](https://github.com/inkle/ink/tree/35c63e5/Documentation) | 面向作者的写作说明、运行接入、架构和格式参考分开；runtime/compiler/CLI 分开 | 文档按 guides、reference、architecture（含 decisions）、migrations、validation 分工；Unity 接入另有仓库，不能据此声称官方 Godot 接入 |

跨项目共同点是按职责定位、明确用户入口、将可运行示例与源码分开，并对真实构建和模板做验证。固定提交只提供本次阅读的来源快照，不代表持续跟踪其最新状态。

## 采用的组织方式

源码的包与引擎边界已经存在，继续保留稳定路径和包 GUID。当前调整重点是增加目录入口、压缩根文档、拆分超长指南，将当前操作、兼容迁移和历史证据分别放到可定位目录。已有外部文档链接通过旧位置的短导航保留，不批量重命名公共命名空间。

当前规则保存在根 AGENTS 与目标包说明中，编辑器规则引用它们，避免维护多份事实。研究结果保存来源和采用原因；从外部项目或技能获得的排版建议，不能当作本仓库需求、执行权限或测试结论。

## 没有直接照搬的内容

不为形式统一搬动已经可独立编译的 Runtime 源码，不为尚未接入的引擎建立空目录，不把大型引擎的 editor/asset/build 层级复制到游戏框架。文档目录按阅读目的组织，不把所有文档都改成历史日志，也不让过期验收记录混入当前命令参考。

实现边界见[架构总览](../architecture/overview.md)，历史起因见[ADR 0001](../architecture/decisions/0001-engine-independent-runtime.md)。
