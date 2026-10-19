# Unity 动作输入后端

`AwithGameFrame.Unity.Input.UnityInputBackend` 实现通用 `IInputBackend`，使用 Unity 内置 Input API。配合 `InputService(host.Events)` 与 `InputModule(input, backend)` 在宿主线程每帧 poll；不调用旧静态 InputManager，也不自动注册 Mono 回调。

默认 control 包含 `keyboard/space`、`keyboard/enter`、`keyboard/escape`、`keyboard/w/a/s/d`、`mouse/left/right`。通过 `MapKey(control, KeyCode)`、`MapMouse(control, button)`、`MapAxis(control, unityAxis)` 覆写或增加设备映射；axis 名称需配置在 Unity Input Manager。未知 control 明确报错，按键重绑定使用通用 InputContext，不把 KeyCode 写入通用模块。

焦点来自 `Application.isFocused`，失焦后 InputService 补发抬起。此后端适用于启用旧 Input Manager 的项目；仅启用新 Input System 的项目应另行实现 IInputBackend。旧 Input API 无公开合成按键接口，原生 smoke 检查实际 key/mouse 读取，并以确定输入帧验证 focus release。
