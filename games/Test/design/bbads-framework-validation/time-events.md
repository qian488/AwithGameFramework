# BBads 时间与事件

战斗场景创建自己的 `BBadsBattleClock`。Unity 适配以未缩放时间驱动 `GameHost`。波次、背包充能、单位移动和攻击、投射物及表现寿命使用该局时钟。F1 行为面板的速度设置仅影响本局。Spine 动画使用本局的速度，HUD 和转场仍可在暂停期间响应。

`BBadsTimingTests` 使用真实的 BBads 战斗场景、配置和投射物，在 2.4 游戏秒附近暂停。鱼将就绪，首波将出现，实弹在飞向基地。暂停 45 帧期间波次进度、冷却、充能、位置及生命值保持一致。恢复后依次收到恢复、鱼就绪、命中与首波事件，每种一次。暂停期间退出并重进后，旧宿主定时器已释放，事件订阅归零，新宿主经过旧定时器的到期窗口也没有旧回调。 [PlayMode 用例](../../client/Unity/Assets/Tests/PlayMode/BBadsTimingTests.cs)

后台回放通过，证据为 `artifacts/game-unity/Test/20261127-122002-448c1d05/timing-pool.xml`。该报告同时包含一次对象池采样等待超时，时间用例为通过。完整回归结果见专项集成报告。
