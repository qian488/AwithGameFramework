# Unity 兼容包

保留旧静态 API，供已有项目迁移。新项目从 [现代 Unity 适配包](../unity/README.md) 的 `GameHostBehaviour` 开始。

| 目录 | 程序集与职责 |
| --- | --- |
| Runtime/Engine | AwithGameFrame.Unity.Legacy：旧 Mono/Scene/Inspector 配置、静态帧驱动和 Unity 类型接口 |
| Runtime/Services | AwithGameFrame.Foundation：旧 UI、音频、输入、资源、对象池、ProviderManager、PlayerPrefs 和 UnityDataPersistence |
| Runtime/Features | AwithGameFrame.Unity.Compatibility：调试控制台与旧本地化启动器 |

安装 `com.awithgameframe.unity.compatibility` 和真实 UniTask 插件，依赖清单会引入现代 Unity 包。安装本包才自动创建旧静态帧驱动、设置旧本地化单例及注册 PlayerPrefs。它不会将旧静态状态绑定到 GameHost 的会话。

DOTween 和 SQLite 分别由 `../unity-dotween`、`../unity-sqlite` 接入。本包可以在没有这两个插件时编译。`FoundationAPI.Initialize()` 初始化 Task/JSON 提供者，动画操作在没有可选包时明确失败；项目也可显式设置 `ProviderManager.AnimationProvider`。`UnityDataPersistence.InitializeAsync(enableDatabase: true)` 要求先安装/注册 SQLite 提供者。

旧脚本 namespace 与 .meta/GUID 保留，旧 Engine 类型的程序集归属已改变。使用这些类型的自定义 asmdef 需要引用 `AwithGameFrame.Unity.Legacy`；调试控制台需引用 `AwithGameFrame.Unity.Compatibility`。旧 Foundation 程序集名称不变。

验证完整示例：`powershell -File tools/verify-unity.ps1 -EditorPath <Unity.exe> -RunSmoke -BuildPlayer`。纯现代包独立编译由 `tools/verify-unity-minimal.ps1` 验证。
