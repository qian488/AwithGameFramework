# 兼容入口

此目录集中旧 BaseManager、FrameworkBootstrap、ServiceLocator 和字符串 EventCenter，保持原 namespace、程序集与 .meta/GUID。实例内核位于同级 Hosting、Events、Flow、Timing、Pooling、Resources。

新增模块使用 GameModule/GameContext、GameServices 与类型 EventBus。标准 verify 检查实例内核和现代 Gameplay 目录，拒绝依赖这些全局门面。兼容 API 的生命周期和状态仍由旧应用负责，不由 GameHost.StopAsync 接管。

两套语义不同：GameServices 拒绝重复服务注册，ServiceLocator 可以覆盖；EventBus 收集每个监听器的异常，EventCenter 保持多播委托的首异常中断语义。迁移时显式选择、转换并测试，不建立隐式转发。
