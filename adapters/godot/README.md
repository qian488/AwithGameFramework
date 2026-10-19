# Godot .NET 适配

基线 Godot **4.5.2 .NET** / .NET 8。`GameHostNode` 创建独立 `GameHost`，通过 `_Process(double)` 以秒驱动 Tick，转发应用暂停/恢复，并在窗口关闭时等待异步清理。覆写 `ConfigureHost` 注册模块，`OnStartedAsync` 中启动应用操作。

删除宿主 Node 或主动 `SceneTree.Quit()` 前先 `await ShutdownAsync()`。Godot 的 `_ExitTree` 不可等待异步清理，故该回调只提供尽力清理；正常退出应走显式关闭路径。

可写路径使用 `ProjectSettings.GlobalizePath("user://...")`。资源、Control、输入、音频继续使用 Godot 原生类型，普通 C# 模块不引用此项目。现有静态 Unity API 不会自动变为 Godot API。

`GodotResourceLoader` 接到通用 `ResourceStore` 并使用宿主 Dispatcher，在引擎线程同步加载 `Resource`。使用 `CacheMode.Ignore` 创建所拥有的原生包装，最后租约归还时 Dispose；没有原生异步加载或缓存预算策略。页面和节点的创建/销毁仍由应用负责。

打开 `samples/Godot/project.godot`。验证见 `tools/verify-godot.ps1 -EditorPath <Godot .NET executable>`；加 `-Export` 可构建并运行 Windows .NET debug 独立包，`-TemplatePath` 可指定匹配版本的模板。日志与版本/产物 SHA-256 报告保存在 `artifacts/GodotValidation`，详见 `docs/validation.md`。此适配不声明 GDScript、Web、移动设备、主机平台或代码热更新已经验证。
