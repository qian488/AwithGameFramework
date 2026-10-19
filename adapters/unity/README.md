# Unity 适配包

Unity 需要的代码集中在本包，通用能力来自 src 下的 Core、Data、Gameplay。

| 目录 | 职责 / 程序集 |
| --- | --- |
| Runtime/Engine | Mono、场景、Unity 类型契约、Inspector 配置、时间/日志接入；AwithGameFrame.Unity |
| Runtime/Services | 资源、UI、音频、输入、对象池、PlayerPrefs、SQLite 接入与旧 Foundation 门面；AwithGameFrame.Foundation |
| Runtime/Features | 本地化 ScriptableObject/Resources 数据源与 Unity 调试面板；AwithGameFrame.Extensions |

UnityPlatform 自动安装日志、目录和时间源，UnityFrameDriver 提供帧驱动。UnityDataPersistence.CreateConfig() 设置旧存档目录；InitializeAsync(config, enableDatabase: true) 可显式启用数据库存储。详细行为见根 docs/migration.md。

当前示例工程已安装 UniTask、DOTween 和 Mono.Data.Sqlite 插件，UPM 清单提供 Newtonsoft.Json、uGUI。将适配包安装进其他 Unity 工程时，也需要安装这些现有服务使用的插件；它们不属于通用包依赖。Unity 示例和运行时诊断演示位于 samples/Unity/Assets/Examples。

新增引擎实现放独立 adapters/<engine>，不要修改通用源码添加引擎条件编译分支。
