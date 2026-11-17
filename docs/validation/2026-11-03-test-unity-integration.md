# Test Unity 工程接入：2026-11-03

基于 `03d56ce` 的未提交工作区，保留上一批工作区工具与产品方向改动，接入开发者新加入的 Test 工程。实际编辑器为 **Unity 2022.3.16f1 (d2c21f0ef2f1)**，与项目记录版本一致；外部编译使用本机 .NET SDK。

## 工程整理与接线

原 Unity 工程直接位于 `games/Test`，现在整体移动到 `games/Test/client/Unity`；缓存、用户设置和原 IDE 文件一起保留，继续由 Git 忽略。移动前后对原 Assets、Packages、ProjectSettings 中的 27 份文件逐一比较 SHA256，内容均一致；移动本身不修改资源 GUID。基线保存在本机 `artifacts/test-client-before-move.json`。

随后通过 `game.ps1 new` 的模板补齐 `games/Test` 外部工作区：Domain、Application、.NET Server、配置/协议目录、`game.json` 和 `module.json`。框架源仍在公共 `src`，没有添加玩法。工程已采用 .NET Standard 2.1 API 兼容级别。

新增客户端 `Test.Client.asmdef` 与 `TestBootstrap`，注册外部 `TestModule`。在保留原场景及其 GUID 的基础上，为 SampleScene 添加 GameHost 对象，并将该场景加入 Build Settings。TMP 及其他已有包继续由客户端维护。

## 实际检查

| 检查 | 结果 |
| --- | --- |
| `powershell -File tools/game.ps1 build -Name Test -Configuration Debug` | 外部框架、游戏程序集和服务端成功编译，0 警告、0 错误 |
| `powershell -File tools/game.ps1 sync -Name Test -Configuration Debug` | 写入客户端嵌入式 UPM 生成目录，DLL/PDB 与适配源码接入成功 |
| Unity batch import | 实际 Test 工程原生导入与脚本编译成功，进程退出码 0，没有 C# 编译错误；`Test.Client`、Unity Engine/Extensions/Editor 适配程序集均实际生成 |
| 导入后再次 `sync` | 所有权与哈希检查通过，未出现导入元数据导致更新失败 |
| `powershell -File tools/verify.ps1 -StaticOnly` | 当前文档、公共依赖、包清单与 Unity 元数据检查通过 |
| AgentKit `modules --id game-test` | 真实发现游戏模块；上下文源码范围为游戏 `src` 和 `server` |

实际 Unity 命令：

```powershell
& 'E:/software/UnityVersion/Unity 2022.3.16f1/Editor/Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'E:/projects/AwithGameFramework/games/Test/client/Unity' `
  -logFile 'E:/projects/AwithGameFramework/artifacts/test-unity-import.log'
```

日志中包含本机许可证客户端签名/联网更新诊断以及退出时的 Curl 中止信息；没有阻止这次导入、编译和正常退出。该结果只证明本次编辑器编译，不证明许可证 CI、Player 构建或远端环境可用。

## 未执行项

没有运行 Demo、PlayMode、NUnit、Player 构建、目标设备、断点/符号加载或 Godot 验证。SampleScene 的启动接线已经加入，但本次没有进入运行模式，不能将编译成功写成模块运行验收。Test 仍没有具体游戏规则、界面、联网业务或生产部署。
