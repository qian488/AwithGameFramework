# Unity 画面自动验收

为每个需要验收的画面定义一个用例。用例可以离屏渲染场景相机，也可以读取项目已经生成的 PNG。验收产物包括实际画面、可选的像素差异图和逐项结果。画面用例、阈值和经评审的基准图由游戏项目维护。

## 运行

```powershell
powershell -File tools/verify-game-unity.ps1 -Name MyGame -EditorPath '<Unity.exe>' -VisualSpec '<visual-cases.json>'
powershell -File tools/verify-unity.ps1 -EditorPath '<Unity.exe>' -RunVisualSmoke
```

第一条命令在隔离工作区执行游戏用例。含 `scene` 的用例在隐藏的 Unity 批处理进程中使用 GPU 和 RenderTexture。只有 `image` 的清单继续使用 `-nographics`。第二条命令创建独立的正反例，验证正常画面可重复，并对人为引入的 UI 和像素故障报错。

## 用例清单

```json
{
  "schemaVersion": 1,
  "cases": [
    {
      "id": "menu-720p",
      "scene": "Assets/Scenes/Menu.unity",
      "camera": "/Main Camera",
      "width": 1280,
      "height": 720,
      "requireNonUniform": true,
      "maxDarkRatio": 0.98,
      "maxMagentaRatio": 0.005,
      "baseline": "baselines/menu-720p.png",
      "pixelTolerance": 4,
      "maxChangedRatio": 0.002,
      "visible": ["/Canvas/Play", "/Canvas/Settings"],
      "noOverlap": [{ "first": "/Canvas/Play", "second": "/Canvas/Settings" }]
    },
    {
      "id": "gameplay-hud",
      "image": "captures/hud.png",
      "width": 1920,
      "height": 1080,
      "requireNonUniform": true
    }
  ]
}
```

- `scene` 是 `Assets` 下的场景路径。`camera` 和控件使用唯一的绝对层级路径。场景用例指定分辨率，PNG 用例可以省略分辨率，也可填写以检查截图尺寸。`image` 和 `baseline` 的相对路径以清单所在目录为起点。
- `visible` 检查指定的 uGUI 图形是否启用、有面积、处在相机视口内，并检查祖先 `RectMask2D` 的裁切和常规 uGUI `Text` 的尺寸。`noOverlap` 检查指定的控件对的屏幕包围框，可对同一场景登记多种分辨率。包围框重叠代表布局风险，适合只登记不应彼此遮挡的控件。
- `requireNonUniform` 检查接近纯色的画面。`maxDarkRatio` 和 `maxMagentaRatio` 分别限制接近黑色与高饱和洋红的像素比例。两项缺省或填零时关闭。夜景、黑底页面和有意使用洋红的项目应设定各自阈值。
- `pixelTolerance` 是单个像素任一 RGBA 通道的容差，范围 0–255。超过容差的像素占比不得高于 `maxChangedRatio`。基准图缺失或尺寸不同会报错。先运行省略 `baseline` 的用例生成候选图，评审通过后才复制到基准目录。比较时固定 Unity 版本、渲染后端、色彩空间和分辨率，对动态内容固定种子、机位和截帧时机。

## 报告与适用范围

`summary.json` 的 `visual` 字段指向 `Visual/visual-report.json`。每个用例记录分辨率、像素比例、问题描述和实际 PNG。使用基准图时，差异图以红色标出超过容差的像素。日志和报告中的引擎、图形设备及色彩空间用于定位环境差异。用例失败时命令返回非零退出码。

直接捕获针对 Unity Built-in 渲染管线下已保存的编辑态场景，以及相机空间或世界空间 Canvas。Screen Space Overlay 和其他渲染管线使用项目已有的真实截图作为 PNG 输入，运行时生成的 UI、IMGUI 和 UI Toolkit 也可用这一入口。构图、动效节奏和整体可读性继续纳入人工验收。

## 参考实现

- [Unity Graphics Test Framework](https://docs.unity3d.com/Packages/com.unity.testframework.graphics@8.1/manual/index.html) 与 [Unity Graphics 仓库](https://github.com/Unity-Technologies/Graphics/tree/master/Tests/SRPTests) 展示引擎内参考图和图形测试项目的组织方式。
- [Playwright](https://github.com/microsoft/playwright/blob/main/docs/src/test-snapshots-js.md) 展示基准图、渲染环境对齐以及单像素与全图容差的分工。
- [pixelmatch](https://github.com/mapbox/pixelmatch) 展示独立像素比较接口和可视化差异图。本实现使用简单的 RGBA 通道阈值和像素比例。
