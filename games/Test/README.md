# Test 游戏工作区

Test 是用来检验框架开发流程的演示工程。粒子演示验证公式、宿主和 DLL 同步。近战关卡 Rain Pass 验证外部玩法、Unity 程序化表现和工作区交付。框架包在仓库根 `src`。

| 区域 | 职责 |
| --- | --- |
| [Domain](src/Test.Domain/README.md) | 纯数学与领域计算，包括粒子发射、近战时序、生命与架势 |
| [Application](src/Test.Application/README.md) | 粒子播放、近战关卡编排和框架服务组合 |
| [Server](server/Test.Server/README.md) | .NET 进程配置、日志、生命周期及专属基础设施接线 |
| `client/Unity` | Unity 2022.3.16f1 工程，负责内容生成、编辑器预览、渲染和宿主接线 |

`game.json` 记录游戏名称、引擎和构建入口，生成的项目通过相对路径引用本克隆的框架。客户端位置和接入命令见仓库的游戏工作区指南，Spine、TMP 等组件在实际引擎工程内安装。

在 Unity Hub 中添加 `games/Test/client/Unity`，使用 Unity **2022.3.16f1** 打开。原来的 `games/Test` 工程已整体移入此目录，原场景 GUID 保留。无需再次创建工程。

需要玩法功能时先在游戏中实现，经过实际维护后再考虑提取为公共框架能力。独立功能的规则和应用编排参考 [portable-feature 模板](../../templates/portable-feature/README.md)。

从仓库根目录执行：

```powershell
powershell -File tools/game.ps1 paths -Name Test
powershell -File tools/game.ps1 build -Name Test
powershell -File tools/game.ps1 sync -Name Test
powershell -File tools/game.ps1 run-server -Name Test
```

修改外部代码后执行 `sync` 即可。`sync` 会先构建当前外部代码，再更新 `Packages/com.awithgameframe.workspace`，避免 Unity 继续加载旧 DLL。生成目录不提交，手写脚本放在客户端 `Assets` 中。需要单独诊断编译时仍可先执行 `build`。

## 演示场景

- `Assets/Scenes/EffectShowcase.unity` 用于比较九个预设。进入 Play Mode 后，在 Game 窗口左侧切换效果。天气、火焰和星光会持续发射，闪电与爆发结束后可点 `Replay` 重播。
- `Assets/Scenes/SampleScene.unity` 用于验证 `GameHost`。场景中的星环实例关闭了 `Play On Enable`，由宿主控制首播和每 3 秒重播。
- `Assets/Scenes/CombatShowcase.unity` 包含雨夜试炼和原版 Rain Pass。打开后点击 Play，或使用 `Tools > Test > Rain Pass > Open and Play`。
- `Assets/Scenes/RainPassCamp.unity` 是雨夜委托的独立入口。打开后点击 Play。

## 雨夜委托

在营地接取委托，进入杉林挑战 Cedar Sentinel。加载时可以取消。在战斗中按 `Esc` 暂停后可以撤退，战败后可以从当前检查点重试。胜利后结算保存完成记录，随后可以返回营地。重开后可以看到完成记录并再次挑战。存档位于 `Application.persistentDataPath/RainPassContract`。

`ContractSession` 管理委托状态和版本化存档。营地通过场景服务加载已有战斗场景，回营时关闭战斗宿主并释放场景。营地界面经视图服务加载。战斗继续使用现有杉林配置、规则和检查点。

在 Project 窗口打开 `Assets/Resources/RainPassContract/Panel.prefab`，可在 Inspector 调整卡片布局、字体、配色和各按钮。打开 `Assets/Scenes/RainPassCamp.unity` 可调整营地相机、Canvas Scaler 和 EventSystem。界面脚本根据委托状态填充标题、状态和错误信息，并选择当前可用按钮。

