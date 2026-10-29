# Unity 示例

此目录保留旧 Unity 工程的 Assets、Packages、ProjectSettings。Unity Hub 应打开这里。

框架源码由 `Packages/manifest.json` 通过本地 UPM 引用；勿在 Assets 再复制一份框架。`Assets/Examples` 为演示，`Assets/Test` 为交互验证脚本；自动化单元测试位于仓库根 `tests`。

## 选择接入包

完整示例安装下列组合，既运行现代共享应用，也继续验证旧入口：

| 本地包 | 作用 | 依赖或前置插件 |
| --- | --- | --- |
| [Core](../../src/AwithGameFrame.Core/README.md)、[Data](../../src/AwithGameFrame.Data/README.md)、[Gameplay](../../src/AwithGameFrame.Gameplay/README.md) | 通用逻辑 | 同一份 `Runtime` 源码，无引擎引用 |
| [Networking](../../src/AwithGameFrame.Networking/README.md) | 协议、内容安装与连接能力 | 可选，不进入 Data/Gameplay 依赖 |
| [现代 Unity 适配包](../../adapters/unity/README.md) | 实例宿主、资源、视图/场景、输入/音频后端 | uGUI、Newtonsoft.Json；不需要旧插件 |
| [Unity 兼容包](../../adapters/unity-compat/README.md) | 旧配置/Mono、Foundation 静态服务、PlayerPrefs | 真实 UniTask |
| [DOTween 接入](../../adapters/unity-dotween/README.md) | 兼容动画默认提供者 | 兼容包和真实 DOTween |
| [SQLite 接入](../../adapters/unity-sqlite/README.md) | 数据库连接工厂 | 真实 Mono.Data.Sqlite 与目标平台 SQLite 原生库 |
| [Shared](../Shared/README.md) | 与 Godot/Headless 共用应用流程 | Core/Data/Gameplay |

新工程使用实例 API 时，只安装现代适配及其依赖即可。示例内 vendored 的 UniTask、DOTween 和 SQLite 插件由示例工程提供，UPM 不自动为外部项目安装它们。旧项目的程序集、namespace 和句柄 API 变化见 [迁移说明](../../docs/migrations/README.md)。

## 运行与验证

工程记录编辑器版本为 **2022.3.40f1c1**；实际执行过的编辑器和构建目标见 [验证记录](../../docs/validation/latest.md)。通用框架构建无需安装编辑器。

`Assets/SharedDemo.unity` 运行跨引擎共享冒险：原生 uGUI 菜单、关卡、模态设置，支持语言/音量/静音/按键持久化。空格执行关卡动作，Esc 开关设置，界面按钮完成关卡与保存。原生控件由示例 `DemoUnityView` 创建；应用流程与状态在 `samples/Shared`。批处理 `-executeMethod AwithGameFrame.Samples.Editor.DemoExperienceAssets.Prepare` 可重新生成示例 prefab、WAV 与关卡场景；标准验证自动调用此入口。示例资源缺失会直接报告加载失败。

从仓库根执行真实引擎检查：

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath '<Unity.exe>' -RunSmoke -BuildPlayer
powershell -File tools/verify-unity-minimal.ps1 -EditorPath '<Unity.exe>'
```

第一条验证完整示例、原生资源和独立 Player；第二条创建不复制 Plugins 的现代包工程，检查导入和模块生命周期。其他后端、构建目标、成功标记与运行限制见 [验证参考](../../docs/reference/verification.md)。资源构建、HTTP 安装与加载步骤见 [内容流水线](../../docs/guides/content-pipeline.md)。
