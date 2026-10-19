# 实例音频

`AwithGameFrame.Gameplay.Audio.AudioService` 接收资源 key、bus 和普通播放选项。通用层不暴露 AudioClip、AudioSource、AudioStream 或 Node；原生后端通过每次播放专属的 `ResourceScope` 加载真实资源并创建 `IAudioVoice`。

```csharp
var audio = new AudioService(resources, nativeBackend, maxConcurrent: 32);
host.AddModule(new AudioModule(audio, "resources"));
audio.SetMasterVolume(0.8f);
audio.SetBusVolume("music", 0.5f);
await using var music = await audio.PlayAsync(new AudioRequest("music-key", "music", loop: true));
music.Pause();
music.Resume();
// StopAsync / DisposeAsync 均等待 native voice 与 ResourceScope 清理。
```

默认 bus 为 `sfx`（限额等于全局限额）与 `music`（限额 1），`ConfigureBus` 可添加分组或调整限额。全局和 bus 最大并发都包含加载中、播放中、暂停中及等待清理的对象；额度不足明确拒绝新播放，不偷偷抢占已有音轨。预先取消的请求不占额度。

有效音量为 master × bus × track，范围 `[0, 1]`；master/bus mute 令有效音量为 0，取消 mute 后恢复原音量。分组设置会应用到已有 voice，加载中的 voice 在开始播放时使用最新设置。

宿主暂停仅影响请求中 `pauseWithHost: true` 的音轨。手动 Pause 与宿主暂停分别记录，宿主 Resume 不会强制恢复手动暂停的音轨；在宿主暂停期间新建的普通音轨也保持暂停。请求可指定 `pauseWithHost: false` 继续播放。原生后端不使用全局静音/暂停单例。

PlayAsync 的 token 控制加载和创建阶段，成功返回后不再控制音轨。取消会及时结束调用等待；即使后端忽略取消，服务仍保留额度并观察晚到结果，停止和销毁 voice 后归还整个 scope。Scope 下的底层加载晚到资源由 ResourceStore 继续管理；服务不关闭共享 ResourceStore，资源模块应后于音频模块关闭。后端必须最终完成或响应取消，关闭不会强行终止无限等待的外部任务。

调用 `Tick()` 检查自然完成并开始清理；AudioModule 自动接入 Tick、Pause、Resume 和异步关闭。Stop/关闭会逐项尝试 native Stop、Dispose、scope.DisposeAsync，聚合错误；重复停止共享同一清理任务。后台自然完成或晚到清理错误由后续 Tick、`TakeErrors()` 或最终 DisposeAsync 报告。关闭音频服务会停止所有在途/已有播放，但不代替调用方观察仍持有的 PlayAsync Task。

服务、handle 与后端绑定创建线程；异步操作必须在 Unity/Godot 或显式无图形同步上下文中发起，确保 await 后继续在宿主线程。后端回调禁止重入修改服务。框架不安装全局上下文、不使用 Task.Run，也不隐藏线程切换。后端创建中途失败需自行销毁部分 native 对象，服务统一释放 scope。

Unity 后端使用 AudioClip/AudioSource；Godot 后端使用 AudioStreamPlayer，并复制 AudioStream 以隔离每次播放的 loop 设置，支持 WAV/Ogg Vorbis/MP3 loop，其它 stream 请求 loop 会明确拒绝。两个后端都在 Stop 时清除原生播放对象对资源的引用，再由 scope 归还源资源。声音设备听感、3D 音频、混响、流式网络音频及平台音频焦点策略不在此基础服务中。

验证：`dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter FullyQualifiedName~AudioServiceTests`；两端 `InputAudioSmoke.RunAsync` 使用真实生成的音频资源检查原生生命周期。
