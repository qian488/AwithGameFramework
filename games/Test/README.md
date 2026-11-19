# Test 游戏工作区

Test 是用来检验框架开发流程的演示工程。第一个场景展示程序化粒子特效，并用外部 C# 模块验证框架宿主、计时器和 DLL 同步。框架包在仓库根 `src`。

| 区域 | 职责 |
| --- | --- |
| [Domain](src/Test.Domain/README.md) | 游戏内可复用的纯数学和领域计算，包括粒子的发射位置与初速度 |
| [Application](src/Test.Application/README.md) | 用例编排与框架服务组合，包括粒子演示的播放时机 |
| [Server](server/Test.Server/README.md) | .NET 进程配置、日志、生命周期及专属基础设施接线 |
| `client/Unity` | Unity 2022.3.16f1 工程，负责粒子制作、预览、渲染和宿主接线 |

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

修改外部代码后先 `build`，再 `sync`。生成的 `Packages/com.awithgameframe.workspace` 不提交，由这两个命令重建；手写脚本放在客户端 `Assets` 中。

## 演示场景

- `Assets/Scenes/EffectShowcase.unity` 用于比较九个预设。进入 Play Mode 后，在 Game 窗口左侧切换效果。天气、火焰和星光会持续发射，闪电与爆发结束后可点 `Replay` 重播。
- `Assets/Scenes/SampleScene.unity` 用于验证 `GameHost`。场景中的星环实例关闭了 `Play On Enable`，由宿主控制首播和每 3 秒重播。两个场景都在 Build Settings 中。

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
