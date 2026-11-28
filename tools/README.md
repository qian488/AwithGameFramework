# 开发工具

从仓库根运行下列命令；验证脚本会自行定位仓库，不依赖调用时的工作目录。构建、日志和本地报告写入 `artifacts/`，不提交这些产物。

| 要做的事 | 入口 | 说明 |
| --- | --- | --- |
| 在浏览器运行编译、生成和测试 | [开发控制台](DevPanel/README.md) | 选择框架仓库或游戏工作区，查看命令输出、退出码和日志路径 |
| 创建游戏代码骨架、模块、外部构建、客户端接入和服务端启动 | [game.ps1](game.ps1) | 不创建引擎工程；目录与步骤见[游戏工作区](../docs/getting-started/game-workspace.md) |
| 查询和编辑 Unity 场景 | [unity-editor.ps1](unity-editor.ps1) | 用真实编辑器检查层级、放置 Prefab 和修改对象。见[操作说明](../docs/reference/unity-editor-automation.md) |
| 验证 Unity 编辑器自动化 | [verify-unity-editor-automation.ps1](verify-unity-editor-automation.ps1) | 默认批处理。实时编辑器检查使用 `-RunLiveEditor` |
| 检查依赖、元数据、生成结果，构建、测试并运行 Headless | [verify.ps1](verify.ps1) | 标准交付检查 |
| 检查维护中文档的本地链接 | [verify-docs.ps1](verify-docs.ps1) | 由标准验证调用；检查 Markdown/MDC 行内链接和引用定义的本地目标及仓库范围，跳过代码块；不联网，不检查锚点或 HTML |
| 生成配置 DTO 与数据 | [game.ps1](game.ps1) `config -Name <Game>`、[generate-config.ps1](generate-config.ps1)、[ConfigGen](ConfigGen/README.md) | 分别处理具体游戏与共享样例，`-Check` 只读检查产物 |
| 生成协议并检查兼容性 | [game.ps1 protocol](game.ps1) / [generate-protocol.ps1](generate-protocol.ps1) | 游戏源和基线在 `games/<Name>/protocol`，共享示例仍在 [Protocol](../samples/Protocol/README.md) |
| 生成 Protobuf C# 并检查兼容性 | [generate-protobuf.ps1](generate-protobuf.ps1) | 由 Buf 检查 schema 基线，protoc 生成 C# |
| 构建原生资源包 | [build-unity-content.ps1](build-unity-content.ps1)、[build-godot-content.ps1](build-godot-content.ps1) | 需要真实引擎；生成 AssetBundle/PCK 与地址目录 |
| 发布文件与大小/hash 清单 | [publish-content.ps1](publish-content.ps1) | 写入独立发布目录 |
| 经真实 HTTP 下载并验证内容安装 | [verify-content.ps1](verify-content.ps1) | 校验清单和下载文件；不替代原生资源加载 |
| 验证完整 Unity 示例 | [verify-unity.ps1](verify-unity.ps1) | 后台导入、PlayMode 和构建。追加 `-RunPlayerSmoke` 可后台运行 Windows Player |
| 验证指定游戏的 Unity 工程 | [verify-game-unity.ps1](verify-game-unity.ps1) | 默认后台导入并编译。`-RunTests` 跑两种测试模式，`-TestMode` 和 `-TestFilter` 可缩小复现范围。`-BuildScene` 构建 Player。 [验证参考](../docs/reference/verification.md#unity) |
| 自动检查 Unity 画面 | [verify-game-unity.ps1](verify-game-unity.ps1) `-VisualSpec` | [画面自动验收](../docs/reference/unity-visual-verification.md)包含离屏截图、控件布局断言和基准图对比 |
| 审计 Unity 配置资源键和图集成员 | [verify-game-unity.ps1](verify-game-unity.ps1) `-ResourceSpec`、`-AtlasSpec` | 在隔离工程加载实际资源并检查类型、数量、回退和图集绑定。 [清单格式](../docs/reference/unity-resource-audit.md) |
| 预检已有 Unity 工程 | [verify-unity-migration.ps1](verify-unity-migration.ps1) | 在副本中导入并核对版本、包、场景和 Prefab 引用。 [迁移预检](../docs/reference/unity-migration-audit.md) |
| 后台运行已有 Unity Player | [run-unity-player-smoke.ps1](run-unity-player-smoke.ps1) | 调用项目冒烟入口，记录退出码、成功标记及可选离屏截图。 [Player 冒烟](../docs/reference/unity-player-smoke.md) |
| 验证 Unity 基础适配 | [verify-unity-minimal.ps1](verify-unity-minimal.ps1) | 独立工程仅安装基础适配及其依赖 |
| 验证 Unity Addressables 资源后端 | [verify-unity-addressables.ps1](verify-unity-addressables.ps1) | 在独立工程构建真实资产目录，后台验证租约、类型和取消行为 |
| 验证 Unity WebGL 启动 | [verify-unity-webgl.mjs](verify-unity-webgl.mjs) | 无界面浏览器加载 WebGL Player，保存控制台证据和截图 |
| 验证 HybridCLR | [verify-unity-hybridclr.ps1](verify-unity-hybridclr.ps1) | 在隔离工程生成并后台运行 Windows IL2CPP Player |
| 验证 Godot .NET 接入 | [verify-godot.ps1](verify-godot.ps1) | 原生运行与可选 Windows 导出 |
| 保存开发任务上下文、计划、执行与交接证据 | [agent.ps1](agent.ps1)、[AgentKit 实现](AgentKit/README.md) | 使用约定在 [agent](../agent/README.md)，不进入游戏运行时 |
| 查询框架能力 | `powershell -File tools/agent.ps1 capabilities` | 输出能力负责人、入口、参数、后台/编辑器模式和验证检查 |

```powershell
powershell -File tools/verify.ps1
```

编辑器路径、导出参数、成功标记和检查边界统一见 [验证参考](../docs/reference/verification.md)。资源生成到安装/加载的完整步骤见 [内容流水线](../docs/guides/content-pipeline.md)，实际执行结果见 [最近验证](../docs/validation/latest.md)。配置和协议的 Generated 文件由工具维护，修改源后重新生成，不手改产物。
