# src 范围约定

- 每包 README 描述接口、依赖和宿主接入。先读涉及的包，避免全仓扫描。
- Core 仅标准库；Data -> Core；Gameplay -> Data/Core。源码只放 Runtime。
- csproj 与 UPM asmdef 同时可用。noEngineReferences 必须为 true；SDK 输出放根 artifacts。
- 公共接口使用普通 C# 类型与 Task，时间/路径/资源由宿主提供。
- JSON/二进制修改需要读取旧格式的测试；示例数据不要带引擎类型。
- 运行 tools/verify.ps1；影响行为的修改应在 tests 中验证，不依赖 Unity 编辑器。
