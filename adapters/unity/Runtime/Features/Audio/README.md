# Unity 音频后端

`AwithGameFrame.Unity.Audio.UnityAudioBackend(parentTransform)` 通过播放专属 ResourceScope 加载 AudioClip，每条音轨创建独立 AudioSource。传入父对象可把原生播放对象归属到场景/宿主；关闭对应 AudioService 后再销毁父对象。

它不管理全局音频状态。音量、分组、并发与手动/宿主暂停策略由通用 AudioService 负责。native Stop/Dispose 清除 clip 引用并销毁 source 所在 GameObject；之后通用服务释放 scope。与 UnityResourceLoader 配合时资源 key 使用 Resources 路径；自定义 loader 可支持其他分发方式，但需真实释放加载所有权。

所有调用和 await 延续必须在创建时的 Unity 线程。`samples/Unity/Assets/Examples/Foundation/InputAudioSmoke.cs` 使用运行时创建的真实 AudioClip，经生产后端验证 source 音量、mute、暂停、并发和销毁。
