# Unity 示例

此目录保留旧 Unity 工程的 Assets、Packages、ProjectSettings。Unity Hub 应打开这里。

框架源码通过 Packages/manifest.json 本地 UPM 引用 src 与 adapters/unity；勿在 Assets 再复制一份框架。Assets/Examples 为演示，Assets/Test 为交互验证脚本；自动化单元测试位于仓库根 tests。

详细迁移说明见 ../../docs/migration.md。记录编辑器版本 2022.3.40f1c1；框架通用构建无需安装编辑器。
