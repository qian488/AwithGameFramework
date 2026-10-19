# Unity 示例

此目录保留旧 Unity 工程的 Assets、Packages、ProjectSettings。Unity Hub 应打开这里。

框架源码通过 Packages/manifest.json 本地 UPM 引用 src 与 adapters/unity；勿在 Assets 再复制一份框架。Assets/Examples 为演示，Assets/Test 为交互验证脚本；自动化单元测试位于仓库根 tests。

详细迁移说明见 ../../docs/migration.md。记录编辑器版本 2022.3.40f1c1；框架通用构建无需安装编辑器。

`Assets/SharedDemo.unity` 运行跨引擎共享冒险：原生 uGUI 菜单、关卡、模态设置，支持语言/音量/静音/按键持久化。空格执行关卡动作，Esc 开关设置，界面按钮完成关卡与保存。原生控件由示例 `DemoUnityView` 创建；应用流程与状态在 `samples/Shared`。批处理 `-executeMethod AwithGameFrame.Samples.Editor.DemoExperienceAssets.Prepare` 可重新生成示例 prefab、WAV 与关卡场景；标准验证自动调用此入口。示例资源缺失会直接报告加载失败。
