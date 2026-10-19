# 验证入口与支持边界

## 通用运行时

```powershell
powershell -File tools/verify.ps1
```

检查引擎依赖、asmdef 图、UPM 路径、GUID/metadata、配置/协议生成结果，构建 Release、运行 NUnit 和 Headless 共享流程。构建与日志位于 `artifacts`，不进入发布源码。

配置源改变后运行 `tools/generate-config.ps1`；协议改变后运行 `tools/generate-protocol.ps1`。标准验收只检查产物，禁止 CI 悄悄改写源码。生成文件固定 LF，避免 Windows checkout 的换行差异。

## Unity

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath 'Unity.exe' -RunSmoke -BuildPlayer
powershell -File tools/verify-unity.ps1 -EditorPath 'Unity.exe' -BuildPlayer -ScriptingBackend IL2CPP
```

复制到独立验证工程，不改变示例记录版本。依次执行导入编译、AssetBundle 构建/发布/HTTP 安装、真实 PlayMode 断言、共享完整应用、Windows x86_64 Player 构建和运行；默认 Mono，可用 `-ScriptingBackend IL2CPP` 验证 AOT 路径。编辑器和 Player 都使用安装快照中的资源目录。运行必须输出全部成功标记并以 0 退出。`Build-report.json` 保存实际版本、后端、运行验证标志、源码 commit、工作区修改标志和产物 SHA-256。编辑器、平台构建模块和对应原生工具链需在本机可用。

`-PlayerTarget Android` / `WebGL` 提供相应构建入口，WebGL 要求 IL2CPP；这些目标仅构建，报告 `runtimeVerified: false`，需要产品另行运行设备/浏览器验收。PlayMode 使用 Windows bundles，须与这些目标分开运行。未执行的目标不能记为已通过。

当前实测编辑器为 **2022.3.16f1**；示例记录版本仍为 2022.3.40f1c1。无图形模式可能报告现有 Sprite shader 不支持；断言检查真实对象和执行路径，不代替画面、听感、物理输入设备和移动前后台测试。Windows IL2CPP 样例运行只证明所执行代码的 AOT 路径，不代表任意用户 DTO/反射插件/联网协议均已验证。

## Godot .NET

```powershell
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable'
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export -TemplatePath 'windows_debug_x86_64.exe'
```

基线 **Godot 4.5.2 .NET** / .NET 8。默认编译真实 Godot SDK、导入资源、构建独立 PCK、发布并通过 HTTP 安装，再运行后台资源加载、Control/场景、逻辑按键、音频与完整共享应用断言；`-Export` 使用 Windows Desktop 预设发布 C#、打包资源，再启动独立 exe 验收。两种运行都从安装快照挂载 PCK，退出含 `ObjectDB instances leaked at exit` 即失败。模板参数可省略以使用本机已安装的对应版本模板。自定义模板只在导出期间写入预设，finally 恢复。脚本自动生成引擎导出所需的本地 `.sln`（忽略提交）。

无图形模式通过 `ParseInputEvent` / `FlushBufferedEvents` 验证逻辑按键缓存与真实后端，物理键布局及桌面焦点仍需实际窗口/设备。音频检查真实 AudioStreamPlayer 的状态、分组音量和释放，未检查听感。

编辑器/模板从 Godot 官方 release 获取。Windows x86_64 .NET debug 导出与运行已实测；日志与产物报告位于 `artifacts/GodotValidation`。其他导出目标需独立验证。

CI 包含通用运行时与固定版本 Godot Linux 编译/运行任务，Godot 下载检查官方 SHA-512。新增 CI 配置的远端运行结果须以实际流水线为准；本地 Windows 验收不能等同 Linux CI 已通过。Unity CI 仍需要编辑器/许可证环境，当前通过本机脚本验收。

## 前轮验收记录（不足以关闭目标）

2026-10-19 本轮在基线 commit `4090382` 上验证工作区代码，产物报告记录 `workingTreeModified: true`。下列日志均为忽略提交的本机证据；其他机器应重新运行脚本。

| 检查 | 结果 | 本机记录 |
| --- | --- | --- |
| 架构、生成、Release 构建、NUnit、Headless | 379/379 测试通过，0 编译警告/错误，共享流程通过 | `artifacts/verify-foundations-final.log` |
| Unity 2022.3.16f1 Mono | 编译、29 项旧适配器断言及新增基础断言、共享 PlayMode、Windows Player 构建/运行 | `artifacts/UnityValidation/065e67f00cbe48b58135987b84da8c41` |
| Unity 2022.3.16f1 IL2CPP | Windows x86_64 构建/运行，基础断言与共享流程 | 同目录 `Player-build-il2cpp.log`、`Player-runtime-il2cpp.log`、`Build-report-il2cpp.json` |
| Godot 4.5.2 .NET | SDK 编译、原生宿主、Windows debug 导出/独立运行，原生时间缩放隔离断言 | `artifacts/GodotValidation/3d91e01e60394fa6b48747734527548d` |
| 原始文件发布 | PowerShell 发布 Unicode/空文件，运行时解析清单并验证大小/hash | `artifacts/ContentPublishValidation/c66617819d77458ab89ba71bc6e95eae` |
| WebSocket | NUnit 中包含 6 项 localhost 真实连接，涵盖完整客户端心跳、补查及业务发送 | `tests/AwithGameFrame.Core.Tests/WebSocketTransportTests.cs` |

共享断言覆盖配置读取、导航到关卡、固定 60 帧、暂停不推进、存档/重建恢复；新增事件隔离/订阅清理、时间缩放/重复调度和池借还。双引擎另检查真实视图层级/模态/焦点/属性绑定与关闭、Single/Additive 场景所有权、输入失焦释放、音源分组音量/暂停/并发与资源清理。

成功日志必须包含 `FOUNDATION_CORE_PASSED`、`SHARED_SCENARIO_PASSED`、`COMPOSED_APPLICATION_PASSED`，以及对应引擎的 `*_PRESENTATION_SCENES_PASSED`、`*_INPUT_AUDIO_PASSED`、`UNITY_ASSET_BUNDLE_PASSED` / `GODOT_RESOURCE_PACK_PASSED` 和最终宿主标记 `UNITY_SHARED_HOST_PASSED` / `GODOT_HOST_PASSED`。Godot 额外要求 `GODOT_UNSCALED_CLOCK_PASSED`：暂设 `Engine.TimeScale = 0`，确认宿主未缩放时间仍推进。资源缓存、内容校验/断点续传/回退、异常清理、长连接重连/补查在通用测试中验证；没有使用实际 CDN 或业务服务器。

上一阶段的 231 项测试、初版双引擎记录保留在 `artifacts/verify-framework-expansion.log` 与旧报告目录，不作为本轮新增能力的验收依据。

前轮 Godot 普通/导出运行以 0 退出且行为断言通过，但出现过 `ObjectDB instances leaked at exit`；当时未定位，所以此前“完成”结论不足。下面记录本轮复现和修复，历史日志不作为最终退出验收依据。

Unity 最终验收复用已导入的隔离工程，完整执行脚本相同步骤；`artifacts/verify-unity-foundations-final.log`、`artifacts/verify-unity-il2cpp-final.log` 记录实际结果。`artifacts/unity-final-freeze-check.json` 确认 136 份通用与 Unity 运行时源码指纹前后一致、`changedSources: []`，两套 Player 均覆盖最后的资源取消/清理修复。

## 本轮修复与验收

2026-10-19 在同一基线 commit `4090382` 上继续验证未提交工作区。新增存储配置拒绝/旧格式兼容、暂停音频回收、UI 消费持键、实例本地化、设置迁移/保存竞态、复杂配置与组合应用回归；最终 Release **523/523** 通过，构建 0 警告/错误，Headless 共享流程通过。日志：`artifacts/verify-completion-accepted.log`。不支持选项的负例会输出预期拒绝日志，这些不属于编译警告或失败测试。

| 检查 | 结果 | 本机记录 |
| --- | --- | --- |
| Unity 2022.3.16f1 | 完整导入、适配器/组合应用 PlayMode、Windows IL2CPP Player 构建运行通过 | `artifacts/UnityValidation/4321555eb09347e69cb5dda837496841`，`artifacts/verify-unity-completion-il2cpp-final.log` |
| Unity Windows Mono | 复用同一隔离工程构建/运行通过，使用相同 HTTP 安装资源 | 同目录 `Player-build-mono.log`、`Player-runtime-mono.log`、`Build-report-mono.json` |
| Unity 内容链 | 原生分包、发布 9 文件、localhost HTTP 10 请求安装，Editor 与两种 Player 加载安装目录 | 同目录 `ContentInstall/Install-report.json`；bundle 场景/共享依赖/跨 store/最后释放断言通过 |
| Godot 4.5.2 .NET | SDK 构建、普通原生运行、Windows debug 导出/独立运行全部通过；最终两份运行日志均无 ERROR/WARNING/UID/ObjectDB 警告 | `artifacts/GodotValidation/ea31607c38194b04a7ea7b90b12b4d07`，`artifacts/verify-godot-completion-final2.log` |
| Godot 内容链 | 真实 import/export-pack、发布 2 文件、localhost HTTP 3 请求安装，两种运行从快照挂载并验证纹理/材质/场景/视图 | 同目录 `ContentInstall/Install-report.json`、`Runtime.log`、`Export-runtime.log` |
| PCK 版本隔离 | 固定源 UID 的两个版本连续挂载、纹理/材质依赖、重载旧版及普通 UID 字符串保持通过；二进制/UID-only 非法源预拒绝 | `artifacts/review/godot-uids/After-native.log`，`GODOT_UID_NAMESPACE_PASSED mounts=2` |
| 内容边界 | Unity 15 项原生构建规则、Godot 目录重复挂载/UID 冲突检查，HTTP 工具 7 类路径重叠预拒绝 | `artifacts/portable-core-validation`、`artifacts/review/content-report-fe45e9f4c5b845af81c5dd2474d3a8a9` |
| 复杂配置导表 | JSON/UTF-8 CSV 等价生成与 `--check`，生成 C# netstandard2.1 编译及强类型表实际读取通过 | `artifacts/ConfigValidation/config-complex-final.trx` 与 `tools/ConfigGen/Fixtures/Complex` |

Unity 完整脚本后在同一工程补跑 Mono，`artifacts/unity-completion-freeze-check.json` 确认 163 份通用、共享应用及 Unity 源码指纹没有变化，两套 Player 验证相同源码。样例记录版本未修改，实际执行编辑器为 2022.3.16f1。

Godot 退出诊断已取得明确复现：相同资源重复加载 100 次，旧原生 threaded 路径留下 193 个 `RefCounted`，同步路径没有；更换加载策略后该泄漏消失。音频的剩余 `AudioStreamPlaybackWAV/AudioStreamWAV` 来自 Stop 后 mixer 尚未归还对象，现通过异步 voice 释放等待真实引用归还，再释放资源。Windows 导出还复现了场景重开与 C# 脚本桥接销毁并发的问题；场景/脚本/未知容器改在引擎线程加载，只有原始纹理和音频进入后台。最小复现、连续 10 轮完整原生流程及 5 轮修复后诊断导出日志位于 `artifacts/GodotDiagnostics`。没有强制 GC 或固定延时消除警告。

最后重新执行完整 Godot 脚本，普通与导出包均包含全部必需标记、以 0 退出且没有运行警告。PCK 源 UID 冲突也已在构建阶段修复并以双版本实测；Unity/Godot 报告仍明确记录未提交工作区，不将上述本机结果扩大为其他设备、平台或远端 CI 已通过。

## 尚未声明完成的能力

资源预算仅覆盖空闲项的后端估算，活跃资产不因预算淘汰；AssetBundle/PCK 提供最小构建发布链路，Addressables、发布者认证和 CDN 另行接入。PCK 挂载属于进程，不支持卸载或替换同 namespace。长连接提供客户端与连续补查契约，实际服务端、业务幂等、游标持久化、TLS/代理和具体平台网络需要独立验收。内容 hash、配置结构 hash 与存档 checksum 检查一致性或损坏，不提供发布者认证。内置旧文件存储只支持 XOR 混淆/GZip，AES256/LZ4 主动请求会拒绝。商用 SDK、云存档、跨进程文件事务、触屏/物理设备与各目标设备按照产品需求补充。
