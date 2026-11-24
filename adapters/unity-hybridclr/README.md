# AwithGameFrame Unity HybridCLR

通过项目已安装的 HybridCLR 接入 IL2CPP Player。构建工具按目标平台整理热更新 DLL 与 AOT 补充元数据，启动入口先校验 Player 身份与全部 DLL 的 SHA-256，再按顺序加载。

在 Unity 工程中安装 HybridCLR 8.12.0。DLL 工作区执行 `game.ps1 sync` 后，生成包包含 HybridCLR 适配源码。源码接入的工程添加本地 UPM 适配包。

在 HybridCLR 设置登记更新程序集和补充元数据。用 `HybridClrExport.Generate` 更新生成代码和桥接，再构建 IL2CPP Player。使用 `HybridClrExport.Export` 从相同目标的平台产物生成 `hot-update.json`、`hot/` 和 `aot/`。按依赖顺序登记热更新 DLL，并让 Player 和发布物使用同一个构建 ID。

启动时校验发布来源，再调用 `HybridClrUpdate.Load`。程序集和 AOT 元数据随进程加载，版本切换使用新一轮启动。安装失败时由启动入口报告并选择启动版本。

Windows 验证在隔离工程安装本地 IL2CPP，使用项目自己的插件与同版本的 HybridCLR 原生源码。追加 `-Name Test` 可验证 DLL 工作区接入。 [verify-unity-hybridclr.ps1](../../tools/verify-unity-hybridclr.ps1)
