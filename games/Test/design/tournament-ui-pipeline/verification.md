# 擂台赛大厅交付与验证

在 DevPanel 选择 Test，打开「UI 原型」及「擂台赛大厅」。切换入场、局间就绪、重连和赛后战报，可以修改尺寸、位置、文本、样式及素材。开发者和 AI 编辑同一份设计源。项目配置指定素材路径、控件绑定、预览样本和输出位置。 [设计源](../../ui-prototypes/tournament.json) · [项目配置](../../ui-prototypes/tournament.profile.json) · [DevPanel](../../../../tools/DevPanel/README.md)

## 导入

```powershell
$editor = '<Unity.exe>'
& tools/import-ui-prototype.ps1 -Name Test -Prototype tournament -EditorPath $editor
& tools/import-ui-prototype.ps1 -Name Test -Prototype tournament -EditorPath $editor -Apply
```

预检在隔离 Unity 中生成候选 Prefab，打印新增、修改、删除的节点和素材，并验证重复导入稳定性。带 `-Apply` 时，预检通过后更新 `Assets/Resources/Tournament/GeneratedView.prefab`。更新已有 Prefab 时保留其 `.meta`。控件使用稳定的节点 ID，游戏接线和手工扩展由外层 `Lobby.prefab` 及其 Test 组件管理。

## 后台验收

```powershell
dotnet build tools/DevPanel/AwithGameFrame.DevPanel.csproj
node games/Test/verify-tournament-prototype.mjs '<DevPanel DLL>' '<Chromium.exe>' artifacts/TournamentPrototypeWeb
& tools/verify-game-unity.ps1 -Name Test -EditorPath $editor -RunTests -BuildScene 'Assets/Scenes/CombatShowcase.unity' -ReleasePlayer -KeepProject
& games/Test/verify-tournament-ui.ps1 -VerificationSummary '<summary.json>' -EditorPath $editor
node games/Test/verify-pvp.mjs '<Test.Server.dll>' '<Player/Test.exe>' artifacts/TournamentNativeFlow tournament split-restart
```

浏览器脚本使用可由 Node 解析的 `playwright-core`，在临时工作区验证保存与进程重启。Unity 脚本先从正式 Lobby Prefab 离屏生成三种分辨率的截图，再检查改稿、重复导入、稳定引用和三种失败输入。失败报告包含对应资源键或节点 ID。使用验证副本结束后可通过仓库的 Unity 验证清理工具清理。 [画面验收](../../../../docs/reference/unity-visual-verification.md)

玩家在 `CombatShowcase → Online PvP → Tournament` 使用相同房间码和不同玩家名进入大厅。局间显示比分、下一局和就绪状态，断线时显示恢复提示，赛后可以展开 BBads 双方背包。人工体验观察实体手柄、焦点和战报滚动。
