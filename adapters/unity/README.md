# Unity 适配包

Unity 需要的代码集中在本包，通用能力来自 src 下的 Core、Data、Gameplay。

| 目录 | 职责 / 程序集 |
| --- | --- |
| Runtime/Engine | Mono、场景、Unity 类型契约、Inspector 配置、时间/日志接入；AwithGameFrame.Unity |
| Runtime/Services | 资源、UI、音频、输入、对象池、PlayerPrefs、SQLite 接入与旧 Foundation 门面；AwithGameFrame.Foundation |
| Runtime/Features | 本地化 ScriptableObject/Resources 数据源与 Unity 调试面板；AwithGameFrame.Extensions |

UnityPlatform 自动安装日志、目录和时间源，UnityFrameDriver 提供帧驱动。UnityDataPersistence.CreateConfig() 设置旧存档目录；InitializeAsync(config, enableDatabase: true) 可显式启用数据库存储。详细行为见根 docs/migration.md。

当前示例工程已安装 UniTask、DOTween 和 Mono.Data.Sqlite 插件，UPM 清单提供 Newtonsoft.Json、uGUI。将适配包安装进其他 Unity 工程时，也需要安装这些现有服务使用的插件；它们不属于通用包依赖。Unity 示例和运行时诊断演示位于 samples/Unity/Assets/Examples。

新增引擎实现放独立 adapters/<engine>，不要修改通用源码添加引擎条件编译分支。

## 实例宿主与资源

新应用继承 `AwithGameFrame.Unity.Hosting.GameHostBehaviour`，覆写 `ConfigureHost(GameHost)` 注册模块，在 `OnStartedAsync()` 启动应用操作。每个组件拥有独立 `GameHost`，使用秒数驱动 Tick；启动、暂停和停止时持续泵主线程调度。停止一个会话后创建新的宿主，旧静态 API 不会因此自动隔离。

切走宿主所属场景或销毁组件前先 `await ShutdownAsync()`。正常应用退出会等待清理；`OnDestroy` 只能尽力清理，不能替代显式等待。

`UnityResourceLoader` 可接到通用 `ResourceStore`，使用宿主 Dispatcher 执行原生加载与释放。请求按 key/type 合并，作用域归还全部租约后释放；Unity 的原生 Resources 请求不能取消。非 Prefab 资产释放调用 `UnloadAsset`，Prefab 由宿主选择 `UnloadUnusedAssets` 时回收。直接 `Resources.Load` 的外部引用不在所有权记录中，应统一加载入口。

共享应用入口为 `samples/Unity/Assets/SharedDemo.unity`。运行根 `tools/verify-unity.ps1 -EditorPath <Unity.exe> -RunSmoke -BuildPlayer` 验证适配器、共享 PlayMode 与 Windows Mono Player，详细命令及实测版本见 `docs/validation.md`。

## 生命周期与配置

`FrameworkEntry` 是旧静态模块的兼容入口。启动读取 `FrameworkConfig` 的音频、UI、输入、对象池开关；修改模块开关后需结束并重新启动会话。对象池关闭时，资源服务直接加载并销毁 GameObject。一个已启动的旧会话只由创建它的入口负责关闭。

`OnApplicationPause` 暂停输入与正在播放的音频，保留已启动模块、Canvas 和对象池；回到前台恢复暂停前的状态。主动禁用的输入和主动暂停的 BGM 不会被强制启用。最终关闭解除输入/音频的帧订阅，释放音频引用、UI 根节点及对象池拥有的对象。

`UnityFrameDriver` 驱动 `ConfigManager.Tick()`；运行时字段变更每 0.25 秒检查一次，Inspector 变更也会通知。帧率、日志设置、对象池默认容量/自动清理/调试信息、默认音量和最大并发音效数量会实际应用。音效并发包含仍在加载的请求，达到上限时回调收到 `null`。`resourceRootPath` 等描述性路径字段不会改变 Unity Resources API 的查找规则。

## 对象池与 UI 的异步行为

池中对象只处于空闲或借出其中一种状态。新加载的对象直接登记为借出，重复归还同一个空闲对象会被忽略；预热只实例化传入的模板，不修改模板本身。容量限制作用于空闲数量，超过容量的归还对象会被销毁。清空池会销毁它拥有的空闲和借出对象；不要在清空后继续使用旧引用。`ResetStatistics()` 保留实际借出数量。不要从外部直接修改兼容字段 `PoolData.poolStack`。

同名 UI 加载期间的多个 `ShowPanel<T>` 共用一次加载并分别完成回调；同名面板应使用同一种组件类型。`HidePanel` 会取消尚未完成的打开，相关回调立即收到 `null`；迟到的资源会被回收，无法重新打开旧请求。加载失败或面板缺少所需组件时，回调也会收到 `null`。再次打开使用新的请求，不会被已取消的旧加载覆盖。

## Unity 行为验证

示例包含可在真实编辑器执行的入口 `AwithGameFrame.Samples.Editor.UnityAdapterSmoke.Run`，使用真实 GameObject、Resources 预制体、Canvas、AudioSource 和 PlayMode 帧循环，断言池借还、UI 异步取消、前后台恢复、配置驱动、重复初始化及最终资源释放。

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -nographics -projectPath '<isolated sample copy>' -executeMethod AwithGameFrame.Samples.Editor.UnityAdapterSmoke.Run -logFile '<smoke.log>'
```

入口自行进入/退出 PlayMode 并返回进程退出码，因此不要传 `-quit`。成功日志包含 `Unity adapter smoke passed:`；断言失败返回非零。它会在隔离工程创建临时资源、使用空场景并在结束后清理，建议通过根 Unity 验证工具运行。验证音源状态不能替代目标设备上的实际听感和移动平台前后台测试。
