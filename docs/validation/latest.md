# 最近验证

## Test 双玩法擂台赛（2026-11-28）

- Unity **2022.3.16f1** 后台隔离导入、EditMode **16/16**、PlayMode **32/32** 和 Windows Release 构建通过。两组隐藏 Windows Player 打完 **2 比 0** 和 **2 比 1**。后一组在 BBads 进行中重启 Test.Server，保留了 1 比 0 的比分和原席位，以新对局 ID 重赛，再完成 Rain Pass 决胜。两端战报一致，Rain Pass 可按存档逐帧复算，BBads 终局背包与服务端快照一致。 [后台复跑及人工验收](../../games/Test/design/dual-mode-tournament/verification.md)
- Test.Domain **60/60**、Test.Application **108/108**、Test.Server **11/11**。真实 WebSocket 检验了 Rain Pass 补帧、局间续接、BBads 快照恢复、上一局迟到输入、确认丢失后只计一次分、弃权超时及重启后读取终局战报。平局计分由规则用例覆盖。
- 标准 `tools/verify.ps1` 通过，公共测试 **579/579**、模板 **13/13**。临时工程由后台脚本清理。局间界面、战报布局和输入手感待人工试玩。
- 最终 Windows Player 在同一构建上复跑了独立 Rain Pass、独立 BBads、擂台赛全胜和擂台赛重启决胜，四条路径通过。结果保存在 `artifacts/game-unity/Test/20261128-170626-a46ba902` 下各路径的 `report.json`。

## Test PvP 帧同步与状态同步（2026-11-28）

- Unity **2022.3.16f1** 完成后台导入、EditMode **16/16**、PlayMode **32/32** 和 Windows Release 构建。两个隐藏 Player 的 Rain Pass 客户端各自演算到第 **263** 帧，覆盖斜向移动、弓箭和结算，终局哈希均为 `e997e4d011612a5e11b47a34c758e58276fabb5a3d75ced00635501ca2da5e8e`。BBads 服务端在第 **269** 步结算，两端本地模拟计数均为 **0**，收到的终局哈希均为 `8cb0a4e6c9a263c54cb9de568057576ff1ea2133730f2067edde99fe31494cbb`。
- 服务端集成测试 **3/3** 对比实际 WebSocket 载荷，验证 Rain Pass 断线前尚未应用的输入帧在重连后重放，以及 BBads 从最新快照恢复。Test.Domain **60/60**、Test.Application **108/108**。原生验证在无图形模式执行，输入手感和画面留待试玩。 [复跑入口](../../games/Test/design/pvp-framework-validation/integration.md)
- 跨运行时对照复现了第 0 帧的带符号零，以及第 35 帧斜向发箭的速度末位差异。统一向量哈希的零编码并明确标量计算顺序后，Mono 与 .NET 8 对相同的 120 帧输入逐帧得到相同哈希。 [帧同步与回放](../../games/Test/design/pvp-framework-validation/determinism-replay.md)

## Test Rain Pass 与 BBads 双客户端 PvP（2026-11-28）

- Unity **2022.3.16f1** 后台隔离导入、EditMode **16/16**、PlayMode **32/32** 和 Windows Release 构建通过。
- 每种模式以两个后台 Windows Player 连接本机 .NET 8 Test.Server。Rain Pass 在第 **270** 步结束，双方状态哈希均为 `a505becd58251ef8a41cf82403fc41dd2784c66404ab99af95cd2407fd97d226`。BBads 完成双方不同背包、就绪、倒计时和自动战斗，在第 **271** 步结束，双方哈希均为 `301e9a030770fe59bc34935119ac686df4994731bc4d93af7d8e240b92ac1a4d`。两场胜者均为席位 0。
- Test.Domain **60/60**、Test.Application **108/108**、Test.Server 集成 **3/3**。集成覆盖两种模式的真实 WebSocket 断开和同局重连、迟到、重复、乱序与超窗输入，以及服务器重启后原房间凭据失效。框架测试 **578/578**、模板 **13/13**、AgentKit 与 Headless 通过。
- 原 Rain Pass 和 BBads Windows Player 无图形冒烟通过。粒子示例由 EditMode 和 PlayMode 覆盖。PvP 的实体输入手感、镜头、音画和界面可读性留待人工体验。 [复跑与体验入口](../../games/Test/design/pvp-framework-validation/integration.md)

## BBads 验证能力补强（2026-11-27）

- Unity **2022.3.16f1** 后台导入、EditMode **16/16**、PlayMode **11/11** 通过。资源审计从现有配置和客户端入口生成 **60** 项，三张图集核对 **24/24** 个 Sprite。故意改错类型和必需键时报告失败，已登记的回退和可选缺失分别给出独立状态。正反例在 `artifacts/game-unity/Test/20261127-111702-06d5e4ac` 和 `20261127-105021-9d43fedc`。独立 UPM 工程也通过导入与模块生命周期检查。
- 迁移预检首次报告 **109** 个断开的 Image 引用。Unity 中实测七张单图的 Sprite 子资源均导入为 fileID `21300000`，修复场景和 Prefab 共 **13** 处序列化引用后，复查 **5** 个场景和 **18** 个 Prefab，发现项为零。两次 JSON 在 `artifacts/bbads-framework`。
- 同一 Windows Player 通过后台无图形及低档 D3D11 冒烟，后者保存三张运行时截图。动态截图通过像素检查。离屏场景用例在正常 BBads 场景通过，在隔离副本恢复旧版 TMP 字段后报告渲染异常并退出失败。证据位于 `artifacts/bbads-framework`。
- Test 六张高低档 PNG 符合通用纹理导入配置，应用和复跑均为零改动。独立 Unity 夹具首次应用变更两张纹理，第二次零改动，并验证 Sprite 标识保留和异常发生后的元数据恢复。标准验证通过框架 **578/578**、模板 **13/13**、AgentKit 契约和 Headless 示例。 [BBads 验证入口](../../games/Test/design/bbads-integration/verification.md)

