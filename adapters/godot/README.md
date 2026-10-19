# Godot .NET 适配

基线 Godot **4.5.2 .NET** / .NET 8。`GameHostNode` 创建独立 `GameHost`，在 `_Process(double)` 中用 `Time.GetTicksUsec()` 的单调差值提供未缩放秒数，不把受 `Engine.TimeScale` 影响的 delta 再送入宿主。游戏缩放由 `Host.Clock.TimeScale` 控制；应用恢复时重置时间基线，避免累计后台间隔。节点转发应用暂停/恢复，并在窗口关闭时等待异步清理。覆写 `ConfigureHost` 注册模块，`OnStartedAsync` 中启动应用操作。

删除宿主 Node 或主动 `SceneTree.Quit()` 前先 `await ShutdownAsync()`。Godot 的 `_ExitTree` 不可等待异步清理，故该回调只提供尽力清理；正常退出应走显式关闭路径。

可写路径使用 `ProjectSettings.GlobalizePath("user://...")`。资源、Control、输入、音频继续使用 Godot 原生类型，普通 C# 模块不引用此项目。现有静态 Unity API 不会自动变为 Godot API。

`GodotThreadedResourceLoader` 仅将明确的原始纹理/音频扩展交给托管工作线程，通过线程安全的 `ResourceLoader.Load(CacheMode.Ignore)` 加载；PackedScene、脚本、`.tres/.res` 和未知格式在引擎线程同步加载。这些容器可能引用 C# 脚本，后台重新加载与节点销毁并发时会撞到 Godot 4.5.2 的失效脚本桥接指针；场景加载因此可能占用一帧，应按应用需要安排预加载。loader 跨 store 合并同一路径的在途加载，完成和最后释放回到引擎同步上下文，同时避开原生 `LoadThreadedRequest/Get` 在重复加载验收中复现的 token 泄漏路径。传入宿主 Dispatcher，确保调用和异步后续处于引擎线程；原生加载不能中断，取消后由 ResourceStore 释放迟到结果。可提供保守的字节估算器以使用有限空闲 LRU；默认估算未知、不保留空闲缓存。`GodotResourceLoader` 保留同步加载入口。

`GodotPackResourceLoader` 校验构建生成的目录和 PCK SHA-256，在不可变 `res://awith-content/<version>/` 下挂载，登记导入资源 UID，再交给后台 loader。`tools/build-godot-content.ps1` 在隔离工程 import/export-pack 并收集依赖，输出可交给通用内容更新。Godot 不支持卸载 PCK，包与 UID 属于进程；资源/实例可释放，新版本需新 namespace，磁盘回收通常在重启后进行。完整步骤见 [资源流水线](../../docs/content-pipeline.md)。

`GodotViewFactory` / `GodotView` 将 `PackedScene` 和 `Control` 接到 `ViewService`：层级、模态遮罩、原生焦点、状态/输入与关闭后的 QueueFree 清理。`ViewBindingScope` 可以把显式属性 getter/setter 绑定到控件，无反射并随作用域解绑。`GodotSceneBackend` 加载 PackedScene 并管理实例 Node，Single 只替换本服务拥有的场景，保留 bootstrap 与其他宿主。

`GodotViewFactory(parent, initialize)` 可在实例加入专用 parent 后、Show 之前把 `ViewRequest` 与原生 `Control` 交给宿主注入应用/视图模型。初始化失败仍释放部分实例；共享样例用它配置 `DemoGodotView`。

`GodotInputBackend` 显式映射 key/mouse/joy 到通用控件名，`InputService` 管理动作上下文、优先级、模态、重绑定和失焦释放；`InputModule` 将其接到宿主生命周期。`GodotAudioBackend` 使用真实 AudioStreamPlayer；`AudioService` 管理资源作用域、音量分组、静音、并发及暂停策略，`AudioModule` 处理宿主暂停/恢复/停止。支持循环的原生流按副本配置，避免修改共享资源；不支持的流类型会明确拒绝循环请求。原生输入和无图形验证边界见 [输入与音频](InputAudio.md)。

打开 `samples/Godot/project.godot`。验证见 `tools/verify-godot.ps1 -EditorPath <Godot .NET executable>`；加 `-Export` 可构建并运行 Windows .NET debug 独立包，`-TemplatePath` 可指定匹配版本的模板。日志与版本/产物 SHA-256 报告保存在 `artifacts/GodotValidation`，详见 `docs/validation.md`。此适配不声明 GDScript、Web、移动设备、主机平台或代码热更新已经验证。