修改外部程序集后运行 `tools/game.ps1 sync -Name Test`。以下命令在隔离工程中跑 Unity 测试，再用无头 Chromium 对同一个 Prefab 和显示逻辑采集 Screen Space Overlay 画面。先安装与 [WebGL 验证](../../docs/reference/verification.md) 相同的 Playwright 依赖，并填写本机可执行文件路径。

```powershell
powershell -File tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -KeepProject
powershell -File games/Test/verify-rain-pass-ui.ps1 -VerificationSummary '<本次证据目录>/summary.json' -EditorPath '<Unity.exe>' -BrowserPath '<Chromium.exe>'
```

Unity 测试验证正常场景接线、转场和存档。截图使用独立 Overlay Canvas 驱动营地、加载、暂停、战败、结算、下载和玩家设置等状态。PNG 和报告写入同一证据目录下的 `Overlay-*/Browser`。转场观感和真实鼠标焦点可从营地场景进入 Play 后人工检查。

### 手柄委托

Windows 上连接 Xbox 布局手柄，打开 `Assets/Scenes/RainPassCamp.unity` 进入 Play，或启动该场景构建出的 Windows Player。用方向键移动菜单焦点，A 确认，B 关闭弹窗、取消加载或从暂停返回战斗。要放弃委托，在暂停菜单选择「撤回营地」。同一局可交替使用手柄和键鼠。

| 操作 | 手柄 |
| --- | --- |
| 移动、视角、锁定 | 左摇杆、右摇杆、按下右摇杆 |
| 攻击、处决、弹反及格挡 | RB 攻击，破势后长按 RB 处决，LB 按下弹反、按住格挡 |
| 闪避、换武器、暂停 | X、方向键左装剑和右装弓、Menu |

Test 的 Unity 工程用 Input System 读取手柄，将手柄和既有键鼠映射合并为同一组动作。暂停按键设置仍可修改键盘绑定，手柄的 Menu 键跟随同一个暂停动作。手柄断开后摇杆和按键归零，键鼠保持可用。

后台 PlayMode 用虚拟 Xbox 布局手柄验证委托流程。无头浏览器检查暂停提示和焦点画面。实体手柄体验时检查摇杆力度、视角速度和拔插后键鼠接管。

### 玩家设置

在营地或委托暂停时点击「设置」。语言可切换中英文，音乐和音效可分别调整。点击暂停键的当前值后输入字母键、`Esc` 或空格，再点击「应用并保存」。设置会用于当前战斗和之后载入的委托。若按键已被占用，窗口显示冲突。若写盘失败，当前会话继续使用已应用的值，可在窗口内重试保存。

偏好存放在 `Application.persistentDataPath/RainPassSettings`，使用 `-contract-data` 时随隔离数据目录保存。营地持有设置服务，战斗宿主接收偏好并应用到输入和程序化音轨。文案在 [RainPassPreferences](src/Test.Application/Runtime/Combat/RainPassPreferences.cs) 中维护，缺少的中文词条回退到英文。布局在 `Assets/Resources/RainPassContract/Settings.prefab` 中维护。字体是 Noto Sans SC Regular 的 OFL 子集，新增中文文案时需检查子集字符覆盖。

Windows Player 回归沿用下方下载委托的构建和发布步骤，在 `verify-contract-downloads.mjs` 命令末尾追加 `--settings`。它使用隔离存档，验证保存失败重试、跨场景应用、离线重开、输入模态和音轨增益。人工检查从营地进入 Play，试听暂停前后各音轨，实际操作重绑和长按移动键。

### 下载委托

营地可以从发布目录读取盐风堤和余烬铸造厂委托。点 `Next contract` 切换委托，首次点 `Download` 安装，再点 `Accept contract` 进入。用 `F5` 刷新目录，`U` 安装所选委托的发布版本，`Backspace` 回退至前一版。战斗暂停时也可以刷新和更新，继续战斗时沿用进场版本。每张委托独立记录完成状态及各版本的最佳用时。旧版杉林完成档在首次打开时迁移。

