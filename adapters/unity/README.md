# Unity 适配包

Unity 需要的代码集中在本包，通用能力来自 src 下的 Core、Data、Gameplay。

| 目录 | 职责 / 程序集 |
| --- | --- |
| Runtime/Engine | 实例宿主、资源后端、时间/日志接入、Unity 动画契约与数学工具；AwithGameFrame.Unity |
| Runtime/Features | 实例 UI/场景/输入/音频后端、本地化 ScriptableObject/Resources 数据源；AwithGameFrame.Extensions |
| Editor | AssetBundle 构建与地址/依赖目录；AwithGameFrame.Unity.Editor，仅编辑器 |

UnityPlatform 自动安装日志、目录和时间源，实例 GameHostBehaviour 自行驱动宿主。旧静态帧驱动、UnityDataPersistence 与 FrameworkEntry 位于独立 [兼容包](../unity-compat/README.md)。

新工程只安装本包及 Core/Data/Gameplay、Newtonsoft.Json、uGUI 即可使用现代实例后端，不需要 UniTask、DOTween、Mono.Data.Sqlite。仓库示例另行安装兼容包、[DOTween](../unity-dotween/README.md) 与 [SQLite](../unity-sqlite/README.md) 可选包和真实插件，继续验证旧入口。独立验证入口为 `tools/verify-unity-minimal.ps1 -EditorPath <Unity.exe>`，它创建无 Plugins 的工程并执行现代模块启动、暂停、恢复和关闭。

新增引擎实现放独立 adapters/<engine>，不要修改通用源码添加引擎条件编译分支。

## 实例宿主与资源

新应用继承 `AwithGameFrame.Unity.Hosting.GameHostBehaviour`，覆写 `ConfigureHost(GameHost)` 注册模块，在 `OnStartedAsync()` 启动应用操作。每个组件拥有独立 `GameHost`，使用秒数驱动 Tick；启动、暂停和停止时持续泵主线程调度。停止一个会话后创建新的宿主，旧静态 API 不会因此自动隔离。

切走宿主所属场景或销毁组件前先 `await ShutdownAsync()`。正常应用退出会等待清理；`OnDestroy` 只能尽力清理，不能替代显式等待。

`UnityResourceLoader` 可接到通用 `ResourceStore`，使用宿主 Dispatcher 执行原生加载与释放。请求按 key/type 合并，作用域归还全部租约后释放；Unity 的原生 Resources 请求不能取消。非 Prefab 资产释放调用 `UnloadAsset`，Prefab 由宿主选择 `UnloadUnusedAssets` 时回收。直接 `Resources.Load` 的外部引用不在所有权记录中，应统一加载入口。

传入 `ResourceCacheOptions` 可启用通用有限空闲 LRU/预加载；本后端以 Unity Profiler 的 runtime size 估算缓存开销。活跃资源不因空闲预算被淘汰，估算预算不代表实际内存硬上限。缓存条目在淘汰、主动 Trim 或 store 关闭时归还后端所有权。

`UnityAssetBundleLoader` 读取构建生成的 `UnityBundleCatalog`，递归固定 bundle 依赖，跨 store 合并原生 bundle 所有权。普通资源用逻辑 key，场景用 `UnityBundleSceneBackend`，scope 维持到场景卸载。最后所有者释放时 `Unload(true)`，必须先销毁实例再归还租约。`tools/build-unity-content.ps1` 执行原生构建，输出可经通用内容更新安装；命令和版本约束见 [资源流水线](../../docs/content-pipeline.md)。Addressables 另行集成。

新增实例后端位于 `Runtime/Features`。`UnityViewFactory` / `UnityViewBehaviour` 用 uGUI root、CanvasGroup、模态遮罩和 EventSystem 接入 `ViewService`；`UnitySceneBackend` 使用原生异步 additive 加载，Single 只替换 SceneService 自己拥有的场景，不卸载 bootstrap/其他宿主场景。`UnityInputBackend` 显式映射原生键鼠/轴到控件名，由 `InputService` 管理动作、上下文、重绑定和失焦释放。`UnityAudioBackend` 用 AudioClip/AudioSource 实现 `AudioService` 的分组音量、静音、并发与暂停策略。这些服务通过各自 module 纳入宿主生命周期，API 详见 Gameplay README；旧静态门面继续保留原有接口。

共享应用入口为 `samples/Unity/Assets/SharedDemo.unity`。运行根 `tools/verify-unity.ps1 -EditorPath <Unity.exe> -RunSmoke -BuildPlayer` 验证适配器、共享 PlayMode 与 Windows Mono Player，详细命令及实测版本见 `docs/validation.md`。

`UnityViewFactory(parent, initialize)` 的可选初始化回调在 prefab 实例化后、Show 之前接收 `ViewRequest` 与原生 `GameObject`，供宿主注入共享应用/视图模型；回调失败仍销毁部分实例。共享样例通过此入口配置 `DemoUnityView`，引擎类型不进入通用视图契约。

## 生命周期与配置

下列兼容行为需要另行安装 `com.awithgameframe.unity.compatibility`；Inspector 配置和旧 Mono/场景/类型接口归属 `AwithGameFrame.Unity.Legacy`，调试控制台及旧本地化启动器归属 `AwithGameFrame.Unity.Compatibility`。原 namespace、脚本 GUID 保留；自定义 asmdef 和程序集反射按 [迁移说明](../../docs/migration.md) 更新。

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
