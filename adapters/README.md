# 引擎适配

适配层提供原生对象、设备、加载与生命周期接入，依赖 [通用运行时](../src/README.md)。选择宿主时先读对应包，不需要同时安装所有适配器。

| 接入方式 | 包与目录 | 前提 |
| --- | --- | --- |
| Unity 实例宿主及资源、视图/场景、输入/音频后端 | [unity](unity/README.md) / `com.awithgameframe.unity` | Core/Data/Gameplay、uGUI、Newtonsoft.Json；无旧插件依赖 |
| 旧 Unity 静态服务、配置/Mono、PlayerPrefs 与 Foundation | [unity-compat](unity-compat/README.md) / `com.awithgameframe.unity.compatibility` | 现代 Unity 包、真实 UniTask |
| 兼容动画默认提供者 | [unity-dotween](unity-dotween/README.md) / `com.awithgameframe.unity.dotween` | 兼容包、真实 DOTween |
| SQLite 连接工厂 | [unity-sqlite](unity-sqlite/README.md) / `com.awithgameframe.unity.sqlite` | Data、真实 Mono.Data.Sqlite 与目标平台原生库 |
| Godot .NET 宿主及资源/PCK、Control/场景、输入/音频后端 | [godot](godot/README.md) | Godot .NET；基线 4.5.2 / .NET 8 |

Unity 新工程使用现代包即可；完整 [Unity 示例](../samples/Unity/README.md) 显式安装兼容包和两个可选插件包，继续验证旧入口。Godot 使用独立类型接口，不复用 Unity 对象或程序集。

引擎对象销毁前显式等待宿主关闭；各包说明资源所有权、线程、取消和原生加载限制。不要把 .NET 测试通过当作真实引擎接入已经通过。运行对应 [验证命令](../docs/reference/verification.md)，并记录实际编辑器、构建目标与未执行项，证据见 [最近验证](../docs/validation/latest.md)。

目录与包关系见 [架构概览](../docs/architecture/overview.md)，旧项目的程序集和 API 调整见 [迁移说明](../docs/migrations/README.md)。
