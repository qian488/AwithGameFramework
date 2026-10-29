# ADR 0001：引擎无关运行时与独立适配层

状态：已采用。本文记录从旧 Unity 工程演进的决定；当前结构以[架构总览](../overview.md)为准。

## 背景

旧仓库以 Unity 工程为根，Core 同时依赖 MonoBehaviour、UnityAction、Resources、引擎时间与 Console，持久化接口返回 UniTask。即使业务逻辑不需要图形，也必须从引擎工程和插件中寻找依赖。当前维护目标是让人类与 AI 协作开发可持续维护的游戏，近期宿主以 Unity 和 Godot .NET 为主。

## 决定

在单仓库中分离 `src/` 通用 C# 运行时、`adapters/` 引擎实现、`samples/` 宿主示例、`tests/` 自动测试及 `tools/` 开发工具。Core 只依赖标准库；Data → Core，Gameplay → Data/Core，可选 Networking → Core。通用接口使用 BCL、普通数据与 Task，需要引擎类型的接口留在对应适配层。

每个通用包的 `Runtime/` 同时用于 SDK 项目和 UPM asmdef，以独立构建和静态依赖检查约束边界。保留旧公共命名空间及必要静态入口，通过显式迁移逐步采用实例宿主。Unity 现代接入、旧服务、DOTween、SQLite 使用独立包，避免基础接入被可选插件绑定。

宿主主动提供时间、日志、目录和帧更新。跨引擎首先复用兼容 C# 运行时中的逻辑与数据，不统一原生节点、Prefab、Control 或场景对象，也不承诺尚未实现的引擎与语言支持。

## 本地参考项目

最初参考本机 `E:/projects/trunk` 的下列位置。这是历史研究位置，不是构建所需目录，也没有引入该工程代码或运行时依赖。

| 参考位置 | 采用的做法 |
| --- | --- |
| `Program/Client/GameLogic/GameLogic.csproj` | 共享逻辑独立编译 |
| `Program/Client/GameApp` 与 `ClientPublish/Bin` | 引擎宿主与源码分开 |
| `Program/Client/GameLogicTest/TestMgr.cs` | 宿主主动驱动逻辑时间 |
| `Program/Share/Table` 与 `Program/Share/tools` | 共享数据、生成工具与宿主分开 |
| 根 `CLAUDE.md` | 按任务导航到目录和验证命令 |

参考工程中共享逻辑对服务端基础库的反向依赖、发布目录内 DLL、绝对机器路径和过期导航，不作为新框架规范。公开项目的后续对照见[仓库组织调研](../../research/repository-organization.md)。

## 结果与约束

通用逻辑可以在 .NET 测试和 Headless 中验证，同一源码再交由真实引擎编译运行。引擎适配仍需要独立测试、原生资源所有权和平台验收，普通 C# 测试不能代替它们。维护者需要同步包元数据、构建关系、迁移说明和入口文档；构建产物集中在 `artifacts/`，不以复制 DLL 维持源码依赖。

旧全局 API 与实例宿主仍可并存，但生命周期和状态相互独立。迁移需要明确选择实例 API，而非仅移动源码目录。旧目录、程序集和行为契约见[迁移指南](../../migrations/framework-modernization.md)。
