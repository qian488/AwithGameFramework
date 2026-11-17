# Test.Application

本游戏的命令式应用层，目标为 .NET Standard 2.1 / C# 9。依赖 Domain 和公共框架。当前客户端接入保留 Core、Data、Gameplay 作为现代适配器的构建基线；Networking 是可选能力，可按需求同时调整项目引用和游戏 `module.json` 的模块依赖。

`Runtime` 是唯一源码目录。应用层负责采集事实、调用领域规则、提交状态以及执行显式效果。接口使用普通 C# 数据和 `Task`，不要引入 Unity、Godot 或服务端基础设施类型。

`TestModule` 是客户端与服务端可组合的空模块入口，不代表玩法、联网或商业业务已经实现。必要的宿主服务由客户端/服务端注册，具体模块在宿主启动前显式添加并声明依赖。
