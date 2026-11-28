# 开发控制台

在仓库根目录运行下列命令。需要 .NET 8 SDK 或能构建 `net8.0` 的更新 SDK，以及仓库脚本所需的 PowerShell。

```powershell
dotnet run --project tools/DevPanel/AwithGameFrame.DevPanel.csproj
```

在浏览器打开 `http://127.0.0.1:5188`。工具宿主监听本机地址，终端中按 Ctrl+C 可关闭。

| 工作范围 | 操作 |
| --- | --- |
| 框架仓库 | 构建解决方案、生成或检查共享配置和协议、运行 .NET 测试、运行完整验证或静态检查 |
| 游戏工作区 | 构建外部 C# 和服务端、构建并同步客户端、生成或检查游戏配置和协议、单独运行 `tests` 下的 `*.Tests.csproj` |

用「工作范围」选择仓库或游戏。控制台从 `games/*/game.json` 发现游戏工作区，标出生成入口缺少的源文件。点击「生成」会更新对应产物，点击「检查」会校验当前产物。构建使用 Debug。完整验证包含 Headless 示例，可以在后台运行。

修改外部游戏代码后，选「构建并同步客户端」更新引擎工程使用的 DLL 和配置。该操作调用 `game.ps1 sync`。同步完成后，Unity 的导入和脚本编译结果在编辑器 Console 或单独的后台验证报告中查看。 [引擎验证命令](../../docs/reference/verification.md)

同一控制台一次运行一项操作。右侧显示实际命令、实时日志、退出码和耗时。刷新可恢复当前或最近一次操作，完整日志写入 `artifacts/DevPanel/`，关闭进程后仍可从磁盘读取。

工具列表和参数映射位于 [PanelCatalog.cs](PanelCatalog.cs)，构建、生成和验证由现有脚本或 .NET CLI 执行。脚本的参数和输出位置见 [开发工具](../README.md)。

选择游戏后点击「UI 原型」。控制台读取该游戏 `ui-prototypes` 下成对的 `<id>.json` 和 `<id>.profile.json`。可编辑控件属性或 JSON，切换状态样本预览，再保存源文件。另一编辑者先保存时，页面会提示刷新并合并修改。

启动控制台前将 `AWITH_UNITY_EDITOR` 设为对应版本的 Unity 可执行文件。「Unity 预检」会后台导入隔离项目，在页面显示节点、素材和失败原因。 [Test 试点与命令](../../games/Test/design/tournament-ui-pipeline/verification.md)
