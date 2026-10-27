# Unity 范围约定

- 本目录只承载 Unity 类型、API、生命周期和第三方 Unity 插件的接入；通用逻辑移入 src。
- Runtime/Engine、Features 各自有 asmdef；现代适配包不引用兼容包或可选插件。
- 保持现有 namespace 与脚本 .meta，移动文件时同时移动 .meta。
- 示例与场景放 samples/Unity，不放适配运行时包。
- 旧服务在 ../unity-compat；DOTween/SQLite 接入分别在 ../unity-dotween、../unity-sqlite。只安装现代包不需要这些插件；兼容包需要 UniTask，可选包需要其真实插件。
- 先运行 tools/verify.ps1，再用可用 Unity 编辑器验证。不能将纯 .NET 编译替代 Unity 资源/场景验证。
