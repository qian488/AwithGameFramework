# 在框架内开发游戏

克隆完整仓库，在 `games/<游戏名>` 开发自己的游戏；框架代码、游戏的独立 C# 代码和引擎工程分别维护。以下命令从仓库根执行，Windows PowerShell 使用 `powershell`，PowerShell 7 可改成 `pwsh`。脚本根据自身位置定位仓库，也可从其他工作目录通过脚本绝对路径执行。

安装 .NET 9 SDK 和 .NET 8 Runtime。外部游戏代码使用 .NET Standard 2.1 / C# 9，服务端使用 .NET 8。客户端选择 Unity 2022.3 LTS 或 Godot **4.5.2 .NET**；引擎平台构建要求仍由对应编辑器决定。

## 创建外部代码骨架

```powershell
powershell -File tools/game.ps1 new -Name MyGame -Engine unity
# 使用 Godot 时将 unity 改为 godot
powershell -File tools/game.ps1 paths -Name MyGame
```

`MyGame` 是目录和程序集名称的一部分。名称限制为 1–40 个 ASCII 字符，以字母开头，后接字母、数字或下划线，不能使用 C# 关键字或 Windows 设备保留名。模块名称采用相同要求。命令不覆盖已有同名游戏。

```text
games/MyGame/
  game.json
  src/
    MyGame.Domain/           # 纯规则、普通数据，无引擎依赖
    MyGame.Application/      # 应用编排与 GameModule 组合入口
  server/
    MyGame.Server/           # .NET 服务端入口及进程配置
  configuration/            # 本游戏配置源
  protocol/                 # 本游戏协议源和兼容性基线
  client/                   # 开发者在此创建选定引擎工程
```

`game.json` 记录 `schemaVersion`、`name`、`engine`、`applicationProject` 和 `serverProject`，是命令定位本游戏的入口。默认一游戏一份克隆；同仓库多个游戏使用不同名称与独立目录。框架包不引用任何具体游戏。

领域代码通过显式输入和输出计算结果；应用层负责状态提交、外部效果与模块组合。空模板不预置战斗、背包或其他玩法。新增独立模块可执行：

```powershell
powershell -File tools/game.ps1 new-module -Name MyGame -Module MyFeature
```

命令创建 `src/MyGame.MyFeature` 空库及 Domain/Application 目录说明，自动将该项目加入主 Application 的项目引用。模块初始不带框架依赖，按实际职责再添加所需引用。模块名称不能与服务端、框架或已有依赖的程序集重名，例如 `MyGame.Server`。

模板的使用边界与依赖说明保存在生成的游戏 README、AGENTS 和项目文件中。不要把引擎表现或服务端专用依赖加入客户端和服务端共享的领域项目。

## 手动创建客户端工程

创建命令只准备外部代码和目录说明。由开发者使用编辑器创建真实工程：

| 引擎 | 创建目录 | 必要设置 |
| --- | --- | --- |
| Unity | `games/MyGame/client/Unity` | Unity 2022.3 LTS，API Compatibility Level 选择 .NET Standard 2.1 |
| Godot | `games/MyGame/client/Godot` | Godot 4.5.2 **.NET**，创建 C# 工程，目标框架 net8.0 |

避免编辑器在目标路径下再次创建一层同名目录。`paths` 命令显示本机完整路径。场景、Prefab、Control、资源、原生表现和必要接线放在客户端；Spine、TMP 等组件在该工程中自行安装。

## 构建外部代码

```powershell
powershell -File tools/game.ps1 build -Name MyGame -Configuration Debug
```

命令编译游戏 Application 及其项目/运行时依赖，同时编译服务端。便携 DLL 与 PDB 集中输出到 `artifacts/games/MyGame/Debug/portable`；Release 使用相应的 `Release` 目录。客户端和服务端共享代码通过项目引用形成依赖，输出不用手动复制进公共框架源码。

模板的项目引用指向当前克隆中的框架。新增依赖必须同时满足目标引擎的 API、AOT 和平台要求；可在 .NET 编译不代表已在所有客户端平台可用。

当前客户端分发支持平铺的托管 DLL/PDB 依赖。带 native runtimes、语言卫星资源或额外内容文件的依赖会在构建收集阶段被拒绝，需要显式的引擎接入方案。Core、Data、Gameplay 是现代适配器的构建基线；Networking 可以按实际需求移除。

## 将产物接入 Unity

关闭可能占用产物的运行会话，完成外部构建后执行：

```powershell
powershell -File tools/game.ps1 sync -Name MyGame -Configuration Debug
```

工具要求目标位置已有完整 Unity 工程，然后写入嵌入式 UPM 包 `Packages/com.awithgameframe.workspace`。其中包括游戏与框架 DLL/PDB、现代 Unity 适配源码及其元数据。Unity 原生适配仍由编辑器编译；生成的 asmdef 使用 DLL 引用，不再引用同名便携源码程序集。

