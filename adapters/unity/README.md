# Unity 适配包

`Editor/Automation` 提供项目内的场景和资产操作。使用现代包或通过游戏工作区同步生成的程序集接入，命令与范围见 [Unity 编辑器自动化](../../docs/reference/unity-editor-automation.md)。

`Editor/UiPrototype` 将项目声明的布局、资源键和控件绑定编译为 uGUI Prefab。运行时的 `UnityPrototypeView` 提供对应的原生控件引用，游戏通过自己的 MonoBehaviour 接线。 [预检和导入](../../games/Test/design/tournament-ui-pipeline/verification.md)

Unity 需要的代码集中在本包，通用能力来自 src 下的 Core、Data、Gameplay。

| 目录 | 职责 / 程序集 |
| --- | --- |
| Runtime/Engine | 实例宿主、资源后端、时间/日志接入、Unity 动画契约与数学工具；AwithGameFrame.Unity |
| Runtime/Features | 实例 UI/场景/输入/音频后端、本地化 ScriptableObject/Resources 数据源；AwithGameFrame.Extensions |
| Editor | AssetBundle 构建与地址目录、[资源与图集审计](../../docs/reference/unity-resource-audit.md)、[纹理导入规则](../../docs/reference/unity-art-import-profiles.md)、[离屏画面与布局验收](../../docs/reference/unity-visual-verification.md)，由 AwithGameFrame.Unity.Editor 提供 |

`UnityPlatform` 将框架日志接到 Unity Console，`GameHostBehaviour` 驱动实例宿主。

本包依赖 Core/Data/Gameplay、Newtonsoft.Json 和 uGUI。仓库示例另行安装 [SQLite](../unity-sqlite/README.md) 及其插件来验证数据库。运行 `tools/verify-unity-minimal.ps1 -EditorPath <Unity.exe>` 可在仅安装基础适配的工程验证模块启动、暂停、恢复和关闭。

新增引擎实现放独立 adapters/<engine>，不要修改通用源码添加引擎条件编译分支。

## 实例宿主与资源

新应用继承 `AwithGameFrame.Unity.Hosting.GameHostBehaviour`，覆写 `ConfigureHost(GameHost)` 注册模块，在 `OnStartedAsync()` 启动应用操作。每个组件拥有独立 `GameHost`，使用秒数驱动 Tick。启动、暂停和停止时持续处理主线程调度。停止一个会话后创建新的宿主。

切走宿主所属场景或销毁组件前先 `await ShutdownAsync()`。正常应用退出会等待清理；`OnDestroy` 只能尽力清理，不能替代显式等待。

`UnityResourceLoader` 可接到通用 `ResourceStore`，使用宿主 Dispatcher 执行原生加载与释放。请求按 key/type 合并，作用域归还全部租约后释放；Unity 的原生 Resources 请求不能取消。非 Prefab 资产释放调用 `UnloadAsset`，Prefab 由宿主选择 `UnloadUnusedAssets` 时回收。直接 `Resources.Load` 的外部引用不在所有权记录中，应统一加载入口。

传入 `ResourceCacheOptions` 可启用通用有限空闲 LRU/预加载；本后端以 Unity Profiler 的 runtime size 估算缓存开销。活跃资源不因空闲预算被淘汰，估算预算不代表实际内存硬上限。缓存条目在淘汰、主动 Trim 或 store 关闭时归还后端所有权。

`UnityAssetBundleLoader` 读取构建生成的 `UnityBundleCatalog`，递归固定 bundle 依赖，跨 store 合并原生 bundle 所有权。普通资源用逻辑 key，场景用 `UnityBundleSceneBackend`，scope 维持到场景卸载。最后所有者释放时 `Unload(true)`，必须先销毁实例再归还租约。`tools/build-unity-content.ps1` 执行原生构建，输出可经通用内容更新安装；命令和版本约束见 [资源流水线](../../docs/guides/content-pipeline.md)。Addressables 另行集成。

项目选择 Addressables 时安装可选的 [Unity Addressables 适配包](../unity-addressables/README.md)。它使用同一 `ResourceStore` 租约边界，Addressables 初始化、Catalog 选择和发布流程由项目组合根负责。

实例后端位于 `Runtime/Features`。`UnityViewFactory` / `UnityViewBehaviour` 用 uGUI root、CanvasGroup、模态遮罩和 EventSystem 接入 `ViewService`。`UnitySceneBackend` 使用原生异步 additive 加载，Single 替换 SceneService 自己拥有的场景。`UnityInputBackend` 映射原生键鼠和轴到控件名，由 `InputService` 管理动作、上下文、重绑定和失焦释放。`UnityAudioBackend` 用 AudioClip/AudioSource 实现 `AudioService` 的分组音量、静音、并发与暂停策略。这些服务通过各自模块纳入宿主生命周期。 [Gameplay](../../src/AwithGameFrame.Gameplay/README.md)

共享应用入口为 `samples/Unity/Assets/SharedDemo.unity`。运行根 `tools/verify-unity.ps1 -EditorPath <Unity.exe> -RunSmoke -BuildPlayer` 验证适配器、共享 PlayMode 与 Windows Mono Player，详细命令及实测版本见 [验证命令](../../docs/reference/verification.md)与[实测记录](../../docs/validation/latest.md)。

`UnityViewFactory(parent, initialize)` 的可选初始化回调在 prefab 实例化后、Show 之前接收 `ViewRequest` 与原生 `GameObject`，供宿主注入共享应用/视图模型；回调失败仍销毁部分实例。共享样例通过此入口配置 `DemoUnityView`，引擎类型不进入通用视图契约。
