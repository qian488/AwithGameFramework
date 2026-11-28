# BBads 图集与合批验证

源素材来自 BBads `72f9e87ca38f00e8e78142e8f8ad12b7e2e2d366`。图集以原图的 GUID 为成员引用，Sprite 切片及其 fileID 继续由原图的 `.meta` 提供。场景、Prefab 及 `Resources.Load<Sprite>` 可以沿用原 Sprite 引用与迁移后的资源键。Unity 通过 Sprite Atlas V2 的 **Include in Build** 自动绑定。

| 图集 | 成员依据 | 源纹理 / Sprite |
| --- | --- | ---: |
| `Backpack` | `item.csv` 的道具外框和图标，以及 `BackpackPresenter` 的 `Bag_Slot` 网格图 | 13 / 13 |
| `BattleHud` | 源 `SampleScene.unity` 引用的 `Art/CF/1` HUD 图，包括 ATK、HP、数据框和波次条 | 6 / 6 |
| `WorldCombat` | `WorldHealthBar` 的背景及两阵营填充，以及 `attack_profile.csv` 中两枚 BBads 投射物 | 5 / 5 |

`Art/CF/BG.png` 是单独的战斗背景。Spine 角色与特效继续使用其骨骼图集、纹理页和材质。TMP 字体及数字由各自的字体与材质管理。成员清单按源纹理列出，包含其现有 Sprite 切片。资源目录支持 `Resources/BBads/…`、`Resources/BBads/BBads/…` 或 `Resources/…`，每个成员须恰好匹配一份纹理。 [成员清单](../../client/Unity/Assets/BBads/Editor/Atlases/Catalog.json)

## 维护图集

将素材和原始 `.meta` 迁入 `Assets/BBads`。Test 的 **Project Settings > Editor > Sprite Packer** 已启用 **Sprite Atlas V2 - Enabled**。画质档位提供独立的背景、基础道具框和 Gangster 图标变体。其余图标和 HUD 继续由共享图集打包。

从 **Tools > Test > BBads > Atlases** 运行 **Validate Catalog** 检查导入设置、Sprite 数量和绑定。成员调整时先编辑清单，运行 **Apply Catalog**，再提交对应的 `.spriteatlasv2` 和 `.meta`。Apply 在生成前检查全部源纹理和切片，并保留现有图集 GUID。默认最大页为 2048，使用自动压缩、sRGB、双线性过滤和 4 px padding，禁用旋转、紧密打包、mipmap 和 Read/Write。两次重复 Apply 的三份图集和三份 `.meta` 散列一致。

已完成迁移且启用 V2 的工程也可以使用后台命令。从仓库根目录执行，按需将最后一个方法换成 `Test.BBads.AtlasTools.BBadsAtlases.Apply`。

```powershell
$unity = 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe'
$project = (Resolve-Path 'games/Test/client/Unity').Path
$log = Join-Path $project 'Logs/BBadsAtlasValidation.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $project,
    '-logFile', $log, '-executeMethod', 'Test.BBads.AtlasTools.BBadsAtlases.Validate')
$quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$run = Start-Process $unity -ArgumentList $quoted -WindowStyle Hidden -Wait -PassThru
if ($run.ExitCode) { throw "Atlas validation failed. See $log" }
```

## Windows 前后对照

从仓库根目录执行回归命令。带 `-SourceAssets` 时，从指定的只读项目 `Assets` 目录复制目录清单中的原图及 `.meta`。省略该参数时使用 Test 已迁入的素材。运行需要 Unity Windows Build Support、Mono 和可用的 Direct3D 11 设备。

```powershell
powershell -NoProfile -File games/Test/client/Unity/Assets/BBads/Editor/Atlases/Verify-BBadsAtlases.ps1 `
    -EditorPath 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe' `
    -SourceAssets 'E:/projects/BBads/Assets'
```

- 命令创建隔离工程，构建两个 Windows x64 Development Player。两者使用同一场景、原始 Sprite 引用、资源键和 Git 中维护的图集设置。基线构建时从隔离工程移出图集文件，避免 Sprite 进入等待晚绑定的状态。图集构建打开自动绑定。
- 两个 Player 用隐藏批处理启动。同一个正交相机每帧手动渲染到 960 × 320 RenderTexture。背包和 HUD 分别使用一张 Screen Space - Camera Canvas 与默认 uGUI Image 材质。世界组使用默认 SpriteRenderer 材质并固定排序。组内重复展示同一批 Sprite，每次只显示一组。资源键通过 `Resources.Load` 或 `Resources.LoadAll` 读取，并与序列化的原 Sprite 核对。
- 每组预热 30 帧，然后用 `ProfilerRecorder` 记录 60 帧的 `Batches Count`、`Draw Calls Count` 和 `SetPass Calls Count`。报告同时记录绘制用 Sprite 的纹理绑定、尺寸、格式、mip 数和各唯一纹理的 `Profiler.GetRuntimeMemorySizeLong`。该内存值是 Unity 报告的纹理对象用量，采样范围由绘制绑定限定。
- 每组保存同一 RenderTexture 的 PNG，并逐像素比较 RGB。测试检查图像含有可见内容，差值超过 32 的像素至多为基线非背景像素数的 5%。测试还检查基线 Sprite 未打包、图集版每组绑定一张图集页、计数稳定且 UI 批次下降。
- `artifacts/bbads-atlas/<运行编号>/` 包含 `comparison.json`、构建与 Player 日志，以及 `baseline` 和 `atlas` 两组原始帧数据、纹理清单和 PNG。通过时命令输出 `BBADS_ATLAS_REGRESSION_PASSED`。 [验证脚本](../../client/Unity/Assets/BBads/Editor/Atlases/Verify-BBadsAtlases.ps1)

### 实测

Unity **2022.3.16f1**，Windows x64 Development，Direct3D 11，RTX 2060，内置渲染管线，Gamma。下表的 60 个样本在各组内一致。图集版的 24 个 Sprite 均已绑定，三组各占一张 RGBA DXT5 页。基线使用 RGBA32，三张源纹理使用 DXT5。

| 组 | 批次 / Draw Calls | SetPass | 纹理数 | 绘制纹理用量（字节） | 图集页 | RGB 最大通道差 > 32 的像素 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Backpack | 13 → 1 | 1 → 1 | 13 → 1 | 824,386 → 262,650 | 512 × 512 | 93 |
| BattleHud | 6 → 1 | 1 → 1 | 6 → 1 | 365,352 → 262,650 | 1024 × 256 | 198 |
| WorldCombat | 16 → 1 | 16 → 1 | 5 → 1 | 20,290 → 16,890 | 128 × 128 | 0 |

两个测试 Player 的 BuildReport 大小为 **92,523,163 → 92,705,223 字节**。纹理对象用量下降，完整测试包增加 182,060 字节。此处的包大小含整个 Development 构建。运行记录位于 `artifacts/bbads-atlas/20261126-170821-ae9513a3`。

同一运行目录的 `test-editor.log` 记录了隔离 Test 客户端的编译和图集校验。该副本经 `tools/game.ps1 sync` 接入 Test DLL，并使用这 24 份源图、原 `.meta` 和启用 V2 的副本设置。

### 完整战斗场景验收

完整战斗在同一设备和相机条件下，使用带图集和移出图集的两个 Development Player。20 帧的 Draw Calls 中位数由 28 降至 19，SetPass 由 18 降至 16。HUD 波次底图、填充和文字底框共享 1024×256 的 BC3 页。TMP、Spine、不同排序层和独立档位纹理仍有各自的提交。移动设备的图集和纹理驻留待真机采集。 [完整战斗证据](verification.md)
