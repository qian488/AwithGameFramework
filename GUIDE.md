# 开发指引

## 开发顺序

1. 先读根 `AGENTS.md`，按任务找到对应模块 README；历史资料按需读取。
2. 通用逻辑放 `src`。只有实际调用引擎 API、需要引擎类型或引擎生命周期的内容放适配层。
3. 外部依赖通过显式配置、接口或宿主注册接入。通用 API 使用 BCL 类型和 `Task`，不返回 Unity 对象或 UniTask。
4. 先在 .NET 测试或无图形宿主验证行为，再验证相应引擎的适配。
5. 修改边界或公共接口时，同步更新模块 README、迁移指南和受影响的验证。

## 依赖方向

```mermaid
graph TD
    UnitySample[samples/Unity] --> Unity[adapters/unity]
    Unity --> Gameplay[src/Gameplay]
    Unity --> Data[src/Data]
    Unity --> Core[src/Core]
    Headless[samples/Headless] --> Gameplay
    Headless --> Data
    Headless --> Core
    Gameplay --> Data
    Gameplay --> Core
    Data --> Core
    Shared[samples/Shared] --> Gameplay
    Shared --> Data
    Shared --> Core
    Headless --> Shared
    UnitySample --> Shared
    GodotSample[samples/Godot] --> Godot[adapters/godot]
    GodotSample --> Shared
    Godot --> Core
    Networking[src/Networking 可选] --> Core
```

- Core 只依赖标准库，不包含任何引擎分支。
- Data 使用 Newtonsoft.Json；数据库驱动和额外二进制编解码器由宿主注册。
- Gameplay 接收普通 C# 数据。Unity 的 Resources、ScriptableObject 与 PlayerPrefs 位于适配包。
- Networking 使用 BCL HTTP 和可替换 codec；不包含登录 UI、业务凭证管理、隐式重试或服务端代码。
- 适配包可以引用通用模块；通用模块不能反向引用适配包、示例或其中的第三方 DLL。
- `Runtime` 是 .NET 与 UPM 共同使用的唯一源码位置；构建输出统一进入根 `artifacts`，避免 Unity 导入编译产物。

## 模块与扩展

| 任务 | 修改入口 | 验证方式 |
| --- | --- | --- |
| 生命周期、事件、服务注册 | `src/AwithGameFrame.Core/Runtime` | NUnit 测试 + 无图形示例 |
| 文件存取、序列化、数据库契约 | `src/AwithGameFrame.Data/Runtime` | 临时目录存取与重开测试 |
| 语言切换、存档槽位 | `src/AwithGameFrame.Gameplay/Runtime` | 普通数据集与存档恢复测试 |
| 版本化存档、迁移、导航 | `src/AwithGameFrame.Gameplay/Runtime` | 迁移/故障恢复/取消与共享流程测试 |
| 网络、超时取消、会话边界 | `src/AwithGameFrame.Networking/Runtime` | 注入传输处理器的网络行为测试 |
| Godot 宿主/资源 | `adapters/godot`、`samples/Godot` | 真实 Godot .NET 编译/运行/导出 |
| 配置/协议生成 | `tools/ConfigGen` 与各样例 schema | 生成器行为测试、`-Check` 与兼容性基线 |
| Unity UI/资源/音频/输入/场景 | `adapters/unity/Runtime` | .NET 边界检查 + Unity 编译/演示场景 |
| 接入新 C# 引擎 | 新建 `adapters/<engine>` | 引用通用项目，实现时间、日志、数据源与宿主入口 |
| 修改文档/目录 | 对应 README、`docs`、`AGENTS.md` | 检查链接及 `tools/verify.ps1` |

新模块有真实需求再创建独立项目；不为未接入的引擎建立空壳，不预设所有引擎都具备相同的 UI、音频或资源对象模型。

## 宿主接入

新项目优先使用可创建实例的 `GameHost`、`GameModule` 与 `GameContext`。模块通过稳定 ID 和 Dependencies 显式声明依赖，异步启动失败会回滚，停止会取消会话并逆序清理。每个会话创建新宿主；旧静态 Bootstrap/ServiceLocator 继续服务兼容代码。

Unity 继承 `GameHostBehaviour`，Godot 继承 `GameHostNode` 并覆写 `ConfigureHost`。宿主在创建线程驱动 `Tick(deltaSeconds)`，启动、暂停和异步关闭期间也要持续泵调度队列。普通 .NET 工具需提供单线程循环/同步上下文，参考 `DemoScenario.RunBlocking`。

窗口、场景或流程可以持有 `ResourceScope`：`ResourceStore` 合并同 key/type 加载，所有租约归还后释放。传入宿主 Dispatcher 确保原生加载/释放发生在引擎线程。先清理业务作用域，最后停止资源模块和宿主；删除引擎对象前应显式等待 `ShutdownAsync`。

`GameHost.Lifecycle` 输出实例 ID、模块/操作、耗时、错误及回滚标记，可接自有结构化日志/指标后端；监听失败不改变生命周期行为。

以下为保留的静态入口接入约定：

宿主设置 `LoggingHost.Output`、`LoggingHost.DataPath` 和 `TimeHelper.TimeSource`，驱动 `PerformanceMonitor.Tick(delta)`。引擎专用的加载逻辑通过适配器转换成普通数据；例如 `ILocalizationSource` 返回 `LocalizationDataset`。

存储路径由 `DataPersistenceConfig` 明确指定。Unity 使用 `UnityDataPersistence.CreateConfig()` 或 `UseUnityPaths(config)`，再调用 `UnityDataPersistence.InitializeAsync(...)`。普通 .NET 宿主直接初始化 `DataPersistenceAPI`。需要重新配置或结束会话时，停止业务继续发起存储请求，再 `await ShutdownAsync()`；它会等待经统一 API 发起的在途操作。直接调用提供者的操作由宿主自行等待。

当前保留旧命名空间与多数静态入口，便于迁移。新增业务优先使用可构造实例和显式依赖，不扩大全局单例。模块编排现有顺序保持不变，初始化高优先级在前，关闭逆序。

模块启动失败时会逆序回滚已经开始初始化的模块；`Shutdown()` 应能处理部分初始化状态。正常关闭会尝试清理所有已启动模块，再集中报告异常。注册模块应在启动前完成，生命周期转换期间不允许重入启动或新增注册。

## 当前范围与后续工作

当前提供共享宿主与流程、Unity/Godot/Headless 样例、配置与 HTTP 协议生成、版本化存档、资源作用域和可选网络基础。远程资源发布、长连接恢复/推送补查、平台 SDK、热更新、完整 ORM、云存储等仍按实际需求推进；历史调研不代表能力已实现。每个引擎/平台通过实际编译与运行验收后再扩大支持声明。

架构取舍见 [架构决策](docs/architecture.md)，旧 API 与目录变化见 [迁移说明](docs/migration.md)。
