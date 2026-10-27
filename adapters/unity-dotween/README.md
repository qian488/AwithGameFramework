# Unity DOTween 接入

可选包 `com.awithgameframe.unity.dotween`。先安装真实 DOTween 插件（`DG.Tweening`）和 Unity 兼容包所需的 UniTask，再安装本包。UPM 不自动提供示例工程内的 vendored 插件。

保留 `AwithGameFrame.Foundation.Providers.DOTweenProvider` 名称与脚本 GUID，程序集为 `AwithGameFrame.Unity.DOTween`。运行时在 SubsystemRegistration 注册 ProviderManager 的默认动画工厂，兼容 FoundationAPI.Initialize/Reset 的使用。宿主也可以不安装本包，显式注入自己的 `IAnimationProvider`。

完整 Unity 示例通过 `tools/verify-unity.ps1 -RunSmoke -BuildPlayer` 验证编译及既有行为；无插件新项目通过 `tools/verify-unity-minimal.ps1` 单独验证。
