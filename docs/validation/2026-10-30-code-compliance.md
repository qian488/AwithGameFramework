# 2026-10-30：代码规范审查与修复

审查基线为 `5f14d1e0e18bf65256796d57f3ffb7cc4bbf3d7f`，开始时工作区干净。结果对应基线上的未提交修改。依据根协作约定、开发指引、局部 AGENTS/README 和函数式核心指南，逐文件核对依赖、线程、生命周期、取消、资源所有权、失败恢复及领域规则与应用效果的分工。

本轮已完成实现代码和构建清单审查，并修复确认的问题。按本轮要求，自动测试的专项审查与新回归用例留待后续测试 skill；已有用例仅用于核对契约。本记录不作为人工验收或修复后的行为回归通过声明。

## 范围

初始清单共 333 个文件。其中 `tests` 目录的 52 项留待测试专项；其余 281 项均完成代码阅读或生成来源核对。

| 区域 | 文件数 | 范围 |
| --- | ---: | --- |
| Core | 47 | 43 个 Runtime C# 与项目/包/模块/asmdef |
| Data、Networking | 44 | 35 个 Runtime C#、8 个清单及非 legacy 的 MySQL 文档示例 |
| Gameplay | 23 | 19 个 Runtime C# 与 4 个清单 |
| Unity | 95 | 75 个自有 C#、12 个 asmdef、8 个包/模块清单，含兼容包、可选插件适配和交互示例 |
| Godot | 20 | 17 个自有 C#、2 个项目和模块清单 |
| Shared、Headless、模板、协议产物 | 21 | 共享组合、控制台入口、功能模板及生成来源 |
| 工具与构建 | 31 | AgentKit、ConfigGen、ContentVerify、12 个脚本、根 props 与 CI |

另外核对了相关 README、任务模板、Unity/Godot 场景配置、solution 和忽略规则。第三方 Plugins/Packages 实现、legacy、引擎缓存、二进制资源和构建产物不列为自有源码审查；其程序集/包接线仍检查依赖与元数据。生成源码未手动修改，配置和协议使用检查模式验证。

现代运行时未发现引擎反向依赖，Networking 保持可选。设置与布局的纯规则继续使用显式输入输出；宿主、租约、原生对象和持久化编排按其状态与所有权建模。兼容层已有的公共命名空间和静态入口保留。

## 已确认并修复

| 位置 | 原问题与修复结果 |
| --- | --- |
| Core / TimeHelper | 时间戳返回 UTC 钟面却丢失 Kind，非 UTC 时区往返偏移；现在返回 UTC，统一相对时间计算。 |
| Core / Logging | 运行时级别、分类和格式未同步文件输出，轮转/保留配置被忽略；现同步设置、应用配置并保留重新启用目录。 |
| Data / DatabaseStorageProvider | 建表失败仍初始化成功，异步关闭被丢弃；现检查建表结果，实现异步释放，统一关闭与回滚等待驱动，关闭失败可显式重试。 |
| Data / JSON、Binary 文件存储 | 复合后缀枚举出错误键；现移除完整配置后缀，枚举键可直接用于读取和删除。 |
| Networking / ProtocolClient | 一个取消回调抛错会跳过其余请求；现尝试全部取消后聚合报告。 |
| Gameplay / SceneService | 未提交场景的回滚卸载失败后仍释放资源且失去 handle；现保留实例和 scope 至 Loaded，成功卸载后才归还资源。 |
| Shared / DemoApplication、DemoExperience | 保存成功后导航失败可能重复奖励，部分收尾不能重试；现记录提交及待收尾状态，保留重试并继续尝试资源清理。 |
| Unity / MathHelper、DOTweenProvider | 极大角度或无穷值可导致主线程循环，FadeTo 无目标导致 Kill 无效；现使用有限值检查/取模，fade 绑定对应 Transform。 |
| Unity / PlayerPrefsStorage | 基础类型读写不对称、重开丢失键管理范围、配置可变且错误时返回原文；现使用对称原生格式、持久键索引、配置快照和明确失败。 |
| Unity / Input、UI、Config、Pool、DebugConsole | 修复回调重绑破坏集合遍历、坏 prefab 遗留实例、Player 写编辑器目录、门面忽略接口实现及控制台通知/解析/关闭遗漏。 |
| Unity / 现有交互示例 | 修复静态订阅未解除、数据库连接未关闭、Binary 后缀不一致、回调空值、错误轮转说明和缓存删除走错存储；补齐自建 UI 清理。 |
| AgentKit、ConfigGen、内容工具 | 严格拒绝标识符末尾换行，识别大小写不同的残留生成文件，释放 monitor 解析文档，统一内容 HTTP 路径的平台比较规则。 |
| verify.ps1 | 补齐四包允许依赖矩阵、Core 标准库边界、Runtime 同源及 csproj/asmdef/UPM/module 一致性检查；增加独立 StaticOnly 入口。 |