## Test 接入 BBads 与美术流水线（2026-11-26）

- Unity **2022.3.16f1** 隔离导入及 Windows Release 构建通过。EditMode **16/16**、PlayMode **11/11**、Test Application **97/97**、Domain **60/60**。标准验证通过框架 **577/577**、模板 **13/13**、AgentKit 契约和 Headless 示例。
- Windows BBads 在无图形和两个画质档运行。每轮核对道具放置、拒绝非法放置、召唤、战斗受伤和基地败北停刷，覆盖重开、退出至粒子场景及再次进入。原 Rain Pass Release 冒烟通过三条原路线和 Boss 试炼。构建及截图在 `artifacts/game-unity/Test/20261126-172322-1e78ec50`。
- 纹理规则首次修改 145 个导入器，重复应用为 0。Windows Release 实际绑定 BC7 背景，高档 1080×1920，低档 540×960。完整场景同战斗阶段的 20 帧对照中，开启图集后 Draw Calls 中位数由 28 降至 19。
- Android 目标导入验证了代表资源的 ASTC 设置。临时准备 Unity 2022.3.16f1 的 OpenJDK、SDK 和 NDK 后，APK 构建通过前置检查并进入 Gradle，Gradle 组装失败且没有生成 APK。触控、移动 GPU 和画质待实机验证。 [运行与证据清单](../../games/Test/design/bbads-integration/verification.md)

## Test 并发后台验证（2026-11-26）

- 修复前，两条并发 `game.ps1 build -Name Test` 命令在共享 ConfigGen 中间文件触发 `CS2012`。修复后复跑同一组命令，两条均退出 0。日志位于 `artifacts/game-concurrency-before` 和 `artifacts/game-concurrency-after`。
- 同时启动 Unity **2022.3.16f1** 的 Test Windows64 与 WebGL Release 验证。日志记录 WebGL 等待共享构建，两侧随后分别在隔离工程构建成功。两份产物清单的源码指纹一致，各自工程内复制的 **12 个** DLL/PDB 均与清单匹配。证据见 `artifacts/game-concurrency-after/portable-proof.json`。
- Windows EditMode **16/16**、PlayMode **11/11** 和无图形盐风堤远征通过，报告位于 `artifacts/game-unity/Test/20261126-155148-4a967779`。无头 Chromium 通过 WebGL 的远征菜单、杉林训练、暂停、回营和再次进场，报告位于 `artifacts/game-unity/Test/20261126-155148-20012c65/ExpeditionBrowser`。标准验证通过。

## Test 远征配置校验与阶段编排（2026-11-26）

- Test Application **46/46**、Domain **48/48**。回归覆盖零箭袋容量、未知装备或路线、地图不匹配时的字段诊断，以及按原内容指纹续玩、两种补给、撤退后保留休整点和存档失败重试。标准验证通过框架 **577/577**、模板 **13/13**、AgentKit 契约和 Headless 示例。
- Unity **2022.3.16f1** 的 EditMode **16/16**、PlayMode **11/11** 及 Windows Release 构建通过。盐风堤与余烬铸造厂分别用独立档案后台跑完首段、离线重开、杉林结算、两段回放和联机上榜。盐风堤同时验证安装 v2 后仍按 v1 续玩。远征与普通委托回放回归报告位于 `artifacts/game-unity/Test/20261126-151856-bca0e5d5`。
- WebGL Release 在无头 Chromium 验证打开远征、进入杉林训练、暂停、撤退与再次进场。报告与 7 张截图位于 `artifacts/game-unity/Test/20261126-151404-71efd7d5/ExpeditionBrowser`。

## Test 雨夜远征与并行交付（2026-11-26）

