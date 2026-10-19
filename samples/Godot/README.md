# Godot 共享流程示例

使用 Godot 4.5.2 **.NET** 打开 `project.godot`，先构建 C#，再运行。进入关卡、完成并保存，重新打开后应恢复金币和完成次数。UI 使用原生 Control，应用逻辑来自 `samples/Shared`。

配置由 `tools/generate-config.ps1` 生成，禁止手改 `Configuration/LevelConfig.json`。本机运行验收：

```powershell
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable'
powershell -File tools/verify-godot.ps1 -EditorPath 'Godot .NET console executable' -Export -TemplatePath 'windows_debug_x86_64.exe'
```

脚本编译真实 Godot SDK 项目，再在真实引擎中执行共享流程断言，检查暂停、隔离和存档重开；`-Export` 使用仓库内 Windows Desktop 预设，发布 C#、打包资源并运行独立 exe。日志与产物报告保留在 `artifacts/GodotValidation`。Windows x86_64 .NET debug 导出运行已实测，其他目标另行验证，详见根 `docs/validation.md`。
