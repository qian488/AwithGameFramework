# 无图形宿主

运行 `dotnet run --project samples/Headless/AwithGameFrame.Headless.csproj`。

示例在普通 .NET 8 进程中使用同一份 Core、Data、Gameplay 源码，验证事件、JSON 存取、本地化和宿主时间驱动。不需要安装 Unity；临时存档在运行结束时清理。失败时返回非零退出码。
