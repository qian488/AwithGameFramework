# Unity 编辑器自动化

框架的 Unity Editor 程序集提供一组项目内操作。项目选用现代 Unity 适配包时即可使用。走 DLL 工作区接入的项目通过 `tools/game.ps1 sync -Name <游戏名>` 分发同一份 Editor 实现。技能位于仓库的 `.agents/skills`，调用工具位于 `tools`。

## 查找与执行

使用运行中的 Unity 时，在该项目选择 `Tools > AwithGameFrame > Enable Editor Automation`。开关按编辑器会话生效，支持脚本重载后恢复。使用关闭的项目时传入匹配项目版本的 `-EditorPath`。项目外部路径可用 `-ProjectPath` 指定。

```powershell
./tools/unity-editor.ps1 -Action actions
$project = @{ ProjectPath = '<Unity 工程路径>' }
./tools/unity-editor.ps1 @project -Action project.info
./tools/unity-editor.ps1 @project -Action asset.find -Query 't:Prefab'
./tools/unity-editor.ps1 @project -Action scene.inspect
./tools/unity-editor.ps1 @project -Action prefab.place -Asset 'Assets/Prefabs/Marker.prefab' -ObjectName 'Preview' -Position 1,0,0
./tools/unity-editor.ps1 @project -Action object.transform -Target '/Preview' -Rotation 0,0,30
./tools/unity-editor.ps1 @project -Action scene.save
```

关闭项目后的批处理示例使用新的场景路径。目录须已存在。`scene.create` 直接保存新场景，后续的对象、组件和 Prefab 操作需要在同一次调用中传 `-Scene` 与 `-Save`。

```powershell
$editor = '<Unity.exe>'
$scene = 'Assets/Scenes/AutomationDemo.unity'
./tools/unity-editor.ps1 @project -EditorPath $editor -Action scene.create -Scene $scene
./tools/unity-editor.ps1 @project -EditorPath $editor -Action object.create -Scene $scene -Save -ObjectName Marker -Kind Cube
./tools/unity-editor.ps1 @project -EditorPath $editor -Action component.add -Scene $scene -Save -Target '/Marker' -Kind Light
```

## 诊断编辑器

`editor.diagnostics` 适用于实时会话和关闭的工程。

```powershell
./tools/unity-editor.ps1 @project -Action editor.diagnostics -Limit 20
./tools/unity-editor.ps1 @project -Action editor.diagnostics -EditorPath $editor
```

- 实时诊断直接读取 `Library/AwithGameFrameAutomation/diagnostics.json`，编译期间也可读取。`state` 区分会话可用、关闭或缺少当前快照。`compilationState` 为 `not-observed`、`compiling`、`failed` 或 `succeeded`。
- 快照从开启自动化时开始收集警告和错误，并保存最近一次编译的诊断。两组各保留最近 100 条，可用 `-Limit` 缩小返回量。编译消息附文件、行和列。`updatedUtc` 表示采集时间。脚本重载后继续同一会话，关闭或禁用自动化时清理实时快照。
- 批处理诊断运行只读项目探测，报告退出状态、能定位的 C# 编译错误和 `logPath`，支持首次导入失败。普通批处理操作遇到编译失败时也会给出诊断；Unity 已返回操作错误时保留该操作的错误信息。

## 编辑组件字段

以下示例使用场景中 `/Marker` 上的 Unity 原生 Light 组件。激活目标场景，查询组件的序列化路径，再预览并提交同一组修改。

```powershell
$light = @{ Target = '/Marker'; Kind = 'UnityEngine.Light'; Scene = 'Assets/Scenes/AutomationDemo.unity' }
./tools/unity-editor.ps1 @project @light -Action component.inspect
$changes = @{ m_Intensity = 4.5; m_BounceIntensity = 2.5 }
./tools/unity-editor.ps1 @project @light -Action component.set -Properties $changes -Preview
./tools/unity-editor.ps1 @project @light -Action component.set -Properties $changes
./tools/unity-editor.ps1 @project -Action scene.save -Scene $light.Scene
```

