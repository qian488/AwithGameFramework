# Unity 范围约定

- 本目录只承载 Unity 类型、API、生命周期和第三方 Unity 插件的接入；通用逻辑移入 src。
- Runtime/Engine、Services、Features 各自有 asmdef；更改调用关系时同步引用，保持单向依赖。
- 保持现有 namespace 与脚本 .meta，移动文件时同时移动 .meta。
- 示例与场景放 samples/Unity，不放适配运行时包。
- UPM 不自动提供示例工程中 vendored 的 UniTask/DOTween/SQLite；接入依赖在 README 明确记录。
- 先运行 tools/verify.ps1，再用可用 Unity 编辑器验证。不能将纯 .NET 编译替代 Unity 资源/场景验证。
