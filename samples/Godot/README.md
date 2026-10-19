# Godot 共享流程示例

使用 Godot 4.5.2 **.NET** 打开 `project.godot`，先构建 C#，再运行。进入关卡、完成并保存，重新打开后应恢复金币和完成次数。UI 使用原生 Control，应用逻辑来自 `samples/Shared`。

默认入口组合原生菜单、关卡场景、循环音频与模态设置。空格执行关卡动作，Esc 开关设置；语言、音量、静音与空格/回车映射保存后会在新会话恢复。`DemoGodotView` 使用显式绑定与本地化订阅，关闭前解绑；实际应用逻辑与 Unity 使用同一个 `DemoExperience`。资源来自 `Resources/Demo*.tscn`、`DemoTone.wav`，缺失会报告加载失败。验收执行同一 `DemoExperienceScenario` 并检查 `COMPOSED_APPLICATION_PASSED`。

配置由 `tools/generate-config.ps1` 生成，禁止手改 `Configuration/LevelConfig.json`。本机运行验收：

```powershell
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable'
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export -TemplatePath 'windows_debug_x86_64.exe'
```

脚本编译真实 Godot SDK 项目，再在真实引擎中执行共享流程断言，检查暂停、隔离和存档重开；`-Export` 使用仓库内 Windows Desktop 预设，发布 C#、打包资源并运行独立 exe。日志与产物报告保留在 `artifacts/GodotValidation`。Windows x86_64 .NET debug 导出运行已实测，其他目标另行验证，详见根 `docs/validation.md`。