`component.inspect` 按 Unity 序列化路径返回类型、值与可写标记。`component.set` 在单个场景组件的一次请求中校验最多 32 个可见字段，支持布尔、32 位整数、单精度浮点数和字符串，包括现有列表元素的标量字段。预览返回旧值和拟写入值，正式修改会读回 Unity 应用后的值。校验失败时整组不提交。实时提交可用一次 Undo 恢复。批处理预览需指定 `-Scene`，正式提交还需指定 `-Save`。

游戏专用工具负责列表增删、资产引用、公式资源与自定义 Inspector 交互等项目工作流。字段写入使用 Unity 序列化 API，具体玩法需要在游戏工程中验证。

## 同场景事务

将操作按顺序放进版本为 `1` 的 JSON 文件。字段名使用 `name`、`target`、`parent`、`asset`、`kind`、`position`、`rotation` 和 `scale`。`component.set` 使用 `properties` 数组，每项包含 `path` 和字符串 `value`。

```json
{
  "version": 1,
  "steps": [
    { "action": "object.create", "name": "Rig", "kind": "Empty" },
    { "action": "prefab.place", "asset": "Assets/Prefabs/Marker.prefab", "name": "Marker", "parent": "/Rig" },
    { "action": "object.transform", "target": "/Rig/Marker", "position": [1, 0, 0] }
  ]
}
```

```powershell
./tools/unity-editor.ps1 @project -Action scene.transaction -Scene $scene -StepsFile ./transaction.json -Preview
./tools/unity-editor.ps1 @project -Action scene.transaction -Scene $scene -StepsFile ./transaction.json
./tools/unity-editor.ps1 @project -Action scene.save -Scene $scene
```

每个事务最多 32 步，可使用 `object.create`、`prefab.place`、`object.transform`、`component.add` 与 `component.set`。后续步骤可以引用同一事务创建的层级路径。实时成功的变更归入一次 Undo，显式保存后写盘。关闭 Unity 后传入 `-EditorPath`，正式应用时加 `-Save`，全部步骤成功后保存一次。

预览在 Unity 中执行步骤并用 Undo 复原，记录旧值、结果以及各步状态。失败时撤销该组场景操作。`*.result` 保留请求 ID、模式、时间和步骤记录。CLI 失败时显示记录路径。回滚范围是这些命令登记到 Unity Undo 的场景数据。项目自定义的编辑器回调仍可执行，因此应在隔离工程验证带回调的组件。恢复干净标记依赖 Unity 的内部接口，当前以实测编辑器版本为准。找不到接口时拒绝在干净场景上启动事务。

## 项目授权

项目可在 `ProjectSettings/AwithEditorAutomation.json` 提交独立的实时与批处理命令白名单。未添加配置的项目沿用会话开关与原有批处理调用方式。配置存在后，空列表拒绝对应模式下的桥接执行。事务需要同时授权 `scene.transaction` 和每一步动作。实时诊断直接读取会话发布的快照。关闭工程的诊断探针使用 `project.info`。

```json
{
  "version": 1,
  "live": ["project.info", "scene.inspect", "scene.transaction", "object.create"],
  "batch": ["project.info", "scene.inspect", "checkpoint.inspect"]
}
```

## 场景与 Prefab 检查点

保存待保护的场景或 Prefab 后先捕获检查点。检查点保存源文件内容、路径、GUID 和 SHA-256，保留在项目 `Library/AwithGameFrameAutomation/Checkpoints`。编辑器重启后能列出和检查。备份需要保留该目录。恢复使用关闭的项目并保留原 `.meta` 文件。需先检查正在被替换的版本，再传入所审阅的当前哈希。目标路径、GUID 或哈希不匹配时拒绝恢复。

