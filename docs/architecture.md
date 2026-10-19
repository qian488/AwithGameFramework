# 架构决策：引擎之外的框架

## 问题与决定

旧仓库以 Unity 工程为根，Core 同时依赖 MonoBehaviour、UnityAction、Resources、游戏时间和 Console，持久化接口依赖 UniTask。即使业务逻辑不用图形，也需要从引擎工程寻找依赖。

现在采用单仓库中的普通 C# 包、引擎适配包与宿主示例：

- Core：标准库即可构建，包含事件、模块编排、服务注册、日志、计时和时间契约。
- Data：通用持久化及数据库/序列化契约，依赖 Core、Newtonsoft.Json。
- Gameplay：存档槽位和本地化，依赖 Core、Data。
- Unity：保留引擎类型的接口与实现、Inspector 配置、帧循环、Resources 和 PlayerPrefs。
- Headless：实际运行共享源码，提供无需图形界面的验证入口。
- Godot：独立 .NET Node 宿主和 Resource 后端，不复用 Unity 类型接口。
- Networking：可选的 BCL HTTP/协议模块，只依赖 Core，不引入业务 UI/凭证和重试策略。

兼容基线为 .NET Standard 2.1、C# 9。这里的跨引擎首先覆盖兼容 C# 运行时的宿主；未来接入其他语言需要单独设计桥接层。

## 源码与分发

每个通用包的 Runtime 源码同时被 SDK 项目和 UPM asmdef 编译。UPM 启用 noEngineReferences，.NET 项目没有引擎引用；独立构建和静态检查共同防止反向依赖。产物放根 artifacts，避免 Unity 将 obj 中生成的 C# 再次导入。

Unity 适配包保留三个程序集以减少原项目的引用改动，物理目录按 Engine、Services、Features 组织。程序集名与旧命名空间不代表通用性；是否可复用由项目依赖和代码中的类型决定。

## 宿主的责任

宿主提供日志输出、可写目录、时间源和帧更新。PerformanceMonitor 使用 Stopwatch 计时、接收宿主 delta 统计帧率；引擎渲染数据归具体适配层。

配置/存档模型使用普通数据。本地化管理器接收 LocalizationDataset 或 ILocalizationSource；UnityLocalizationSource 负责将 ScriptableObject 转成数据集。数据库驱动显式注册，通用 Data 不引用 Unity 工程中的 SQLite DLL。额外编解码器没有安装时明确失败。

Data 的 AdoDatabaseProvider 接收宿主提供的 ADO.NET 连接工厂，承载现有数据库操作；Unity SQLiteDatabaseProvider 只绑定 Mono.Data.Sqlite 连接，SQL 查询规则仍沿用 SQLite 约定。

原单例和静态 API 暂时保留，方便现有使用者迁移。新增模块应从显式依赖开始；此轮没有另造 DI 容器、ECS 或所有引擎统一的资源模型。

新增 `GameHost` 提供实例服务表、显式模块依赖、可取消异步启动与清理、暂停恢复、秒数 Tick 和主线程调度。`FlowMachine` 管理流程，Gameplay 的 NavigationStack 管理可恢复的导航历史。旧静态门面仍然全局共享，迁移必须显式选择实例 API，不将两者混用视为自动隔离。

资源共享契约只包含逻辑 key、请求类型、租约、作用域和真实释放回调。Unity 与 Godot 后端各自承认原生限制；并未统一节点/Prefab/场景对象。Store 本身在最后租约归还后释放，不宣称已有 LRU 预算缓存或远程更新系统。

配置/协议使用可审查 schema、确定性生成、强类型 DTO、运行时结构校验和兼容性基线。工具不依赖引擎。版本化存档将数据和元信息放同一校验封装，原子提交并保留上一版；与旧分离格式通过显式迁移衔接。

## Agent 协作

根 AGENTS.md 负责定位、依赖规则和验证命令，各领域规则和 README 描述局部接入。运行时、测试、示例、工具与历史资料分别放置。Cursor 规则只保留对这些入口的引用，避免多套约定逐渐冲突。

## 参考 trunk

借鉴本机 E:/projects/trunk 的具体模式：

| 参考位置 | 采用的做法 |
| --- | --- |
| Program/Client/GameLogic/GameLogic.csproj | 共享逻辑独立编译 |
| Program/Client/GameApp 与 ClientPublish/Bin | 引擎宿主和源码边界 |
| Program/Client/GameLogicTest/TestMgr.cs | 宿主主动驱动逻辑时间 |
| Program/Share/Table 与 Program/Share/tools | 工具/共享数据与宿主分开 |
| 根 CLAUDE.md | 按任务导航到目录与验证命令 |

未引入该参考工程的代码或运行时依赖。它的共享逻辑对服务端基础库的反向依赖、发布目录内 DLL、绝对机器路径以及过期导航，不作为新框架规范。
