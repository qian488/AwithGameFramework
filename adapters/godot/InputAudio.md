# Godot 输入与音频

`GodotInputBackend(window)` 将普通 control 字符串映射到 Godot 物理键、鼠标、joy button/axis，供通用 InputModule 在主线程 poll。默认键位与 Unity 相同，另提供 `joy/a`、`joy/left-x`、`joy/left-y`；MapKey/MapMouse/MapJoyButton/MapJoyAxis 可覆写或增加映射。MapKey 默认读取物理键，也可指定 `physical: false` 读取逻辑键。焦点读取 Window.HasFocus，重绑定与失焦抬起在通用 InputService 完成。

`GodotAudioBackend(parentNode)` 通过播放专属 ResourceScope 读取 AudioStream，创建独立 AudioStreamPlayer，并令 ProcessMode=Always，把宿主暂停策略交给通用 AudioService。每次复制 stream 后设置 loop，避免修改共享源资源；WAV/Ogg Vorbis/MP3 支持 loop，其它 stream 明确拒绝 loop 请求。

`AudioService` 关闭播放对象时优先等待其 `IAsyncDisposable`：后端先 Stop、清除 Stream、移出父节点并同步 Free player，再逐帧检查独占 stream 副本的引用计数，直到混音线程和 AudioServer 主线程更新实际释放 playback 持有的引用，最后释放副本。完成后通用服务才归还源资源 scope 和并发名额。这个等待依据原生对象所有权，不使用固定延时；五秒内未完成或 SceneTree 已销毁会抛出明确错误，仍释放后端持有的托管引用，服务汇总错误并继续其余清理。

退出前必须 `await AudioService.DisposeAsync()`（宿主使用 `await ShutdownAsync()`），期间保持 SceneTree 存活并继续处理帧。直接调用 voice 的同步 `Dispose()` 只释放本方所有权，不能保证混音线程已处理完淡出，不适合作为立即退出的确认。后端不修改 Godot AudioServer 的全局 bus；所有调用及 await 延续属于创建时的 Godot 线程。

`samples/Godot/InputAudioSmoke.cs` 使用 Input.ParseInputEvent/FlushBufferedEvents 注入并读取真实逻辑键状态，运行时创建 AudioStreamWav，经生产后端检查原生 player 的音量、mute、loop、暂停、并发限制和释放。`StopAsync` 返回后立即验证 player 与私有 stream 已释放，最后一次播放还须归还源资源；不额外等待帧辅助断言，也不依赖声音设备听感。headless 的 DisplayServer 不提供桌面物理键布局转换或真实窗口焦点，因此本检查不声明验证了物理键设备/焦点；焦点抬起策略通过 InputService.SetFocused 验证。
