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
```

复制到独立验证工程，不改变示例记录版本。依次执行导入编译、真实 PlayMode 适配器断言、共享宿主流程、Windows x86_64 Mono Player 构建和运行；运行必须输出成功标记并以 0 退出。`Build-report.json` 保存实际版本、源码 commit、工作区修改标志和产物 SHA-256。编辑器与平台构建模块需在本机可用。

当前实测编辑器为 **2022.3.16f1**；示例记录版本仍为 2022.3.40f1c1。无图形模式可能报告现有 Sprite shader 不支持；验收覆盖生命周期/资源/存档和执行路径，不代替画面、听感、移动设备、IL2CPP 或 WebGL 验证。

## Godot .NET

```powershell
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable'
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export -TemplatePath 'windows_debug_x86_64.exe'
```

基线 **Godot 4.5.2 .NET** / .NET 8。默认编译真实 Godot SDK 并运行原生资源租约与共享流程断言；`-Export` 使用 Windows Desktop 预设发布 C#、打包资源，再启动独立 exe 验收。模板参数可省略以使用本机已安装的对应版本模板。自定义模板只在导出期间写入预设，finally 恢复。脚本自动生成引擎导出所需的本地 `.sln`（忽略提交）。

编辑器/模板从 Godot 官方 release 获取。Windows x86_64 .NET debug 导出与运行已实测；日志与产物报告位于 `artifacts/GodotValidation`。其他导出目标需独立验证。

CI 包含通用运行时与固定版本 Godot Linux 编译/运行任务，Godot 下载检查官方 SHA-512。新增 CI 配置的远端运行结果须以实际流水线为准；本地 Windows 验收不能等同 Linux CI 已通过。Unity CI 仍需要编辑器/许可证环境，当前通过本机脚本验收。

## 本轮验收记录

2026-10-19 在提交本轮代码前，使用本机最终运行时代码完成下列检查。报告记录当时的基线 commit `9030b16` 和 `workingTreeModified: true`。

| 检查 | 结果 | 本机记录 |
| --- | --- | --- |
| 架构、生成、Release 构建、NUnit、Headless | 231/231 测试通过，共享流程通过 | `artifacts/verify-framework-expansion.log` |
| Unity 2022.3.16f1 | 编译、29 项真实对象断言、共享 PlayMode、Windows Mono Player 构建/运行通过 | `artifacts/UnityValidation/ab39b9210c064124a31592a8bc0327df` |
| Godot 4.5.2 .NET | SDK 编译、原生宿主、Windows debug 导出/独立运行通过 | `artifacts/GodotValidation/6b909ddf15f7439e8bc353ef804c0c8b` |

共享断言覆盖配置读取、导航到关卡、固定 60 帧、暂停不推进、完成存档、宿主状态隔离、停止后重建恢复。运行日志包含 `SHARED_SCENARIO_PASSED`；引擎另有 `UNITY_SHARED_HOST_PASSED` / `GODOT_HOST_PASSED`。日志路径是忽略提交的本机证据，其他机器应重新执行脚本。

## 尚未声明完成的能力

资源租约不等于 LRU/内存预算/下载更新系统；HTTP 会话隔离不等于长连接、推送补查或业务幂等；配置结构 hash 与存档 checksum 不提供加密/防篡改安全保证。商用 SDK、联网业务、云存档、跨进程文件事务和各目标设备按照产品需求补充。