版本、地图、Boss 参数和旗帜颜色在 `configuration/contracts.json` 中。发布脚本在隔离 Unity 工程中生成 Boss 表与场景、材质 AssetBundle，并为每个版本输出内容清单。目录最后一条同 ID 记录成为当前发布版本。

```powershell
./tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -BuildScene Assets/Scenes/RainPassCamp.unity -AdditionalBuildScenes Assets/Scenes/CombatShowcase.unity -ReleasePlayer -KeepProject
./games/Test/publish-contracts.ps1 -EditorPath '<Unity.exe>' -VerificationSummary '<证据目录>/summary.json' -OutputDirectory '<新发布目录>'
node games/Test/verify-contract-downloads.mjs '<Player>/Test.exe' '<发布目录>' '<旧版 slot-1.save>' '<新证据目录>'
```

后台验证启动本机 HTTP，并用同一 Windows Player 执行在线更新与离线重开。旧档参数使用旧版杉林委托完成后生成的 `slot-1.save`，脚本会复制到隔离数据目录。人工检查时可把发布目录通过本机 HTTP 提供，用 `-contract-feed http://127.0.0.1:<端口>/` 启动营地 Player，查看两个委托卡片、下载提示以及盐风堤更新后的旗帜和招式节奏。可额外传入 `-contract-data <独立目录>` 隔离试玩数据。

### 委托成绩同步

下载委托通关后，游戏先把成绩和待提交记录一起写入本机委托存档。结算显示待同步时可以返回营地或退出，营地显示尚未确认的成绩数。服务端确认后，本机同时保存确认游标和更新后的待提交列表。成绩按委托 ID 和实际游玩的内容版本记录。

为 Test.Server 配置 `ScorePort` 和 `ScoreDirectory`。客户端可在场景的 `RainPassCamp` Inspector 设置 `Contract Score Url`，或通过 `-contract-scores http://127.0.0.1:<端口>/` 启动 Player。首版使用本机单档案。历史完成记录可迁移，新通关产生可同步的独立提交。

