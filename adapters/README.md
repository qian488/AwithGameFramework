# 引擎适配

框架的引擎适配实现公共契约在具体引擎上的行为。例如 `GameHostBehaviour` 用 Unity 的生命周期驱动 `GameHost`，`UnitySceneBackend` 用 Unity 原生 API 完成场景加载和卸载。项目在自己的客户端入口选择后端、配置服务和注册模块。 [通用运行时](../src/README.md)

## 基础引擎包

| 引擎 | 包与目录 | 职责 |
| --- | --- | --- |
| Unity | [unity](unity/README.md) / `com.awithgameframe.unity` | 实例宿主、Resources/AssetBundle、视图、场景、输入、音频及通用 Unity 编辑器工具 |
| Godot .NET | [godot](godot/README.md) | .NET 宿主、资源/PCK、Control、场景、输入和音频 |

## Unity 项目可选集成

下面是当前目录。项目选择安装所需的包和 SDK，并在本项目配置 Catalog、数据库或发布流程。

| 当前包目录 | 职责 | 使用前提 |
| --- | --- | --- |
| [unity-addressables](unity-addressables/README.md) | 将 Addressables 资源与场景接到公共加载契约 | Addressables 1.21.21 |
| [unity-hybridclr](unity-hybridclr/README.md) | 生成和加载热更新程序集与 AOT 元数据 | Unity IL2CPP、HybridCLR 8.12.0 |
| [unity-sqlite](unity-sqlite/README.md) | 为 Data 注册 SQLite 连接工厂 | Mono.Data.Sqlite 与目标平台原生库 |

[Unity 共享示例](../samples/Unity/README.md) 验证实例宿主、原生后端和 SQLite。真实引擎测试见 [验证命令](../docs/reference/verification.md)与[最近验证](../docs/validation/latest.md)。

## 已有工程接入

已有游戏的启动入口负责选择服务。游戏专属的类型转换、旧存档导入、场景和加载流程、对象所有权交接放在该游戏的工作区。涉及 Unity 对象的衔接脚本放在 `games/<Name>/client/Unity/Assets`，可独立编译的规则和应用编排放在 `games/<Name>/src`。按所需能力选包和替换的路径见[已有 Unity 工程渐进接入](../docs/getting-started/game-workspace.md#已有-unity-工程渐进接入)。