```powershell
$saved = (./tools/unity-editor.ps1 @project -Action checkpoint.capture -Asset $scene | ConvertFrom-Json).checkpoint
./tools/unity-editor.ps1 @project -EditorPath $editor -Action checkpoint.list
./tools/unity-editor.ps1 @project -EditorPath $editor -Action checkpoint.inspect -Checkpoint $saved.id
./tools/unity-editor.ps1 @project -EditorPath $editor -Action checkpoint.restore -Checkpoint $saved.id -ExpectedHash '<已审阅的当前哈希>'
```

检查点按单文件恢复，引用到的资产需要单独保护。`component.add -Kind ParticleSystem` 支持在场景对象上添加 Unity 原生粒子组件，再用 `component.inspect` 查找可编辑的标量字段，例如 `playOnAwake`。场景粒子规则、公式、预设和专用创作工具由具体游戏维护。

## 行为与边界

- `scene.inspect` 返回层级路径、本地 Transform、启用状态和组件类型。`-Target` 可以缩小到某个节点。`-Limit` 控制层级、资产或组件字段的返回条数，超过会标记 `truncated`。同级重名路径会报歧义。
- 对象、Prefab、Transform 和白名单组件修改由 Unity 在主线程执行。交互模式使用 Undo，保存由 `scene.save` 显式触发。切换到另一场景前，先在 Unity 中处理已打开场景的未保存修改。请求在 Edit Mode 执行，相关操作针对主场景而非 Prefab Stage。
- 请求、结果与批处理日志保存在目标项目的 `Library/AwithGameFrameAutomation`。工具使用本机文件通信，只接收固定操作和数据。信任边界是目标工程及可写入该工程的本机进程。操作超时后先检查返回状态和场景，避免重复应用已被领取的操作。

## 验证

运行 `./tools/verify-unity-editor-automation.ps1 -EditorPath '<Unity.exe>'`。脚本在 `artifacts/UnityAutomationValidation` 下创建独立 Unity 工程，通过 UPM 导入框架包，并生成验证组件与 Prefab。检查覆盖单条操作、字段预览、事务步骤依赖和失败回滚、实时 Undo、权限拒绝、原生粒子字段落盘，以及场景与 Prefab 的重启恢复和过期哈希拒绝。成功日志包含 `UNITY_EDITOR_AUTOMATION_PASSED`。

追加 `-DiagnosticsOnly` 验证实时日志和编译、关闭工程后的批处理错误定位。成功日志包含 `UNITY_EDITOR_DIAGNOSTICS_PASSED`。

## 项目采用与覆盖

采用标准是在独立游戏工作区接入框架后，所需的操作可以通过仓库技能和命令完成，并能用该项目的场景、资源或任务证据验证结果。当前覆盖按工作流判断。

| 参考项目 | 框架内可用的工作流 | 采用时需要单独评估的上游能力 |
| --- | --- | --- |
| Unity-Skills | 场景和资产检查、对象与 Prefab 实例编辑、组件标量与原生粒子组件、实时与批处理诊断。同场景事务、预览、Undo、操作记录、项目白名单、场景及 Prefab 检查点。 | 材质和动画曲线、UI 布局、专用粒子模块、跨资产事务及项目完整备份。 |
| mattpocock/skills | 设计澄清、规格、分诊、依赖工单、复现、切片交付、证据与评审。本地 GitHub Issue 快照对照、限范围原型和架构改进。 | 远端工单写入、其他跟踪平台和未按本项目需求验收的上游技能。 |

Unity 命令的接入与行为可用本页的独立 Unity 验证复现。开发技能按目标 AI 工具对仓库技能的支持使用，任务验收记录以实际执行证据为准。开发任务步骤见[开发工作流](../../.agents/skills/awith-framework-development/references/workflows.md)。

- [Unity-Skills](https://github.com/Besty0728/Unity-Skills)
- [mattpocock/skills](https://github.com/mattpocock/skills)
