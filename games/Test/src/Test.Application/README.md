# Test.Application

本游戏的命令式应用层，目标为 .NET Standard 2.1 / C# 9。依赖 Domain 和公共框架。当前客户端接入保留 Core、Data、Gameplay 作为现代适配器的构建基线；Networking 是可选能力，可按需求同时调整项目引用和游戏 `module.json` 的模块依赖。

`Runtime` 是唯一源码目录。应用层负责采集事实、调用领域规则、提交状态以及执行显式效果。接口使用普通 C# 数据和 `Task`，不要引入 Unity、Godot 或服务端基础设施类型。

`TestModule` 是客户端与服务端可组合的基础模块入口。`ParticleDemoModule` 由 Unity 客户端注册，使用宿主计时器在首帧请求播放，并按指定间隔重播。它只接收间隔与普通 C# 回调。粒子图层、生成和渲染位于 Unity 工程。

`CombatRun` 在 60 Hz 下消费输入、处理进攻名额并推进关卡和检查点。`ICombatSpace` 接收移动与扫掠请求，通过 `System.Numerics.Vector3` 和参与者编号返回空间事实。`FighterView` 提供快照，`CombatCue` 提供一次性表现事件。