- 从 `e5d9c35` 的关联 worktree 并行交付领域规则、配置、Unity 面板、服务端和后台脚本。五张实际工单的交接位于 `artifacts/agent/boards/test-rain-pass-expedition/deliveries`，状态为 `ready-for-review`。后台脚本首轮触发 Windows 路径长度问题，改派后使用更短的隔离档案路径复跑。新版规格修复了初版导入时损坏的中文验收文字，初版看板保留执行记录。 [需求](../../games/Test/design/expedition/brief.md) · [工单规格](../../games/Test/design/expedition/board.json)
- AgentKit 检查独立 worktree 的原始提交、规格和执行证据，并列出同基线交付的重叠文件。专项测试覆盖重复准备、异基线拒收、冲突检测和主工作区文件保护。标准验证通过框架 **577/577**、模板 **13/13**、AgentKit 契约和 Headless 示例。Test Application **41/41**，Domain **48/48**。
- Unity **2022.3.16f1** 的 EditMode **16/16** 和 PlayMode **11/11** 通过。Windows Release Player 用同一档案跨三次后台启动，验证盐风堤 v1、安装 v2 后离线续玩杉林、首次奖励、装备解锁、两段回放和恢复联机后上榜。下载、设置、成绩同步、常规榜单和委托回放回归通过。远征报告位于 `artifacts/game-unity/Test/20261126-135412-fb5046f6/ExpeditionSmoke/summary.json`，该构建目录下的 `*Regression/report.json` 保存其余报告。余烬铸造厂覆盖常规委托下载与通关，独立的两段远征仍待验收。
- 正式 WebGL Release Player 在无头 Chromium **157.0.8103.79** 完成营地、远征菜单、杉林训练、暂停、撤退和再次进入。原生 Resources 异步请求完成后，资源租约的等待链停住。Unity WebGL Player 改用同步加载内置 Resources，Test 补充远征配置类型的 IL2CPP 保留项。截图还复现了两套暂停 UI 叠加，修复后由营地面板接管委托暂停界面。报告与 7 张截图位于 `artifacts/game-unity/Test/20261126-135412-85b4fa3c/ExpeditionBrowser`。
- 单独构建 WebGL Overlay 夹具，在无头 Chromium 中验证中文营地、滚动焦点、按钮位置、休整和待同步界面。30 张截图副本位于 `artifacts/game-unity/Test/20261126-133728-88a7936a/BrowserAgent/Overlay`。
- 人工体验入口为 `RainPassCamp.unity` 或 Windows Player，可检查实体手柄、休整音效和两条首段路线的战斗节奏。 [启动方式](../../games/Test/README.md#雨夜远征)

## Test 委托通关回放（2026-11-26）

- Unity **2022.3.16f1** 隔离导入、EditMode **16/16**、PlayMode **11/11** 和 Windows Release Player 构建通过。Test Application **26/26**，内容更新专项 **19/19**。报告位于 `artifacts/game-unity/Test/20261126-113801-a6ce9ebc/summary.json`。
- Windows 后台 Player 用三次独立启动验证盐风堤 v1 的结算回放、v2 生效时离线回看 v1、历史包缺失后安全回营。核对了当局的弹反和处决时间、完成步数、最后位置、旗帜和 Boss 配置。报告在同一目录的 `ReplaySmoke/report.json`。体验入口见 [委托回放](../../games/Test/README.md#委托回放)。
- 下载、玩家设置、成绩同步和榜单回归记录位于同一构建目录的 `*Regression/report.json`。回放列表、底部播放控件和版本缺失提示的无头截图位于 `artifacts/game-unity/Test/20261126-113120-dda0a8dd/Overlay-e652e4a1/Browser`。

## Test 手柄委托（2026-11-26）

- Unity **2022.3.16f1** 隔离导入、EditMode **16/16**、PlayMode **11/11** 和 Windows Release Player 构建通过。PlayMode 注入虚拟 Xbox 布局手柄，从营地接取杉林委托，验证摇杆移动、转视角、锁定、弹反、处决、存档和回营。证据位于 `artifacts/game-unity/Test/20261126-111036-3909bae7/summary.json` 与同目录的 `PlayMode.xml`。后台 Player 的设置、下载、成绩同步及榜单回归报告位于同目录下的 `*Regression/report.json`。
- 自动化检查开始暂停时释放攻击、方向键移动焦点、B 继续、恢复后抬起再按才恢复攻击，以及移除设备时释放移动。键鼠接管使用后端的模拟键盘输入值核对。实体手柄的摇杆响应、热插拔和实体键鼠切换待体验，入口见 [手柄委托](../../games/Test/README.md#手柄委托)。
- 无头 Chromium 采集 **22 张** Overlay 截图，复看了手柄继续提示和选中的撤回按钮。截图及报告位于 `artifacts/game-unity/Test/20261126-110652-90e293ec/Overlay-a7045f18/Browser`。

## Test 云端委托成绩（2026-11-26）

- Unity **2022.3.16f1** 的隔离导入、EditMode **16/16**、PlayMode **10/10** 和 Windows Release Player 构建证据位于 `artifacts/game-unity/Test/20261126-102927-91d911f2/summary.json`。Test Application 测试 **25/25**。协议生成检查验证了加入可选玩家标识和新端点后的兼容性，再将榜单端点和记录加入基线。
- 榜单的后台联调使用三个独立档案完成四次委托，覆盖跨玩家排名、按委托版本分榜、更新最佳成绩、同分排序、前十与榜外个人成绩、服务端重启，以及缓存跨进程重开后在线刷新。证据位于同一目录的 `LeaderboardSmoke/report.json`，下载、玩家设置、成绩同步回归分别保存到 `ContractRegression`、`SettingsRegression`、`ScoreRegression`。
- 无头 Chromium 采集 **21 张** 960×600 Overlay 截图。榜单夹具对满榜、查询失败、离线缓存和空榜逐行检查渲染像素及按钮重叠，捕获文本存在但画面缺行的回归。证据位于 `artifacts/game-unity/Test/20261126-101736-6e20c439/Overlay-2b6d6449/Browser`。实际鼠标焦点和其他窗口尺寸的人工检查入口见 [云端委托成绩](../../games/Test/README.md#云端委托成绩)。

## Test 委托成绩同步（2026-11-25）

- Unity **2022.3.16f1** 后台导入、EditMode **16/16**、PlayMode **10/10** 和 Windows Release Player 构建通过。Test Application 测试 **25/25**，包含旧档迁移与待提交记录随通关保存。
- 用同一份 Player 分三次启动，连接独立 Test.Server，实战完成盐风堤 v1、v2 和余烬铸造厂 v1。离线结果重开后仍待确认。服务端落盘后中断首次确认，重启后客户端从游标 2 补回确认，再次重启使用游标 3。重复提交得到原确认，复用 ID 提交不同成绩返回 409。报告及进程日志在 `artifacts/game-unity/Test/20261125-180706-0ffc4f48/ScoreSmoke`。
- 同一 Player 的下载委托与玩家设置回归通过，报告位于同一验证目录的 `ContractRegression/report.json` 和 `SettingsRegression/report.json`。人工体验入口见 [委托成绩同步](../../games/Test/README.md#委托成绩同步)。

## Test 玩家设置（2026-11-25）

- Unity **2022.3.16f1** 隔离导入、EditMode **16/16**、PlayMode **10/10** 和 Windows Release Player 构建通过。后台 Player 验证了暂停键冲突、保存失败及重试、模态窗口释放并抑制持键、播放中的音乐增益、音效独立音量、两个委托应用同一设置和离线重开。报告在 `artifacts/game-unity/Test/20261125-173900-0e13bd35/SettingsSmokeRetry/report.json`。下载委托回归也通过，报告在同一目录下的 `ContractRegression/report.json`。
- 玩家设置由营地宿主持有。进入委托时把偏好应用到独立战斗宿主的输入及程序化音轨。输入验证使用后台可控设备，音频验证读取 Unity 音源增益。实体键鼠和实际听感的检查入口见 [玩家设置](../../games/Test/README.md#玩家设置)。

## Test 可下载委托（2026-11-25）

- Unity **2022.3.16f1** 的隔离导入、EditMode **16/16**、PlayMode **10/10** 和 Windows Release Player 构建通过。Test Application 测试 **24/24**，覆盖旧档迁移以及同一委托不同内容版本的最佳用时。
- 本机 HTTP 发布三份真实 AssetBundle 内容。用同一份后台 Player 完成盐风堤 v1、v2 和余烬铸造厂 v1。测试观察到中断后发出的 HTTP Range 请求，验证坏包不会取代旧版、战斗继续持有旧快照、回营后释放资源，以及回退后可离线重开。旧档由 `b6bf5db` 的委托会话代码生成。报告在 `artifacts/game-unity/Test/20261125-170415-f0cb4dca/ContractSmoke/report.json`。
- Unity 的退出重试延后到 `wantsToQuit` 回调返回后，上述在线和离线 Player 均正常退出。Unity Mono 在深层数据目录创建续传文件时触发 Windows 路径长度限制，验证入口使用较短的独立数据目录。实体键鼠、旗帜实际观感和招式节奏的人工检查入口见 [下载委托](../../games/Test/README.md#下载委托)。

## Test 雨夜委托界面资产化（2026-11-25）

- Unity **2022.3.16f1** 隔离后台导入通过，EditMode **16/16**、PlayMode **10/10**。PlayMode 从正式营地场景验证相机、Canvas 和按钮接线，并覆盖加载取消、暂停撤退、战败重试、胜利存档、存档故障重试和重新读取。报告位于 `artifacts/game-unity/Test/20261125-161324-ce9cb4fe/summary.json`。
- 无头 Chromium 用实际 Panel Prefab 和界面显示逻辑在 Screen Space Overlay Canvas 上渲染八张 **960×600** 截图。测试检查按钮可用性、可见区域、按钮重叠、文字高度和画面像素。PNG、拼图和报告位于 `artifacts/game-unity/Test/20261125-152705-0a7b6654/Overlay-final-browser`。
- 截图夹具提供显示状态和固定相机背景。完整委托流程在无头 WebGL 试跑时于营地主机关闭处超时，玩法与存档以 Unity PlayMode 结果为依据。真实鼠标焦点和转场观感的验收入口见 [雨夜委托](../../games/Test/README.md#雨夜委托)。

## Test 雨夜委托与营地画面（2026-11-25）

- Test 领域测试 **15/15**、应用测试 **23/23**，仓库标准验证通过。Unity **2022.3.16f1** 后台导入通过，EditMode **16/16**、PlayMode **10/10**。回归覆盖营地显示相机、加载取消、进战与回营的相机切换，以及胜利、存档失败重试和读回。
- 在隔离工程的 PlayMode 中用 Direct3D 11 离屏采集 **1280×720** 营地画面，采集夹具临时将 Canvas 切换到相机空间。报告和 PNG 位于 `artifacts/game-unity/Test/20261125-091312-9cdf3b77`。转场观感和实体鼠标焦点的人工验收入口见 [雨夜委托](../../games/Test/README.md#雨夜委托)。

## Windows 干净克隆构建（2026-11-24）

- 基于 `4ec9173` 验证本轮工作区修复。在启用 `core.autocrlf=true` 的改名克隆中，Test 生成配置保留 LF，外部构建和客户端同步通过。构建清单记录 `sourceDirty=false`，同步后的 `Test.Application.dll` 与构建产物 SHA-256 一致。Test 领域测试 **15/15**、应用测试 **21/21**，同一克隆中从脚手架新建的工作区也完成构建。证据在 `artifacts/p0-verified/artifacts`。
- 仓库标准验证通过，框架测试 **575/575**、模板测试 **13/13**、Headless 与 AgentKit 契约通过。Unity **2022.3.16f1** 通过 Test 隔离后台导入与客户端脚本编译。报告在 `artifacts/game-unity/Test/20261124-170559-9a623abf/summary.json`。Windows CI 流程已配置，结果待远端执行。

## 开发控制台与客户端同步（2026-11-24）

- 在 `3cbb5a8` 上的开发控制台工作区，用无界面 Chromium **157.0.8103.79** 执行 Test 构建、配置生成与检查、两组 .NET 测试、共享协议检查和仓库验证。失败用例在页面显示错误与退出码。报告在 `artifacts/DevPanel/smoke.json`。
- 从控制台执行 Test 的「构建并同步客户端」，确认 Unity 工作区中的 `Test.Application.dll` 与新构建产物的 SHA-256 一致。Unity **2022.3.16f1** 隔离后台导入和客户端脚本编译通过。同步报告在 `artifacts/DevPanel/sync-test.json`，Unity 报告在 `artifacts/game-unity/Test/20261124-163115-e6e244ea/summary.json`。

## 通用旧入口清理（2026-11-24）

- 标准验证通过框架 **575/575**、模板 **13/13**、AgentKit 契约和 Headless 共享应用。
- Unity **2022.3.16f1** 通过共享样例的后台导入、PlayMode、Windows x86_64 Mono 构建和 Player 运行。证据在 `artifacts/UnityValidation/6bcad226f365421f8d8a10fb05d213a1`。
- 同版 Unity 通过 Test DLL 工作区导入、EditMode **16/16** 和 PlayMode **6/6**。证据在 `artifacts/game-unity/Test/20261124-154324-58ef3ac2`。
- Godot **4.5.2 .NET** 通过 SDK 构建、后台导入、PCK 发布与 HTTP 安装及共享应用运行。证据在 `artifacts/GodotValidation/9fa620e96b934478a84a530136ecfc67`。

## Unity 旧入口退役（2026-11-24）

- `tools/verify.ps1` 通过。框架 **636/636**、模板 **13/13**，AgentKit 契约和 Headless 示例通过。旧 `FrameworkBootstrap` 专项用例随实现退役，实例宿主的启动、回滚和关闭测试继续执行。
- Unity **2022.3.16f1** 对共享示例完成后台导入、PlayMode、Windows x86_64 Mono 构建与 Player 运行。两种运行模式均通过 UI、场景、输入、音频、AssetBundle、资源租约、共享应用和 SQLite 检查。证据在 `artifacts/UnityValidation/07317d9c56554229b40526f75a79b0d9`。
- 同版 Unity 对 Test 工作区完成 DLL 构建与同步、导入、EditMode **16/16** 和 PlayMode **6/6**。证据在 `artifacts/game-unity/Test/20261124-152220-05f5a488`。

## Unity 工作区与 Addressables（2026-11-24）

- Unity **2022.3.16f1** 后台验证了本地 UPM 方式单独接入版本化存档。隔离工程仅安装 Core、Data、Gameplay 和 Newtonsoft.Json，原有启动组件进入 `Stage`，通过新建存档服务写入并重开读回 `412`。日志与夹具位于 `artifacts/UnitySelectiveAdoption/c2443ae925b048519148b2ac854ec6f4`。
- Unity **2022.3.16f1** 后台验证 Test 编辑器烘焙，覆盖旧清单读回、手工编辑、参数过期、重烘焙保留 Prefab 与材质 GUID、按 Profile 清理和输出路径冲突后回滚。验证结果为 EditMode **16/16**、PlayMode **6/6**，报告在 `artifacts/game-unity/Test/20261124-142022-03783aa8`。
- AgentKit 使用独立 CLI 进程保存 Test 内容草稿并收集上下文。未决问题存在时生成计划返回非零，补全合成验收输入后执行文档检查并生成 `ready-for-review` 报告。证据在 `artifacts/AgentContentDraft/20261124-142931`。
- Unity **2022.3.16f1** 的 Test 隔离工程通过 EditMode **15/15**、PlayMode **6/6** 和 WebGL IL2CPP 构建。无界面 Chromium **157.0.8103.79** 启动至选关，日志记录 Boss 配置 hash、1 个 Boss 挑战和 2 名试炼对手。浏览器报告与截图在 `artifacts/game-unity/Test/20261124-123909-ad00914a/Browser`。
- Addressables **1.21.21** 在同版 Unity 验证了 DLL 工作区接入、两份 localhost HTTP Catalog、同地址版本隔离、旧租约保留，以及独立场景加载和卸载。证据在 `artifacts/UnityAddressablesValidation/018830992c1743df8dd2d5bf9326b76e/client/Fixture`。
- 可选适配器同步调整后，Test 的 Addressables 工作区再次通过资产构建与 PlayMode 回归。证据在 `artifacts/UnityAddressablesValidation/7e54ebe3c65649f8b0a18871894f55fa/client/Fixture/PlayMode.log`。
- 编辑器自动化后台回归通过。新增断引用夹具验证了对象路径、字段、相机数和场景文件 SHA-256。证据在 `artifacts/UnityAutomationValidation/7b67888b7c3f4cc793ac7bf8109d3b8b`。
- Test 使用微信小游戏 SDK **0.1.29** 完成后台导出，产物含 `game.js`，证据在 `artifacts/game-unity/Test/20261124-133716-8ed832ca`。微信开发者工具与设备运行待验收。
- HybridCLR **8.12.0** 的 Windows IL2CPP Player 完成 AOT 补充元数据、更新程序集执行、Player 身份不匹配及篡改拒绝检查。证据在 `artifacts/UnityHybridClrValidation/6c1280dddbc44cb988384ecbd97d2358`。
- 同版 HybridCLR 通过 `verify-unity-hybridclr.ps1 -Name Test` 验证 DLL 工作区分发和 Windows IL2CPP Player。证据在 `artifacts/UnityHybridClrValidation/8706bddea5c248adb4a9465dcd62b919/client/Fixture`。
- 签名内容通过本机 HTTP 下载，使用固定公钥验证后安装，报告在 `artifacts/ContentSignatureValidation/103e84e0c13a4a9b9735831fe935507a/proof/Install-report.json`。
- Test 的 BossStrike XLSX 输入生成 4 个配置产物，与现有样例逐字节相同。Protobuf 使用 Buf **1.75.0** 与 Grpc.Tools **2.72.0** 内置 protoc，生成与检查通过，并检出已登记字段的类型变更。
- 隔离 Protobuf 工作区经 `game.ps1 protocol` 生成、`sync` 构建和分发后，在 Unity **2022.3.16f1** 执行生成类型的序列化与读回。新增消息后使用旧生成产物、变更既有字段类型，两种错误均在构建前检出，上次成功构建的清单 hash 一致。证据在 `artifacts/ProtobufWorkspace-78141ad016d24fcc827d193e0cbaadfd`。
- Test 服务端的独立进程通过 HTTP 就绪、WebSocket 心跳与命令响应、关闭连接后的 HTTP 补查和正常关停。证据在 `artifacts/TestOnlineValidation-756c76762d4a469d99ce320277815223`。
- 标准验证通过框架 **648/648**、模板 **13/13**、AgentKit 契约和 Headless 示例。测试报告在 `artifacts/TestResults`。

## Unity 画面自动验收（2026-11-20）

- Unity **2022.3.16f1 / Direct3D11** 在隐藏的批处理进程离屏渲染 uGUI。正常场景与复拍基准的像素差异为 **0%**。故障夹具检出了出屏、控件重叠、矩形裁切、文字溢出和异常洋红，差异图标出了 **17.99%** 的变化像素。纯黑 PNG 触发了纯色和近黑比例检查。报告和图像在 `artifacts/UnityValidation/ab0de8a4d4674f108e7a60776ff611f0/VisualSmoke`。
- `verify-game-unity.ps1 -VisualSpec` 在 `-nographics` 下检查了 Rain Pass 已存的四张 1920×1080 Player 截图，尺寸、纯色、近黑和洋红比例检查通过。缺图反例返回非零退出码，并保留 `summary.json` 和逐项错误。两次证据分别在 `artifacts/game-unity/Test/20261120-103319-d63d81a1` 和 `artifacts/game-unity/Test/20261120-103605-bf9e880b`。
- `tools/verify.ps1` 通过，框架 **640/640**、模板 **13/13**，Headless 和 AgentKit 契约通过。

## Unity 后台验证（2026-11-19）

- Unity **2022.3.16f1** 的 Windows Mono 样例通过仅构建与后台 Player 冒烟两条路径。`Build-report.json` 分别记录 `runtimeVerified: false` 和 `runtimeVerified: true`。证据在 `artifacts/UnityValidation/082d6a2424cb43fbab3d3fc652a04b9e` 与 `artifacts/UnityValidation/cd7aeabe78bf4d8888542bf3a288acf1`。
- 编辑器自动化默认路径输出 `UNITY_EDITOR_AUTOMATION_BATCH_PASSED`。证据在 `artifacts/UnityAutomationValidation/990315cb9061448cac2a5308c2cc1a8c`。
- Test 完成隔离导入、EditMode **13/13**、PlayMode **4/4** 和 Windows 构建。Rain Pass Player 在无图形模式完成受击重试、弹反、五次终结与通关，随后以退出码 0 结束。构建报告在 `artifacts/game-unity/Test/20261119-181333-ca582e5c`，运行报告在 `artifacts/game-unity/Test/PlayerSmoke-090e909d6460400a8e11ce8b8a071a4e`。实时编辑器检查和实体键鼠、画面、声音体验仍待验收。
- `tools/verify.ps1` 通过，含框架 **640/640**、模板 **13/13**、Headless 示例和 AgentKit 契约。

## Test 特效展示与制作流程（2026-11-19）

- Unity **2022.3.16f1** 隔离工作区通过 EditMode **12/12** 和 PlayMode **3/3**。覆盖预设描述、预览和回退时间后场景与资产保持干净、模板创建、复制、重开、放置和 Undo，以及展示场景逐项切换和原场景的宿主重播。
- D3D11 使用预览相机渲染全部九个预设，图像位于 `artifacts/TestEffectStudio/captures`。Test 外部程序集构建与同步通过，Domain **4/4** 通过。
- 公共 `tools/unity-editor.ps1` 在隔离工程找到 `Spiral`，通过场景事务预览放置、位移及随机种子设置。预览前后场景文件哈希一致，提交后重开读到种子 `1201`。事务记录位于 `artifacts/TestEffectStudio`。
- `tools/verify.ps1` 通过，包含框架 **640/640**、模板 **13/13**、Headless 示例和 AgentKit 契约检查。

## Unity 事务、恢复和工单交付（2026-11-19）

- **P1 Unity 事务与授权。** 用 Unity **2022.3.16f1** 在独立 UPM 工程通过公共 CLI 验证依赖步骤、预览、失败后的内存和磁盘状态、已有未保存修改、一次原生 Undo 和操作记录。实时与批处理的项目白名单均验证了拒绝路径。普通场景命令和诊断脚本均通过。证据位于 `artifacts/UnityAutomationValidation/29f25a3b13364983a6e18f071282f292` 和 `artifacts/UnityAutomationValidation/e5e55790c56f44e384414bd4d19167a3`。
- **P1 工单接手。** 从隔离 `SpecFixture` 工作区导入、分诊并启动倒计时工单。后续进程通过工单路径收集上下文、执行 `game-build` 并生成交接报告。服务端启动时观察到 0 秒就绪、1 秒等待。工单为 `ready-for-review`，依赖工单仍为 `blocked`。报告和行为日志位于 `artifacts/AgentSpecValidation/P0/artifacts/agent/tasks/ticket-e2d2b50ddf1a4ad1b069a24937b5b470`。
- **P2 Unity 检查点。** 同一独立 UPM 工程跨编辑器进程恢复场景和 Prefab。旧哈希被拒绝，复核后恢复的文件与检查点内容一致，Prefab 的 `.meta` 不变。使用游戏工作区 `sync` 分发后，在 `DiagnosticFixture` 客户端重复验证事务预览和检查点捕获。
- **P2 跟踪器和设计验证。** 用隔离议题快照检出远端关闭与本地待评审的状态差异。Unity 原型对照了原生粒子自动播放的两个值并确认预览未写盘。架构对照执行同一组创建对象与无效 Prefab 引用，分别记录了独立保存留下的修改和事务失败后的完整撤销。证据位于 `artifacts/AgentSpecValidation/P0/artifacts/agent/workflow-proof` 和 `artifacts/UnityWorkspaceDiagnosticValidation/P0/games/DiagnosticFixture/client/Unity/workflow-proof`。

`tools/verify.ps1` 通过，框架 **640/640**、模板 **13/13**，Headless 示例与 AgentKit 契约检查通过。Unity 实测范围为 Windows 编辑器，跟踪器以本地快照对照为验收边界。 [编辑器自动化验证](../reference/unity-editor-automation.md#验证)

## Unity 诊断与规格交付（2026-11-19）

- **Unity 2022.3.16f1。** `tools/verify-unity-editor-automation.ps1 -DiagnosticsOnly` 实测警告采集与条数限制、编译中查询、真实 C# 编译错误的文件行列、修复后成功编译、脚本重载与关闭清理。关闭工程后用 CLI 定位批处理编译失败，并验证普通批处理命令显示同一源位置。证据在 `artifacts/UnityAutomationValidation/e4179d6e1c7a4b4f87babdcdd34ac5a3`。标准场景自动化烟测也通过，证据在 `artifacts/UnityAutomationValidation/f6000cce6f424f648607903f09716e03`。
- **游戏工作区分发。** 在独立的 `DiagnosticFixture` 工作区执行 `build` 和 `sync`，再用 `-Name DiagnosticFixture -Action editor.diagnostics -EditorPath '<Unity.exe>'` 得到 `completed`。证据在 `artifacts/UnityWorkspaceDiagnosticValidation/P0` 的客户端 `Library/AwithGameFrameAutomation`。
- **规格与交接。** 临时 `SpecFixture` 工作区用任务说明和 AgentKit 记录验收，验证服务端倒计时在 0 秒就绪、1 秒等待。用新的命令进程从清单定位游戏模块，复跑 `game-build` 和服务端行为检查，报告为 `ready-for-review`。任务说明、日志和报告在 `artifacts/AgentSpecValidation/P0/artifacts/agent/tasks/spec-fixture`。

三个仓库技能的清单校验通过。`tools/verify.ps1` 通过，框架 **638/638**、模板 **13/13**，Headless 示例通过。

## 独立 Unity 工程的自动化验收（2026-11-19）

`tools/verify-unity-editor-automation.ps1` 使用 Unity **2022.3.16f1** 新建隔离工程，通过本地 UPM 引用导入框架并生成验证 Prefab 与组件。公共 CLI 完成资源查找、Prefab 放置、Transform、原生组件添加、嵌套字段预览与成组修改。预览和被拒绝的操作保持场景文件哈希不变，正式提交后重新打开场景读回两个字段。成功标记为 `UNITY_EDITOR_AUTOMATION_PASSED`，证据位于 `artifacts/UnityAutomationValidation/512dcacc50c349159c8cbc7adfa7dff5`。

`tools/verify.ps1 -StaticOnly` 与三个技能的清单校验通过。此项验收覆盖独立 Unity 工程的自动化子集。 [编辑器自动化验证](../reference/unity-editor-automation.md#验证)

## Unity 组件字段与开发工作流（2026-11-19）

在 Unity **2022.3.16f1** 的 Test 工作区副本运行 `tools/verify-unity-editor-automation.ps1`。粒子组件字段可查询。预览和无效的成组修改均保持场景文件哈希不变。批处理提交后重新打开场景，读回随机种子和首层速度。证据位于 `artifacts/UnityAutomationValidation/88e67bb2a7954753a69d0a78e6ad8518`。

独立 UPM 工程的实时验证覆盖 Light 组件的两个浮点字段。预览保持场景为未修改状态，提交后一次 Unity Undo 恢复两个旧值。证据位于 `artifacts/UnityAutomationValidation/upm-live-components/live-editor.log`。三个技能的清单校验及 `tools/verify.ps1` 通过，包含框架 **638/638**、模板 **13/13** 和 Headless 示例。

## Unity 编辑器自动化与仓库技能（2026-11-19）

以 `1e6910f` 为基线的工作区通过 `tools/verify.ps1`，Release 构建 0 警告、0 错误，框架测试 **638/638**、模板测试 **13/13**、Headless 示例和 AgentKit 契约检查通过。三个仓库技能的清单校验通过。

- **Test 工作区分发**使用 `tools/game.ps1 build` 和 `sync`。在 Unity **2022.3.16f1** 隔离工程运行 `tools/verify-unity-editor-automation.ps1`，验证资产查找、对象和组件创建、粒子 Prefab 放置、Transform 落盘，以及拒绝同级重名后场景文件不变。本机证据位于 `artifacts/UnityAutomationValidation/8e2bec6d261541e88b53e63761ba5105`。
- **现代 UPM 适配包**在独立工程通过本地 UPM 引用导入。用 Unity **2022.3.16f1** 在非批处理会话执行菜单命令，随后从外部 CLI 创建对象、确认未保存状态、调整 Transform、用 Unity Undo 恢复并显式保存。退出清除了会话标记。日志位于 `artifacts/UnityAutomationValidation/upm-live/live-editor.log`。菜单和 Undo 通过 Unity Editor API 触发。

## Test 纯公式与 Unity 资源接入（2026-11-19）

`Test.Domain` 的纯 .NET 公式测试 **2/2** 通过。Test Debug 外部程序集与服务端构建均为 0 警告、0 错误，DLL/PDB 同步成功。Unity **2022.3.16f1** 隔离工程的 EditMode **3/3**、PlayMode **2/2** 通过，覆盖公式资源、旧图层内嵌参数、独立播放和宿主重播。Unity 测试报告位于本机 `artifacts/TestFormulaBoundaryValidation`。

## Test 特效库与公式图层（2026-11-19）

在 Unity **2022.3.16f1** 的隔离工程中，EditMode **2/2**、PlayMode **2/2** 通过。覆盖 Prefab 实例接入、螺旋分布、抛射延迟和重力、独立实例启用后自动播放，以及原场景通过宿主首播与重播。Unity 测试报告位于本机 `artifacts/TestParticleLibraryValidation`。仓库标准验证通过，包括框架 **638/638**、模板 **13/13** 和 Headless 示例。

## Test 程序化粒子演示（2026-11-19）

Test Debug 外部程序集和服务端构建均为 0 警告、0 错误，DLL/PDB 同步成功。使用 Unity **2022.3.16f1** 在隔离工程运行 EditMode 与 PlayMode 测试，各通过 1 项。覆盖新增或停用图层后的预览重建、分层延迟、宿主首播与重播、关闭宿主后取消重播及临时对象清理。`tools/verify.ps1 -StaticOnly` 通过。

通过 Direct3D 11 读取场景相机渲染结果，确认粒子实际可见。Windows x64 Development Player 构建通过。日志、测试报告、预览图和 Player 位于本机 `artifacts/TestParticleValidation`。本次运行验收在编辑器内完成，独立 Player 运行以及调试器符号加载、源码断点待验证。

## Test Unity 实际工程接入：2026-11-03

将开发者加入的 Test 工程整理到 `games/Test/client/Unity`，补齐外部 C# 骨架与最小场景接线。Debug 外部构建为 0 警告、0 错误；Unity **2022.3.16f1** 实际导入及脚本编译通过，退出码 0，导入后再次同步通过。过程、日志诊断与范围见[本次记录](2026-11-03-test-unity-integration.md)。

本次没有进入 PlayMode，也未执行 Player 发布、断点调试或 Godot；编译结果不代表这些检查已经通过。

## 游戏工作区与外部构建：2026-11-02

基于 `03d56ce` 的未提交工作区，公共解决方案与隔离游戏骨架 Release 编译通过，均为 0 警告、0 错误；静态检查、工具契约、外部构建更新与失败保护、模块发现和服务端生命周期的直接检查通过。Unity 分发生成器检查和真实 Godot SDK 编译通过，范围与证据见[本次记录](2026-11-02-game-workspace.md)。

本轮没有创建实际 Unity/Godot 游戏工程，没有执行新分发方式的引擎导入/原生运行、调试或导出；静态客户端标记与占位输入只用于工具分支检查。没有运行 NUnit 或 Demo；现有引擎证据仍保留原日期与范围。

## 代码审查与编译：2026-10-30

在 `5f14d1e` 的未提交工作区完成自有实现与构建清单的规范审查，并修复确认的问题。.NET Release、真实 Godot SDK 项目编译均为 0 警告/错误，Unity 2022.3.16f1 隔离导入与编译通过；依赖/元数据、生成产物与 AgentKit 契约检查通过。范围、修复及本地证据见[审查记录](2026-10-30-code-compliance.md)。

本轮按要求将自动测试专项留待后续，没有为修复增加或重跑 NUnit，也未运行 Demo、PlayMode、Player 或 Godot 场景/导出。下方既有行为验收保留各自日期和源码范围。

## 仓库与开发工具：2026-10-29

在 `e1fbb6b` 上的未提交工作区运行标准验证：Release 构建 0 警告/错误，框架 **638/638**、模板 **13/13**，Headless、文档本地链接、依赖/元数据、生成产物及 11 个模块契约通过。另通过真实 CLI 检查模块发现和按模块生成上下文。环境、命令与本地证据见 [本次记录](2026-10-29-repository-organization.md)。

这次修改涉及仓库文档与开发工具，没有重新执行 Unity/Godot、Player 构建或导出。文档与模块路径检查通过不能替代引擎导入和运行。

## 引擎运行时证据：2026-10-27

以下归纳既有框架与引擎证据，不能当作 2026-10-29 工作区的新引擎验收。

当时在 `c94cc9f` 基线上验证未提交工作区，报告记录工作区已修改；相关代码随后提交为 `e1fbb6b`。报告中的源码状态以原报告为准，不将其改写成对后续提交直接执行的结果。完整叙述及此前记录见[2026-10 历史](history/2026-10.md)。

| 范围 | 2026-10-27 实际结果 | 本地证据路径 |
| --- | --- | --- |
| 标准 verify | 依赖、实例/兼容边界、包元数据、生成检查通过；Release 0 警告/错误，框架 602/602、模板 13/13，Headless 通过 | `artifacts/verify-framework-refinement-final.log`（通用 TRX 已被后续运行覆盖） |
| Unity 2022.3.16f1 最小现代工程 | 无插件导入与真实模块生命周期通过；无 Legacy/Compatibility/DOTween/SQLite 程序集 | `artifacts/UnityMinimalValidation/37aaf600c12e41a38cff36826140c2aa`、`artifacts/verify-unity-minimal-refinement.log` |
| Unity 完整样例 / Windows IL2CPP | 29 项旧适配断言、共享 PlayMode、AssetBundle/HTTP 安装、Player 构建运行及可选提供者/SQLite 原生操作通过 | `artifacts/UnityValidation/33faae0c317f4d63ab5b8ff929aae8e9`、`artifacts/verify-unity-refinement-final.log` |
| Godot 4.5.2 .NET / Windows debug | SDK 构建、PCK/HTTP 安装、原生与导出运行通过，含最终生命周期同步重入回归 | `artifacts/GodotValidation/451e2d83f6364b3eb17e50ecb74d77a5`、`artifacts/verify-godot-refinement-final.log` |
| 元数据 | 63 份移动资源的 GUID 保持唯一对应 | `artifacts/framework-refinement-metadata.json` |

Godot 两份最终运行日志包含 `GODOT_HOST_LIFECYCLE_PASSED`，没有 ERROR、UID 或 ObjectDB 退出泄漏；生命周期覆盖多宿主退出、原退出策略恢复、启动失败、启动期暂停与恢复、离树和同步重入关闭。

Unity 示例记录版本仍为 2022.3.40f1c1，实测为 2022.3.16f1。该次未另跑 Mono、Android、WebGL 或移动设备；Godot 未跑其他平台，远端 CI 也不由本地记录证明通过。2026-10-19 的 Mono 等记录保留在历史中，不扩展为 2026-10-27 工作区的同等证据。

日志与产物位于忽略提交的本机 `artifacts/`，其他机器需按[验证命令](../reference/verification.md)重新执行。后续真正执行新检查时，记录日期、源码状态、命令、实际版本、结果及未执行范围，再更新本页。
