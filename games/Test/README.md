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

## 场景一：星环爆发

1. 在 Unity 打开 `Assets/Scenes/SampleScene.unity`。进入 Play Mode 后，Game 窗口会显示星环爆发，默认每 3 秒重播。场景已加入 Build Settings。
2. 退出 Play Mode，打开 Unity 菜单 `Tools > Test > Particle Effect Library`。窗口可搜索工程中的特效 Prefab。`Star Ring Burst` 与 `Formula Burst` 预设位于 `Assets/Particles/Presets`。
3. 点 `Edit` 进入 Prefab Mode，选根对象，在 Inspector 的 `Layers` 增删或停用图层，并点 `Preview in Scene View` 试播。要从零开始，回到场景后点 `New Scene Effect`，编辑选中对象，再点 `Save Selected as Prefab` 保存到 Project。Prefab 可使用 Unity Project 的重命名、移动和版本管理流程。
4. 点 `Add to Scene`，或从 Project 把 Prefab 拖入 Hierarchy 或 Scene 窗口后保存场景。实例的 `Play On Enable` 可在运行时启用对象时自动播放一次。首场景中的星环实例关闭了该选项，由 `GameHost` 控制首播与重播。

### 使用公式图层

`Formula Burst` 的两层都使用 `Shape = Formula`，通过 `Launch Profile` 引用 `Assets/Particles/Formulas` 下的资源。选中资源可以调公式参数并供多个特效引用。需要独立参数时，在 Project 菜单选择 `Create > Test > Particle Formula` 创建另一份资源，再分配给图层。预览时重新点击 `Preview in Scene View` 即可读入新的资源参数。`u` 是粒子的归一化序号，范围为 0 到 1。

- `Archimedean Spiral` 使用 `θ = 2π × Turns × u` 和 `r = Radius × u`，在局部 XY 平面按 `(r cos θ, r sin θ)` 放置粒子，并以图层的 `Speed` 径向发射。
- `Projectile Fan` 使用 `Launch Angle` 和 `Launch Spread` 分配发射角，初速度为 `Speed × (cos θ, sin θ)`。Unity 粒子系统负责后续运动，并应用项目重力乘以图层的 `Gravity`。
- 新增发射公式时，在 [Domain](src/Test.Domain/README.md) 实现 `ILaunchFormula2D` 并添加纯 .NET 测试。需要 Unity Inspector 配置时，新增一个派生自 `ParticleLaunchProfile` 的资源类型，保存参数并创建对应公式。新类型继续使用同一个 `Shape = Formula` 入口。旧版螺旋和抛射图层仍读取原有内嵌参数。

| 代码所在位置 | 职责 |
| --- | --- |
| Unity 编辑器 `Assets/Scripts/Particles/Editor` | 特效库、Prefab 制作与放置、Inspector 预览 |
| Unity 表现层 `Assets/Scripts/Particles`、`Assets/Particles` | 图层配置、公式资源、把采样结果映射为 ParticleSystem 发射参数、模拟与渲染 |
| 外部纯计算 `src/Test.Domain/Runtime/Mathematics` | 螺旋和抛射的初始发射采样，可供 Test 的其他模块复用 |
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

随机种子、图层和公式资源由 Test 的表现层定义，播放结果由 Test 的编辑器和运行测试验证。通用命令见 [Unity 编辑器自动化](../../docs/reference/unity-editor-automation.md)。