在下载委托的后台验证命令末尾追加 `--scores '<Test.Server.dll>'`，并选择新的证据目录。脚本在独立服务进程上验证正常确认、离线通关后重开、服务端提交后断线、重复提交和双方重启。手动体验从营地选择已安装的下载委托，观察结算文字和回营后的待同步数量。 [服务端配置](server/Test.Server/README.md#委托成绩)

### 云端委托成绩

在营地选中已安装的下载委托，点击「云端成绩」查看该版本的前十名、自己的最佳用时和排名。窗口显示最近一次成功查询的本机时间。网络不可用时，已查询过的版本显示缓存和过期提示。服务恢复后点击「刷新」。待同步成绩在营地保留独立提示。

榜单沿用 `Contract Score Url`，也可用 `Contract Leaderboard Url` 指定查询服务。匿名标识保存在 `RainPassProfile/player-id.txt`。每个独立的 `-contract-data` 目录有自己的档案。既有无玩家标识的成绩归入 `local-player`，新提交归入当前匿名档案。

在下载委托的后台验证命令末尾追加 `--leaderboard '<Test.Server.dll>'`。脚本用三个独立档案完成四次通关，再验证同榜排序、分版本查询、最佳成绩、服务端重启、离线缓存和刷新。人工体验从营地入口打开已安装委托的榜单，检查返回焦点和不同窗口尺寸下的可读性。

## Rain Pass 近战演示

### 雨夜试炼

进入场景后选守关者（Gatekeeper）或巡山者（Patrol Blade）。守关者出招慢、单次伤害高，巡山者出招快、反击空档短。胜利时显示本局时间和该对手的历史最佳。时间按 60 Hz 战斗步进累计，暂停和结算期间停止。胜利且刷新纪录后自动写入，本机存档位于 Unity `Application.persistentDataPath` 下的 `RainPassTrials` 目录。

结算时按 `R` 或点 `Retry` 立即重试。按 `Enter` 或点 `Choose opponent` 返回选择。选关页可用 `Play original Rain Pass` 进入原版三段关卡。

在对手配置的 `rows` 中复制一条记录，提供新的正整数 `id`，然后调整名称和数值。执行 `tools/game.ps1 sync -Name Test` 后重新进入 Play。已有纪录按 `id` 保留。若数值改动需要开启新的成绩榜，给该对手分配新 ID。 [对手配置](configuration/rain-pass-opponents.json)

### 新路线

在 `CombatShowcase` 的选关页选择地图。先清掉巡逻队，再穿过打开的闸门进入 Boss 区。`R` 重试当前检查点，`Enter` 在暂停、失败或通关时从本图入口重开。暂停和结算界面都可返回选关页。

| 地图 | 小怪 | Boss | 场地与打法 |
| --- | --- | --- | --- |
| Cedar Grove（杉林） | 3 名短剑斥候，快速贴身 | 双刃守林人，交替使用延迟斩和双连斩，半血后使用三连斩 | 林道两侧有石障，留意近身后的连续出刀 |
| Salt Causeway（盐风堤） | 2 名长枪兵，窄角远刺 | 破浪统领，使用突刺接横扫和延迟横扫 | 海堤较窄，注意横向走位，石障会挡箭 |
| Ember Foundry（余烬铸造厂） | 2 名盾锤卫兵，正面举盾 | 炉心铁匠，使用慢起手重砸与两段重击 | 炉柱之间有开阔侧翼，绕盾或抓攻击后的空档 |

`CombatMap.cs` 维护路线、巡逻队配置和招式参数。`CombatRun` 推进战斗与检查点，Unity 生成地形、掩体、角色、武器和灯光。新路线使用运行时生成。环境预览和烘焙入口适用于原版 Rain Pass。新路线的通关状态随本局保留，单场试炼记录最佳时间。

### 配置 Boss 试点

选关页的 `Route boss challenges` 提供 **Cedar Sentinel**。点 `Enter route` 从杉林入口开始，清掉斥候后在 Boss 区挑战新的双刃守卫。半血后会出现延迟第三击。直接选择 `Cedar Grove` 仍可挑战原守林人。

用 [Boss 表配置](configuration/README.md) 新增选项或调整招式，再生成、构建和同步。新增选项自动出现在可滚动的选关列表。后台验证使用 Boss ID `101` 完成杉林变体、重试 Boss 检查点并通关。`result.json` 会记录配置 hash、地图布局 hash、场景对象数量、AudioSource 数量和程序化建筑数量，便于检查重复切图后的资源增长。人工验收可检查选项可读性、点击路径和半血后的战斗节奏。

### 原版 Rain Pass

依次通过练习台、三人庭院与守门精英。靠近敌人后观察举剑和刀身变亮的预警，在挥击命中前按下右键弹反。敌方破势后，持剑靠近并长按左键 0.35 秒处决。看到处决提示时，提前松开左键会普通挥剑。练习区和庭院的铁门会在敌人清空后升起。

| 操作 | 键鼠 |
| --- | --- |
| 移动、视角、锁定 | `WASD`、鼠标、按下鼠标滚轮（中键），再按一次解除锁定 |
| 挥击、弹反与格挡、闪避 | 左键、右键按下与按住、`Space` |
| 切换剑、弓和射击 | `1` 装剑，`2` 装弓。装弓时用左键拉弓射击，用准星瞄准或按鼠标中键锁定 |
| 处决、暂停、重试、整关重开 | 长按左键、`Esc`、`R`（暂停或失败时）、`Enter`（暂停、失败或通关时） |
| 调试与取景 | `F2` 显示种子、阶段、弹反窗口和帧时间，`F3` 切换固定取景 |

选中场景中的 `Rain Pass` 根节点，在 Inspector 调整雨量、角色比例和音量。环境种子、密度、雾色和月光保存在 `RainPassEnvironment` 参数资源中。点击 `Generate Preview` 后在 Scene View 看布局与练习角色，`Clear Preview` 清掉临时对象，`Restore Default Parameters` 恢复初始值。运行时用同一个生成器创建环境，并启用天气、程序动作、剑光、合成声音和规则。固定种子复现布局，路线和出生点由关卡设计确定。

试炼和原版关卡均可用弓。每次开局、重试或进入检查点时，从剑和六支箭开始。箭有飞行时间，会被场景遮挡。装弓时可闪避，格挡、弹反和终结需要切回剑。弓身、弓弦和箭都是程序生成的模型。

### 程序化音频

原版 Rain Pass、杉林、盐风堤和余烬铸造厂各有一首循环配乐，分别使用弹拨、长笛、钟音和较低沉的金属音色。每张图都有自己的环境声和脚步音色。敌人出招时鼓点逐渐进入，Boss 区保留底鼓，胜负结算播放独立短曲。切换地图时停止上一张图的音乐和环境声。

战斗音效覆盖剑、长柄和重武器挥动、格挡、弹反、破势、倒地、处决、闪避、开闸、拉弓、放箭和命中。常用音效在三个合成版本之间轮换。在 `Rain Pass` 根节点的 Inspector 用 `Sound Volume` 调整总音量，用 `Music Volume` 单独调整配乐和胜负短曲。旋律与编排位于 [PassScore.cs](client/Unity/Assets/Scripts/Combat/PassScore.cs)，音效合成位于 [PassSound.cs](client/Unity/Assets/Scripts/Combat/PassSound.cs)。

### 环境资产工作流

Rain Pass 环境支持运行时生成和预生成资产两种模式。选中 `Rain Pass` 根节点，在 Inspector 的 `Environment Asset` 区域创建 `RainPassEnvironment` 参数资源。参数资源保存种子、环境密度、月光和雾色，修改它不会直接改写生成结果。

点击 `Bake Environment Asset` 后，Unity 在 `Assets/Content/RainPass/Generated` 保存 Prefab、材质、纹理和 manifest。每个新 Profile 使用自己的输出目录。重烘焙会覆盖当前生成资产的内容并保留已有 GUID，原有 manifest 会在重烘焙时更新为带输出哈希的格式。修改参数后，Inspector 会提示过期。`Validate Generated Asset` 检查来源、缺失文件和过期状态，并在 Console 列出本次烘焙后编辑过的文件。

`RuntimeGenerated` 每次运行时按参数和固定种子重建环境，适合需要动态变化的布局。`BakedAsset` 读取已烘焙的 Prefab，可在 Project 和 Prefab Mode 中维护。需要在重烘焙后保留的手工覆盖可以放到 Prefab Variant 或独立材质。天气、战斗特效、角色和音频按运行时状态创建。清理依据 Profile 及其 manifest 共同登记的输出路径。

`Test.Domain` 负责命中结果、弹反窗口和架势规则。 `Test.Application` 管理固定步进、输入缓冲、进攻轮转和检查点，通过 `ICombatSpace` 请求空间事实。Unity 负责实际扫掠查询、镜头以及几何、材质、贴花、动作、音频、特效和 HUD 的程序化生成。改变招式时序和数值时更新外部代码并重新构建、同步。

### 后台验证

从仓库根目录执行以下命令。第一条验证导入、运行 Unity 测试并构建，第二条运行已经构建的 Windows Player，使用 `summary.json` 中的 `player` 路径。

~~~powershell
powershell -File tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -BuildScene Assets/Scenes/CombatShowcase.unity
powershell -File games/Test/verify-rain-pass.ps1 -PlayerPath '<Player/Test.exe>'
~~~

构建报告记录实际 Unity 版本、导入与构建日志、EditMode 和 PlayMode XML。PlayMode 覆盖试炼启动、重试、换对手和原关卡入口。`dotnet test games/Test/tests/Test.Application.Tests/Test.Application.Tests.csproj` 覆盖试炼配置、胜负、重开、实际文件存档重读及弓箭结算。后台 Player 冒烟验证箭命中真实角色、场景碰撞体能挡箭，以及原关卡的受击重试、弹反、终结和三段通关，结果写入 `result.json`。

### 待人工验收

准备好试玩时，双击构建报告里的 Player，或在 Unity 打开 `CombatShowcase` 并点击 Play。

- 用实体键鼠检查移动、锁定、弹反、闪避和终结的响应，以及暂停后恢复时的鼠标焦点。
- 检查雨雾中的线路、HUD 和举剑预警是否易读，确认弹反特效和声音提示是否清楚。
- 试玩雨夜试炼的两个对手，检查节奏区别、结算提示和连续重试体验。
- 记录首次游玩从练习台到守门精英通关的用时，并注明难以理解或卡住的位置。

需要图形和性能证据时，在有图形的 Windows Player 上传入 `-rain-pass-evidence <证据目录>`，保存截图和帧时间摘要。

已生成的 Rain Pass PNG 可提交给[画面自动验收](../../docs/reference/unity-visual-verification.md)的 `image` 用例，后台检查尺寸、纯色、异常洋红和已评审基准的变化。Rain Pass 的 HUD 在运行时用 IMGUI 绘制，截图应在相应的关卡状态采集。

## 特效库

1. 退出 Play Mode，打开 `Tools > Test > Effect Library`。在左侧按名称、标签和分类查找特效。在右侧看说明、图层和校验提示。`Play Preview` 可在独立预览中播放，支持暂停、重启、循环和拖动时间轴。
2. 用 `Create Effect…` 从模板另存资源，或用 `Duplicate…` 复制现有 Prefab。`Custom Layered Effect` 提供一个可以直接试播的基础环形图层。`Edit Prefab` 打开 Prefab Mode，`Locate` 在 Project 窗口定位资源。列表默认显示小图标，可在根对象的 `Particle Effect Metadata` 中设置 `Thumbnail`。查看实际画面用 `Play Preview`，Unity 的 Project 网格可能把未播放的效果显示为空白缩略图。
3. 在 Inspector 中编辑对应模块。程序化效果在根对象的 `Layers` 增删图层，原生粒子效果在子对象的 `Particle System` 调发射、速度、颜色等参数。闪电在根对象配置分段、抖动和持续时间，通过移动 `Anchor` 子对象改变传播路线。根对象上的元数据可设置说明、分类及搜索标签。
4. 用 `Add to Scene` 放进当前场景，或从 Project 把 Prefab 拖到 Hierarchy。放置可以 Undo。保留 `Play On Enable` 时，实例进入运行时且启用后自动播放，也可由宿主或展示场景调用 `Replay`。

`Assets/Particles/Presets` 下的 `Formula Burst` 是一个由青色螺旋和金色抛射扇两个图层组成的 Prefab。`Spiral` 和 `Projectile Fan` 各自保存一个图层，可单独预览、编辑和放置。两份独立 Prefab 与组合示例共享对应的公式参数资源，需独立调参时复制资源并重新绑定。新增 `Rain`、`Snow`、`Flame`、`Star Sparkle` 与 `Lightning Chain`。

### 使用公式图层

`Formula Burst` 的两层都使用 `Shape = Formula`，通过 `Launch Profile` 引用 `Assets/Particles/Formulas` 下的资源。选中资源可以调公式参数并供多个特效引用。需要独立参数时，在 Project 菜单选择 `Create > Test > Particle Formula` 创建另一份资源，再分配给图层。预览时重新点击 `Preview in Scene View` 即可读入新的资源参数。`u` 是粒子的归一化序号，范围为 0 到 1。

- `Archimedean Spiral` 使用 `θ = 2π × Turns × u` 和 `r = Radius × u`，在局部 XY 平面按 `(r cos θ, r sin θ)` 放置粒子，并以图层的 `Speed` 径向发射。
- `Projectile Fan` 使用 `Launch Angle` 和 `Launch Spread` 分配发射角，初速度为 `Speed × (cos θ, sin θ)`。Unity 粒子系统负责后续运动，并应用项目重力乘以图层的 `Gravity`。
- 新增发射公式时，在 [Domain](src/Test.Domain/README.md) 实现 `ILaunchFormula2D` 并添加纯 .NET 测试。需要 Unity Inspector 配置时，新增一个派生自 `ParticleLaunchProfile` 的资源类型，保存参数并创建对应公式。新类型继续使用同一个 `Shape = Formula` 入口。旧版螺旋和抛射图层仍读取原有内嵌参数。

| 代码所在位置 | 职责 |
| --- | --- |
| Unity 编辑器 `Assets/Scripts/Particles/Editor` | 特效库、Prefab 制作与放置、Inspector 预览 |
| Unity 表现层 `Assets/Scripts/Particles`、`Assets/Particles` | 图层配置、公式资源、把采样结果映射为 ParticleSystem 发射参数、模拟与渲染 |
| 外部纯计算 `src/Test.Domain/Runtime/Mathematics` | 螺旋和抛射的初始发射采样、闪电路径采样，可供 Test 的其他模块复用 |
| Unity 接线 `Assets/Scripts/Bootstrap/TestBootstrap.cs` | 把场景组件的播放回调和 Inspector 中的 `Repeat Seconds` 交给外部模块 |
| 外部客户端逻辑 `src/Test.Application/Runtime/ParticleDemoModule.cs` | 使用 GameHost 的 TimerScheduler 安排首播与重播，关闭宿主时取消计时器。接口使用普通 C# 回调 |

`GameHost` 的 `Repeat Seconds` 控制重播间隔。若它比最长图层的延迟加寿命更短，重播会打断上一次效果。修改外部模块代码后重新执行 `build` 和 `sync`。运行此场景只需 Unity 客户端，服务端入口可单独验证。

运行 `dotnet test games/Test/tests/Test.Domain.Tests/Test.Domain.Tests.csproj` 验证纯公式。在 Unity 的 Test Runner 中运行 EditMode 和 PlayMode 测试，验证资源接入、老图层兼容、预览重建、独立播放与宿主重播。完整工作区步骤见[游戏工作区指南](../../docs/getting-started/game-workspace.md)。

### 用通用编辑器命令演示参数调整

打开 Test 的 `SampleScene`，启用 Unity 菜单 `Tools > AwithGameFrame > Enable Editor Automation`，然后从仓库根执行下列命令。在确认预览结果后提交字段并按需保存场景。

```powershell
$project = @{ Name = 'Test'; Scene = 'Assets/Scenes/SampleScene.unity' }
./tools/unity-editor.ps1 @project -Action prefab.place -Asset 'Assets/Particles/Presets/Formula Burst.prefab' -ObjectName 'Formula Preview'
$effect = @{ Target = '/Formula Preview'; Kind = 'Test.Client.ProceduralParticleEffect' }
./tools/unity-editor.ps1 @project @effect -Action component.inspect -Query 'layers'
$changes = @{ seed = 1234; 'layers.Array.data[0].speed' = 3.5 }
./tools/unity-editor.ps1 @project @effect -Action component.set -Properties $changes -Preview
./tools/unity-editor.ps1 @project @effect -Action component.set -Properties $changes
./tools/unity-editor.ps1 @project -Action scene.save
```

也可用 `asset.find` 查找预设，通过 `scene.transaction` 把放置、坐标和字段写入合为一组预览与提交，再用 `scene.inspect` 或 `component.inspect` 读回。通用命令见 [Unity 编辑器自动化](../../docs/reference/unity-editor-automation.md)。

Test 的 Unity 项目提供模板生成、Prefab 编辑、公式资源绑定和特效预览。框架提供工作区 DLL 构建与同步、宿主生命周期和通用场景自动化。CLI 的事务范围是场景，支持已有序列化标量字段。Prefab 内容、公式资产引用和预览由 Test 的编辑器工具处理。
