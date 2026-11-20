# 验证命令与支持边界

脚本根据自身位置定位仓库，可从任意工作目录调用；下面的相对路径示例从仓库根执行。安装 .NET 9 SDK 与 .NET 8 Runtime。构建、测试结果和日志写入 `artifacts/`，不提交源码仓库。

## 通用运行时与开发工具

```powershell
powershell -File tools/verify.ps1
# PowerShell 7 / CI
pwsh -File tools/verify.ps1
# 只检查文档、依赖边界和包元数据
powershell -File tools/verify.ps1 -StaticOnly
# 定位行为问题时单独运行
dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj
```

标准检查覆盖文档本地链接、源码/项目/asmdef/UPM 依赖、现代实例与兼容边界、GUID/metadata、配置/协议生成和兼容性基线，然后构建 Release、运行框架与功能模板 NUnit、模块/Agent 契约和 Headless 共享流程。TRX 位于 `artifacts/TestResults/`。

只检查文档目标时可运行：

```powershell
powershell -File tools/verify-docs.ps1
```

对应工具目录中的 `docs-verify`。检查 Markdown 行内链接与引用定义所指向的本地文件/目录，排除 legacy、第三方目录、缓存和代码块/行内代码；不联网，不校验锚点或 HTML 链接。它可以发现目录移动造成的本地断链，不能证明外部网站或内容本身正确。

配置或协议源改变后先运行 `tools/generate-config.ps1` / `tools/generate-protocol.ps1`。verify 使用检查模式，不自动覆盖源码；生成文件固定 LF。配置的扩展格式与导表方式见 [ConfigGen](../../tools/ConfigGen/README.md)。

## Unity

```powershell
powershell -File tools/verify-unity.ps1 -EditorPath '<Unity.exe>' -RunSmoke -BuildPlayer
powershell -File tools/verify-unity.ps1 -EditorPath '<Unity.exe>' -BuildPlayer -RunPlayerSmoke
powershell -File tools/verify-unity.ps1 -EditorPath '<Unity.exe>' -BuildPlayer -ScriptingBackend IL2CPP
powershell -File tools/verify-unity-minimal.ps1 -EditorPath '<Unity.exe>'
powershell -File tools/verify-unity-editor-automation.ps1 -EditorPath '<Unity.exe>'
powershell -File tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -BuildScene Assets/Scenes/CombatShowcase.unity
```

编辑器、许可证、目标平台构建模块与原生工具链须在本机可用。示例记录版本为 **2022.3.40f1c1**，最近历史验收实际使用 **2022.3.16f1**；不要将这两个版本混为同一实测版本。

完整脚本在隔离工程导入样例。`-RunSmoke` 启用 PlayMode 冒烟测试，以及测试所需的 AssetBundle 构建、发布和 HTTP 安装。`-BuildPlayer` 生成 Player 产物。追加 `-RunPlayerSmoke` 可用 `-batchmode -nographics` 运行 Windows x86_64 Player 冒烟测试。默认 Mono，IL2CPP 通过 `-ScriptingBackend IL2CPP` 选择。构建报告保存实际编辑器版本、后端、是否运行、源码 commit、工作区修改状态与产物 SHA-256。

必需标记包括 `FOUNDATION_CORE_PASSED`、`SHARED_SCENARIO_PASSED`、`COMPOSED_APPLICATION_PASSED`、`UNITY_PRESENTATION_SCENES_PASSED`、`UNITY_INPUT_AUDIO_PASSED`、`UNITY_ASSET_BUNDLE_PASSED`、`UNITY_SHARED_HOST_PASSED`、`UNITY_OPTIONAL_PROVIDERS_PASSED`，运行以 0 退出。可选提供者断言覆盖 DOTween 注册和 SQLite 原生内存数据库；Player 也再次检查注册。

最小脚本创建仅安装现代通用包与 Unity 包的新工程，不复制 Plugins 或兼容服务，验证真实模块生命周期、可选程序集没有编译/加载，要求 `UNITY_MINIMAL_ADAPTER_PASSED`。

编辑器自动化脚本在独立工程导入现代 UPM 包，生成验证 Prefab 和序列化组件。默认用批处理通过公共 CLI 验证查找与放置、字段读写与预览、白名单、原生粒子字段落盘，以及场景与 Prefab 的重启恢复。成功标记为 `UNITY_EDITOR_AUTOMATION_BATCH_PASSED`。

