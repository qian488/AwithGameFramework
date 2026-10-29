# 开发工具

从仓库根运行下列命令；验证脚本会自行定位仓库，不依赖调用时的工作目录。构建、日志和本地报告写入 `artifacts/`，不提交这些产物。

| 要做的事 | 入口 | 说明 |
| --- | --- | --- |
| 检查依赖、元数据、生成结果，构建、测试并运行 Headless | [verify.ps1](verify.ps1) | 标准交付检查 |
| 检查维护中文档的本地链接 | [verify-docs.ps1](verify-docs.ps1) | 由标准验证调用；检查 Markdown/MDC 行内链接和引用定义的本地目标及仓库范围，跳过代码块；不联网，不检查锚点或 HTML |
| 生成配置 DTO 与数据 | [generate-config.ps1](generate-config.ps1)、[ConfigGen](ConfigGen/README.md) | 源在 `samples/Shared/Configuration`；`-Check` 检查产物 |
| 生成协议并检查兼容性 | [generate-protocol.ps1](generate-protocol.ps1) | 源和基线在 [Protocol](../samples/Protocol/README.md) |
| 构建原生资源包 | [build-unity-content.ps1](build-unity-content.ps1)、[build-godot-content.ps1](build-godot-content.ps1) | 需要真实引擎；生成 AssetBundle/PCK 与地址目录 |
| 发布文件与大小/hash 清单 | [publish-content.ps1](publish-content.ps1) | 写入独立发布目录 |
| 经真实 HTTP 下载并验证内容安装 | [verify-content.ps1](verify-content.ps1) | 校验清单和下载文件；不替代原生资源加载 |
| 验证完整 Unity 示例 | [verify-unity.ps1](verify-unity.ps1) | 编辑器、PlayMode 与可选 Player 构建/运行 |
| 验证无旧插件的 Unity 接入 | [verify-unity-minimal.ps1](verify-unity-minimal.ps1) | 独立工程，仅安装现代包 |
| 验证 Godot .NET 接入 | [verify-godot.ps1](verify-godot.ps1) | 原生运行与可选 Windows 导出 |
| 保存开发任务上下文、计划、执行与交接证据 | [agent.ps1](agent.ps1)、[AgentKit 实现](AgentKit/README.md) | 使用约定在 [agent](../agent/README.md)，不进入游戏运行时 |

```powershell
powershell -File tools/verify.ps1
```

编辑器路径、导出参数、成功标记和检查边界统一见 [验证参考](../docs/reference/verification.md)。资源生成到安装/加载的完整步骤见 [内容流水线](../docs/guides/content-pipeline.md)，实际执行结果见 [最近验证](../docs/validation/latest.md)。配置和协议的 Generated 文件由工具维护，修改源后重新生成，不手改产物。
