# 2026-10-29：仓库组织与模块上下文验证

源码基线为 `e1fbb6b16d341624556d37c96fef0b9234e70871`，验证对象为其上的未提交工作区。此记录对应文档重组、模块清单和开发工具修改；游戏运行时源码没有改动。

## 实际执行

环境：Windows，.NET SDK **9.0.318**，Windows PowerShell **5.1.19041.6456**。

从 `E:/projects` 调用仓库脚本，以检查与当前工作目录无关的定位：

```powershell
powershell -NoProfile -File E:/projects/AwithGameFramework/tools/verify.ps1
```

| 检查 | 结果 |
| --- | --- |
| 文档链接 | 标准验证时 71 份文档、287 个本地目标通过；补充本记录后的最终检查为 **72 份文档、291 个本地目标**通过 |
| 通用边界、UPM/asmdef、Unity 元数据 | 通过，包含新增 module.json 的元数据与 GUID 唯一性 |
| 配置/协议生成及兼容基线 | 通过 |
| Release solution | 构建通过，0 警告、0 错误 |
| AgentKit contracts | 11 个模块清单、工具和任务模板契约通过 |
| 框架 NUnit | **638/638**，含新增 33 个模块清单/上下文用例与 3 个文档检查用例 |
| portable-feature 模板 NUnit | **13/13** |
| Headless | 退出码 0，包含 `FOUNDATION_CORE_PASSED` 和 `SHARED_SCENARIO_PASSED coins=10 frames=60 phase=Menu` |

完整日志：`artifacts/verify-repository-organization.log`；最终链接日志与结果摘要：`artifacts/RepositoryOrganization/docs-final.log`、`validation.json`。测试结果：`artifacts/TestResults/framework.trx`、`template.trx`。这些路径由后续运行覆盖，不应用来证明更早日期的测试。

## CLI 与上下文

从仓库外部通过 `tools/agent.ps1` 执行 `modules`、`modules --id gameplay`，成功发现 11 个模块。创建独立 docs-only 任务，显式加入 `readModules: ["gameplay"]`，再执行 `context`。

生成结果包含 Gameplay 描述文件、README、3 个声明入口及必要的根/祖先指令和显式 readPaths，共 14 个文件、86,670 字节（约 84.6 KiB）；没有隐式读取 Data/Core 的模块源码或 Gameplay 其他运行时源码。清单摘要进入上下文；这次操作没有登记人工确认或最终评审。

本地证据在 `artifacts/RepositoryOrganization/modules.json`、`gameplay.json`、`context-manifest.json`、`smoke.json`。上下文是收集时的快照，后续文档变化后须重新收集。自动回归另外验证清单、选择和入口漂移会使上下文及旧计划失效，未使用 readModules 的旧任务保留原摘要格式。

## 未执行范围

本次未启动 Unity/Godot、构建 Player 或导出程序，也未运行远端 CI。引擎运行时证据仍来自 2026-10-27，见 [最近验证](latest.md) 和 [历史记录](history/2026-10.md)。新增 module.json 是开发期元数据，标准检查验证其 Unity 导入元数据，但没有重新执行编辑器导入。

文档脚本检查 Markdown 行内链接和引用定义的本地文件/目录目标，不检查网络、标题锚点、HTML 或文字事实。机器检查不等于人工验收。