场景资源所有权缺口和角度死循环属于优先修复项。Core/Data/Networking、Unity 修复和新增依赖检查做了交叉代码审阅；未发现新的阻塞性回归。公共行为及旧 PlayerPrefs 数据限制见[兼容性说明](../migrations/compliance-fixes.md)。

## 实际检查

环境为 Windows、.NET SDK 9.0.318、Windows PowerShell 5.1。结果如下：

| 命令或范围 | 结果 |
| --- | --- |
| `tools/verify.ps1 -StaticOnly` | 文档链接、便携源码/依赖、清单一致性、UPM 路径和 Unity 元数据通过；最终日志为 static-final.log。 |
| 12 个 PowerShell 脚本 | 全部通过语法解析。 |
| `dotnet build AwithGameFrame.sln --configuration Release --nologo` | 0 警告、0 错误；编译没有运行测试或 Headless。 |
| `dotnet build samples/Godot/AwithGameFrame.Godot.Sample.csproj --configuration Debug --nologo` | 真实 Godot.NET.Sdk 4.5.2 项目编译通过，0 警告、0 错误。 |
| `tools/generate-config.ps1 -Check`、`tools/generate-protocol.ps1 -Check` | 配置、主机副本、协议生成产物及兼容性基线通过。 |
| AgentKit `contracts` | `AGENT_CONTRACTS_PASSED`。 |
| `tools/verify-unity.ps1 -EditorPath <本机Unity.exe>` | 真实 Unity **2022.3.16f1** 隔离导入和程序集编译通过，包含最终交互示例修复。 |
| 最终差异 | `git diff --check` 通过，未改已有资源 GUID，未加入缓存或编译输出。 |

Unity 实际可执行文件为 `E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe`，最终隔离工程为 `artifacts/UnityValidation/82a771c4a03540cf943388f1e8f96317`。示例记录版本仍为 2022.3.40f1c1。Godot 本轮只编译 SDK 项目，没有启动编辑器或场景。

审查详细清单、分区报告及日志保存在本机忽略目录 `artifacts/ComplianceReview/`：`inventory.csv`、`core.md`、`data-networking.md`、`gameplay.md`、`unity.md`、`unity-sample-tests.md`、`godot.md`、`shared-templates.md`、`tooling.md`，以及 `build.log`、`godot-build.log`、`unity-build-final.log`、生成/契约/最终静态日志。其他机器需重新执行命令，不能假设这些本地产物存在。

## 验证限制

用户调整优先级前，干净基线执行过 638/638 NUnit；该结果只对应修复前的代码。之后没有新增或修改自动测试，也没有重新运行 NUnit。本轮没有执行完整默认 verify、Headless、Demo、PlayMode、Player、Godot 运行/导出或远端 CI；编译和静态阅读不能证明修复后的异常路径已通过行为回归。

PlayerPrefs 无公开全键枚举，旧未知键仍需项目通过已知 key 登记；索引不提供跨前缀物理键的自动迁移。其他既有边界包括 SQLite 方言、JsonUtility 支持范围、PCK 不可卸载、协作取消和共享示例无跨服务原子回滚。此次未扩大引擎或平台支持声明。后续测试专项可依据上表修复路径安排回归；真实运行证据仍须按版本与实际执行范围记录。
