# BBads 迁移基线

源工程为本地 BBads 工作区，读取 Git 提交 `72f9e87ca38f00e8e78142e8f8ad12b7e2e2d366`。启用的入口是 Unity 6000.3.6f1 的 `Assets/Scenes/SampleScene.unity`。Test 中的 `Assets/BBads/Scenes/BBadsBattle.unity` 沿用 GUID `8c9cfa26abfee488c85f1582747f6a02`。版本迁移以提交中的场景、脚本、包清单和配置为基线。实际 Player 验证使用 Test 的 Unity 2022.3.16f1。

| 项目 | 源工程 | Test 的实现 |
| --- | --- | --- |
| 场景与素材 | 场景、Prefab、Sprite、Spine 工程和资源键 | 移入 `Assets/BBads`，沿用序列化引用的 GUID 和切片 ID。 |
| 2D 渲染 | URP 17.3，白色 Global Light 2D，强度 1 | 保留 Test 的 Built-in 管线，使用无光照 UI、Sprite 和 Spine 对应材质。移除源场景的 URP 相机扩展与全局灯节点。 |
| 输入 | Input System 1.18 的 UI 默认 Action 资产 | 使用 Test 的 StandaloneInputModule 和双 Input Handling。背包通过 uGUI 接收拖拽。 |
| uGUI 与 TMP | Unity 6 的 uGUI 2.0 | 使用 Test 的 uGUI 1.0 与 TMP 3.0.6。两份 SDF 字体转换成 TMP 3.0 的材质字段。 |
| Spine | Spine 4.2 与 Unity 6 Rigidbody2D API | 使用同版本 Spine runtime，Root Motion 在 Unity 2022 使用 `Rigidbody2D.velocity`。 |
| 场景数据 | 六张 CSV，场景选择器 `demo_001` | 在游戏配置目录编辑，经 Application 校验后映射为 Unity 场景数据。 |

运行时键以 `BBads/` 为根。`Csv` 和 `SlotShapes` 分别供战斗配置与背包形状加载，`Unit` 指向动态生成的战斗 Prefab。`Items`、`HealthBar`、`projectile`、`characterSpine`、`battleEffect` 和 `KM` 子树保存相应表现资源。

道具按时间进入待放区。自动摆放与手动拖拽使用同一组放置和合并规则。鱼道具给相邻猫道具充能，猫道具召唤单位，敌人按表生成。基地损毁时停止波次，重新进入场景建立新的战斗实例。

trunk 的 SVN 工作副本顶层修订为 **58108**。其加载代码区分独立 Sprite 和图集子图，发布工具对经常一起使用的资源分组。Test 用 Unity 原生图集和本地资源键实现这两点，Spine 继续使用自身图集。参考读取位置为 `E:/projects/trunk/Program/Client/GameApp/Resource/ResourceManager.Sprite.cs` 和 `E:/projects/trunk/tools/LANGUAGE_IMAGE_USAGE.md`。

[配置](../../configuration/BBads/README.md) · [导入设置](art-import.md) · [图集](art-atlas.md) · [分辨率档位](art-quality.md)
