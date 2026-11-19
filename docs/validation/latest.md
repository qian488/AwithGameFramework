# 最近验证

## Test 纯公式与 Unity 资源接入（2026-11-19）

`Test.Domain` 的纯 .NET 公式测试 **2/2** 通过。Test Debug 外部程序集与服务端构建均为 0 警告、0 错误，DLL/PDB 同步成功。Unity **2022.3.16f1** 隔离工程的 EditMode **3/3**、PlayMode **2/2** 通过，覆盖公式资源、旧图层内嵌参数、独立播放和宿主重播。Unity 测试报告位于本机 `artifacts/TestFormulaBoundaryValidation`。

## Test 特效库与公式图层（2026-11-19）

在 Unity **2022.3.16f1** 的隔离工程中，EditMode **2/2**、PlayMode **2/2** 通过。覆盖 Prefab 实例接入、螺旋分布、抛射延迟和重力、独立实例启用后自动播放，以及原场景通过宿主首播与重播。Unity 测试报告位于本机 `artifacts/TestParticleLibraryValidation`。仓库标准验证通过，包括框架 **638/638**、模板 **13/13** 和 Headless 示例。

## Test 程序化粒子演示（2026-11-19）

Test Debug 外部程序集和服务端构建均为 0 警告、0 错误，DLL/PDB 同步成功。使用 Unity **2022.3.16f1** 在隔离工程运行 EditMode 与 PlayMode 测试，各通过 1 项。覆盖新增或停用图层后的预览重建、分层延迟、宿主首播与重播、关闭宿主后取消重播及临时对象清理。`tools/verify.ps1 -StaticOnly` 通过。

通过 Direct3D 11 读取场景相机渲染结果，确认粒子实际可见。Windows x64 Development Player 构建通过。日志、测试报告、预览图和 Player 位于本机 `artifacts/TestParticleValidation`。本次运行验收在编辑器内完成，独立 Player 运行以及调试器符号加载、源码断点待验证。

## Test Unity 实际工程接入：2026-11-03

将开发者加入的 Test 工程整理到 `games/Test/client/Unity`，补齐外部 C# 骨架与最小场景接线。Debug 外部构建为 0 警告、0 错误；Unity **2022.3.16f1** 实际导入及脚本编译通过，退出码 0，导入后再次同步通过。过程、日志诊断与范围见[本次记录](2026-11-03-test-unity-integration.md)。

本次没有进入 PlayMode，也未执行 Player 发布、断点调试或 Godot；编译结果不代表这些检查已经通过。

## 游戏工作区与外部构建：2026-11-02

基于 `03d56ce` 的未提交工作区，公共解决方案与隔离游戏骨架 Release 编译通过，均为 0 警告、0 错误；静态检查、工具契约、外部构建更新与失败保护、模块发现和服务端生命周期的直接检查通过。Unity 分发生成器检查和真实 Godot SDK 编译通过，范围与证据见[本次记录](2026-11-02-game-workspace.md)。

本轮没有创建实际 Unity/Godot 游戏工程，没有执行新分发方式的引擎导入/原生运行、调试或导出；静态客户端标记与占位输入只用于工具分支检查。没有运行 NUnit 或 Demo；现有引擎证据仍保留原日期与范围。

## 代码审查与编译：2026-10-30

在 `5f14d1e` 的未提交工作区完成自有实现与构建清单的规范审查，并修复确认的问题。.NET Release、真实 Godot SDK 项目编译均为 0 警告/错误，Unity 2022.3.16f1 隔离导入与编译通过；依赖/元数据、生成产物与 AgentKit 契约检查通过。范围、修复及本地证据见[审查记录](2026-10-30-code-compliance.md)。

本轮按要求将自动测试专项留待后续，没有为修复增加或重跑 NUnit，也未运行 Demo、PlayMode、Player 或 Godot 场景/导出。下方既有行为验收保留各自日期和源码范围。

## 仓库与开发工具：2026-10-29

在 `e1fbb6b` 上的未提交工作区运行标准验证：Release 构建 0 警告/错误，框架 **638/638**、模板 **13/13**，Headless、文档本地链接、依赖/元数据、生成产物及 11 个模块契约通过。另通过真实 CLI 检查模块发现和按模块生成上下文。环境、命令与本地证据见 [本次记录](2026-10-29-repository-organization.md)。

这次修改涉及仓库文档与开发工具，没有重新执行 Unity/Godot、Player 构建或导出。文档与模块路径检查通过不能替代引擎导入和运行。

## 引擎运行时证据：2026-10-27

以下归纳既有框架与引擎证据，不能当作 2026-10-29 工作区的新引擎验收。

当时在 `c94cc9f` 基线上验证未提交工作区，报告记录工作区已修改；相关代码随后提交为 `e1fbb6b`。报告中的源码状态以原报告为准，不将其改写成对后续提交直接执行的结果。完整叙述及此前记录见[2026-10 历史](history/2026-10.md)。

| 范围 | 2026-10-27 实际结果 | 本地证据路径 |
| --- | --- | --- |
| 标准 verify | 依赖、实例/兼容边界、包元数据、生成检查通过；Release 0 警告/错误，框架 602/602、模板 13/13，Headless 通过 | `artifacts/verify-framework-refinement-final.log`（通用 TRX 已被后续运行覆盖） |
| Unity 2022.3.16f1 最小现代工程 | 无插件导入与真实模块生命周期通过；无 Legacy/Compatibility/DOTween/SQLite 程序集 | `artifacts/UnityMinimalValidation/37aaf600c12e41a38cff36826140c2aa`、`artifacts/verify-unity-minimal-refinement.log` |
| Unity 完整样例 / Windows IL2CPP | 29 项旧适配断言、共享 PlayMode、AssetBundle/HTTP 安装、Player 构建运行及可选提供者/SQLite 原生操作通过 | `artifacts/UnityValidation/33faae0c317f4d63ab5b8ff929aae8e9`、`artifacts/verify-unity-refinement-final.log` |
| Godot 4.5.2 .NET / Windows debug | SDK 构建、PCK/HTTP 安装、原生与导出运行通过，含最终生命周期同步重入回归 | `artifacts/GodotValidation/451e2d83f6364b3eb17e50ecb74d77a5`、`artifacts/verify-godot-refinement-final.log` |
| 元数据 | 63 份移动资源的 GUID 保持唯一对应 | `artifacts/framework-refinement-metadata.json` |

Godot 两份最终运行日志包含 `GODOT_HOST_LIFECYCLE_PASSED`，没有 ERROR、UID 或 ObjectDB 退出泄漏；生命周期覆盖多宿主退出、原退出策略恢复、启动失败、启动期暂停与恢复、离树和同步重入关闭。

Unity 示例记录版本仍为 2022.3.40f1c1，实测为 2022.3.16f1。该次未另跑 Mono、Android、WebGL 或移动设备；Godot 未跑其他平台，远端 CI 也不由本地记录证明通过。2026-10-19 的 Mono 等记录保留在历史中，不扩展为 2026-10-27 工作区的同等证据。

日志与产物位于忽略提交的本机 `artifacts/`，其他机器需按[验证命令](../reference/verification.md)重新执行。后续真正执行新检查时，记录日期、源码状态、命令、实际版本、结果及未执行范围，再更新本页。