指定游戏的脚本在隔离工作区后台导入、运行 EditMode 和 PlayMode 测试，并按参数构建单场景 Windows Player。`summary.json` 记录导入、测试与构建结果，附 XML、日志和 Player 路径。项目可提供自己的后台 Player 冒烟入口，例如 [Test 的 Rain Pass 验证](../../games/Test/README.md#rain-pass-近战演示)。

追加 `-VisualSpec '<visual-cases.json>'` 可执行[画面自动验收](unity-visual-verification.md)。用例支持后台离屏截图和已有 PNG 检查，并保存实际画面、差异图与逐项报告。

`-PlayerTarget Android` / `WebGL` 提供构建入口，WebGL 要求 IL2CPP。这些目标只构建并报告 `runtimeVerified:false`，需另行设备/浏览器运行；PlayMode 使用的 Windows bundles 不能代替目标平台资源。Windows IL2CPP 样例只证明所执行代码的 AOT 路径。

### 桌面会话与人工验收

需要桌面会话的自动化检查在约定好时段后执行。`-RunLiveEditor` 启用实时事务回滚、Undo 和未保存状态检查，完整流程通过后输出 `UNITY_EDITOR_AUTOMATION_PASSED`。组合 `-RunLiveEditor -DiagnosticsOnly` 验证实时日志、编译状态、脚本重载与会话清理，成功标记为 `UNITY_EDITOR_DIAGNOSTICS_PASSED`。两种调用都启动非批处理 Unity 编辑器，可能占用桌面。

人工验收由体验者选择时段，从报告记录的 Player 路径或项目场景入口启动。交接时写明启动方式、要观察的现象和预期结果，保留 `manualReview: not-run` 直到获得实际体验反馈。

## Godot .NET

```powershell
powershell -File tools/verify-godot.ps1 -EditorPath '<Godot .NET console executable>'
powershell -File tools/verify-godot.ps1 -EditorPath '<Godot .NET console executable>' -Export -TemplatePath '<windows_debug_x86_64.exe>'
```

基线为 **Godot 4.5.2 .NET / .NET 8**。默认编译真实 Godot SDK、导入资源、构建独立 PCK、发布并通过 HTTP 安装，再运行资源、Control/场景、逻辑输入、音频、时钟与共享应用断言。`-Export` 使用 Windows Desktop 预设发布并运行独立 exe；模板须与编辑器匹配，可省略参数使用已安装模板。脚本只在导出期间设置自定义模板，结束后恢复预设；自动生成的本地 `.sln` 忽略提交。

要求共享基础标记以及 `GODOT_PRESENTATION_SCENES_PASSED`、`GODOT_INPUT_AUDIO_PASSED`、`GODOT_RESOURCE_PACK_PASSED`、`GODOT_HOST_PASSED`、`GODOT_UNSCALED_CLOCK_PASSED`、`GODOT_HOST_LIFECYCLE_PASSED`。普通与导出运行均检查退出码及 ERROR、无效 UID、`ObjectDB instances leaked at exit`，不能仅凭行为标记判定通过。

编辑器/模板从官方 release 获取。当前历史原生与导出证据为 Windows x86_64 .NET debug，其他目标独立验证。CI 包含固定版本 Godot Linux 下载、SHA-512 校验及编译运行入口，远端结果以实际流水线为准；本地 Windows 记录不能证明 Linux CI 已通过。

## 如何解释结果

报告必须区分“命令存在”“静态检查通过”“真实引擎运行”“构建成功”和“目标程序已运行”。最近已有结果与来源见[最近验证基线](../validation/latest.md)，逐次历史见[2026-10 归档](../validation/history/2026-10.md)。此次文档目录重组不产生新的引擎验收记录。

无图形检查不能代替画面、实际声音、物理键布局、桌面焦点、手柄/触屏或移动前后台验证。普通后端测试不替代引擎适配；引擎样例也不证明任意项目 DTO、反射插件或平台 SDK 的 AOT 兼容性。

资源缓存预算只统计空闲项估算。AssetBundle/PCK 不是 Addressables；PCK 不能进程内卸载或替换同 namespace。大小/hash/checksum 校验保证一致性，不提供发布者真实性；CDN、签名密钥、历史版本回收和实际联网服务由项目接入。旧 XOR 是混淆，不是加密；跨进程存储事务、云 SDK、代码热更新及其他平台不在当前验证范围。
