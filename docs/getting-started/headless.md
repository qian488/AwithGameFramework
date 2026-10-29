# 首次运行共享流程

本教程在普通 .NET 进程中运行游戏框架，验证共享逻辑，不需要 Unity 或 Godot。

## 前提

安装 .NET 9 SDK 与 .NET 8 Runtime、Git，并取得完整仓库。SDK 构建工具，.NET 8 运行示例和测试；通用包本身以 .NET Standard 2.1 为基线。首次构建需要能够还原 NuGet 包。

以下命令从仓库根目录执行：

```powershell
dotnet --list-sdks
dotnet --list-runtimes
dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj
```

## 确认结果

程序会运行关卡 60 帧，验证暂停时帧数冻结，完成关卡后保存 10 金币，再重建宿主并读取进度。同时创建独立目录的宿主，检查两个会话的数据隔离。

成功输出包含：

```text
SHARED_SCENARIO_PASSED coins=10 frames=60 phase=Menu
```

程序成功时退出码为 0，失败会返回非零。临时存档和诊断数据在退出时清理，示例源码和更多断言见 [Headless 说明](../../samples/Headless/README.md)。

## 继续验证或接入引擎

安装 PowerShell 后运行完整检查：

```powershell
powershell -File tools/verify.ps1
# PowerShell 7 / Linux / CI: pwsh -File tools/verify.ps1
```

该命令不会启动引擎。原生界面和设备操作需运行 [Unity 示例](../../samples/Unity/README.md) 或 [Godot 示例](../../samples/Godot/README.md)，并使用 [引擎验证工具](../reference/verification.md)。

需要修改共享行为时，先读 [Shared](../../samples/Shared/README.md)；增加框架功能时，从 [开发指引](../../GUIDE.md) 定位通用模块或引擎适配。