该包依赖 Unity 官方 `com.unity.ugui` 和 `com.unity.nuget.newtonsoft-json`，不会再复制 NuGet 的 Newtonsoft.Json DLL。此接入方式应独立使用；不要同时安装便携框架源码包或原 Unity 适配包，避免同名程序集和类型重复。旧兼容、DOTween、SQLite 包未纳入该自动接入流程。

在 Unity 工程的 `Assets/Scripts/Bootstrap` 下添加接线程序集 `MyGame.Client.asmdef`；适配程序集关闭了自动引用，因此接线需要显式引用：

```json
{
  "name": "MyGame.Client",
  "references": ["AwithGameFrame.Unity"],
  "overrideReferences": true,
  "precompiledReferences": ["AwithGameFrame.Core.dll", "MyGame.Application.dll"]
}
```

在同目录添加最小接线脚本，将组件挂在启动场景的 GameObject 上：

```csharp
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Unity.Hosting;
using MyGame.Application;

public sealed class GameBootstrap : GameHostBehaviour
{
    protected override void ConfigureHost(GameHost host)
    {
        host.AddModule(new MyGameModule());
    }
}
```

需要其他模块时继续增加相应程序集引用。设备、资源、视图等后端按[Unity 适配说明](../../adapters/unity/README.md)注册。销毁宿主对象前等待 `ShutdownAsync`，不要绕过宿主异步清理。

## 将产物接入 Godot

外部构建后执行相同的 `sync` 命令。工具使用真实 Godot .NET SDK 编译适配项目，将游戏/框架/适配 DLL 与 PDB 放入 `client/Godot/Framework`，同时生成 `AwithGameFrame.Workspace.props`。

在 Godot 客户端根目录的 `.csproj` 中添加一次导入，放在 `</Project>` 前；之后同步无需重复修改项目文件：

```xml
<Import Project="Framework/AwithGameFrame.Workspace.props" />
```

导入文件为各程序集设置 `HintPath` 与 `Private=true`。不要同时为相同程序集添加框架或适配源码的 `ProjectReference`。在客户端创建一个 C# Node 脚本并挂到启动场景：

```csharp
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Godot;
using MyGame.Application;

public partial class GameBootstrap : GameHostNode
{
    protected override void ConfigureHost(GameHost host)
    {
        host.AddModule(new MyGameModule());
    }
}
```

重新构建 Godot 客户端。资源和原生后端按[Godot 适配说明](../../adapters/godot/README.md)接入；删除宿主 Node 或直接退出场景树前等待 `ShutdownAsync`。SDK 编译与 DLL 接入不等于完成目标设备导出验证。

## 更新、调试与服务端

修改外部代码后重复 `build`、`sync`，再让编辑器编译客户端。同步目录由工具维护，使用所有权记录识别生成物并清理过期文件；不要在生成目录中修改源码、添加手写文件或混放其他插件。客户端自己的接线与资源保留在生成目录之外。

Debug 同步包含 PDB，调试器可用当前仓库源码定位外部程序集。断点、符号加载、引擎热重载和平台发布仍需在实际客户端检查；程序集更新后必要时停止运行或重启编辑器。

```powershell
powershell -File tools/game.ps1 run-server -Name MyGame
# 有限时长启动，便于确认进程能正常启动和关闭
powershell -File tools/game.ps1 run-server -Name MyGame -RunForSeconds 3
# 使用本游戏自行维护的服务端配置文件
powershell -File tools/game.ps1 run-server -Name MyGame -ServerConfig games/MyGame/server/MyGame.Server/server.json
```

服务端默认读取程序输出目录中的 `server.json`。`-ServerConfig` 将路径传给进程的 `--config` 参数；直接启动服务端时可使用 `--run-for-seconds` 设置有限运行时长。

服务端骨架提供进程配置、日志、单线程宿主驱动和正常退出。主循环在 GameHost 创建线程泵送异步延续与 Dispatcher，停止时等待模块清理。它是独立进程入口；账号、战斗同步、数据库接入和生产部署按游戏需求实现，不预置业务接口。

`configuration` 和 `protocol` 当前只定义游戏源文件归属。已有 `generate-config.ps1`、`generate-protocol.ps1` 仍面向 `samples`；不会自动读取本游戏目录。需要生成时按[生成工具](../../tools/ConfigGen/README.md)配置游戏自己的输入、输出和兼容性基线，不手改 Generated 文件。

实际检查范围见[验证记录](../validation/latest.md)。客户端工程由开发者创建后，还需在选定编辑器中完成导入、启动、调试与目标平台发布验证；H5、UE5 不属于当前接入实现。
