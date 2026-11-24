# Unity 编辑器自动化

Unity 适配包在项目内提供编辑器操作。DLL 工作区通过 `tools/game.ps1 sync -Name <游戏名>` 更新同一份实现。 [Unity 操作技能](../../.agents/skills/awith-unity-editor/SKILL.md)

## 查找与执行

1. 选择目标工程。仓库游戏使用 `-Name`，其他工程使用 `-ProjectPath`。
2. 选择运行方式。Unity 已打开时，在目标工程启用 `Tools > AwithGameFrame > Enable Editor Automation`。Unity 关闭时，传入与 `ProjectVersion.txt` 对应的 `-EditorPath`，脚本在后台执行。
3. 用 `project.info` 核对返回的工程路径，再选择操作。用 `-Action actions` 查命令和参数。

```powershell
./tools/unity-editor.ps1 -Action actions
$project = @{ ProjectPath = '<Unity 工程路径>' }
./tools/unity-editor.ps1 @project -Action project.info
./tools/unity-editor.ps1 @project -Action asset.find -Query 't:Prefab'
./tools/unity-editor.ps1 @project -Action scene.inspect
```

后台检查额外传 `-EditorPath '<Unity.exe>'`。检查指定场景时传 `-Scene`。编译或连接失败时先读诊断。 [编辑器诊断](#诊断编辑器)

### 实时编辑与保存

以下示例在已打开的目标场景放置 Prefab，修改 `/Preview`，再保存。先用 `asset.find` 确认 Prefab 路径，并用 `scene.inspect` 核对层级目标。

```powershell
./tools/unity-editor.ps1 @project -Action prefab.place -Asset 'Assets/Prefabs/Marker.prefab' -ObjectName 'Preview' -Position 1,0,0
./tools/unity-editor.ps1 @project -Action object.transform -Target '/Preview' -Rotation 0,0,30
./tools/unity-editor.ps1 @project -Action scene.save
```

### 后台编辑与保存

下面使用已有目录中的新场景路径。`scene.create` 直接保存新场景。之后每条对象、组件和 Prefab 修改命令都需要传 `-Scene` 与 `-Save`。

```powershell
$editor = '<Unity.exe>'
$scene = 'Assets/Scenes/AutomationDemo.unity'
./tools/unity-editor.ps1 @project -EditorPath $editor -Action scene.create -Scene $scene
./tools/unity-editor.ps1 @project -EditorPath $editor -Action object.create -Scene $scene -Save -ObjectName Marker -Kind Cube
./tools/unity-editor.ps1 @project -EditorPath $editor -Action component.add -Scene $scene -Save -Target '/Marker' -Kind Light
```

## 检查 Prefab

`prefab.inspect -Asset <Assets/...prefab>` 读取 Prefab 的 GUID、类型、层级、组件和依赖路径。使用 `-Limit` 限制层级和依赖各自的返回数量，结果超限时会标记 `truncated`。资产通过 AssetDatabase 读取，场景和 Prefab 文件保持原状。

`scene.health -Scene <Assets/...unity>` 检查组件丢失脚本和可见序列化字段中的断引用，并记录对象数与有效相机数。结果包含出问题的对象层级、组件和属性路径。有效相机数可用于检查预配置相机的场景，运行时生成相机的场景可在 Player 检查。

## 诊断编辑器

`editor.diagnostics` 适用于实时会话和关闭的工程。

```powershell
./tools/unity-editor.ps1 @project -Action editor.diagnostics -Limit 20
./tools/unity-editor.ps1 @project -Action editor.diagnostics -EditorPath $editor
```

- 实时诊断直接读取 `Library/AwithGameFrameAutomation/diagnostics.json`，编译期间也可读取。`state` 区分会话可用、关闭或缺少当前快照。`compilationState` 为 `not-observed`、`compiling`、`failed` 或 `succeeded`。
- 快照从开启自动化时开始收集警告和错误，并保存最近一次编译的诊断。两组各保留最近 100 条，可用 `-Limit` 缩小返回量。编译消息附文件、行和列。`updatedUtc` 表示采集时间。脚本重载后继续同一会话，关闭或禁用自动化时清理实时快照。
- 批处理诊断运行只读项目探测，报告退出状态、能定位的 C# 编译错误和 `logPath`，支持首次导入失败。普通批处理操作遇到编译失败时也会给出诊断；Unity 已返回操作错误时保留该操作的错误信息。

## 操作审计

每次编辑器命令完成后，框架会在 `Library/AwithGameFrameAutomation/audit.jsonl` 追加一条结构化记录。记录包含请求 ID、动作、实时或批处理模式、预览和保存标记、场景、资源、目标、事务步骤数、结果状态、错误和对应的结果文件名。失败、策略拒绝和预览也会记录，便于区分“请求已到达编辑器”和“修改已经落盘”。

```powershell
./tools/unity-editor.ps1 @project -Action audit.list -Limit 50
```

`audit.list` 只读取最近记录。具体字段变化和事务每一步仍在对应的 `*.result` 文件中，审计记录通过 `id` 和 `resultFile` 关联。审计日志属于目标 Unity 工程的开发期状态，不进入游戏运行时资源和发布包。

## 性能快照

`performance.inspect` 提供一次编辑器状态快照，包含当前 Unity 版本、场景脏状态、根对象数、工程内对象数、组件数，以及 Unity Profiler 可读取的总分配内存、总保留内存、未使用保留内存、Mono 已用内存和 Mono 堆大小。

```powershell
./tools/unity-editor.ps1 @project -EditorPath $editor -Action performance.inspect
```

快照适合后台导入和场景验证中的明显异常检查，也可以作为项目自己的回归报告输入。它是单次采样，不替代目标设备上的帧时间、持续运行、功耗和温升测试。

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
  "batch": ["project.info", "scene.inspect", "audit.list", "performance.inspect", "checkpoint.inspect"]
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

运行 `./tools/verify-unity-editor-automation.ps1 -EditorPath '<Unity.exe>'`。脚本在 `artifacts/UnityAutomationValidation` 下创建独立 Unity 工程，通过 UPM 导入框架包，并生成验证组件与 Prefab。后台检查覆盖单条操作、字段预览、事务步骤依赖、权限拒绝、原生粒子字段落盘，以及场景与 Prefab 的重启恢复和过期哈希拒绝。成功日志包含 `UNITY_EDITOR_AUTOMATION_BATCH_PASSED`。

在桌面可用的时段追加 `-RunLiveEditor`，检查实时事务回滚、Undo 和未保存状态，成功日志包含 `UNITY_EDITOR_AUTOMATION_PASSED`。组合 `-RunLiveEditor -DiagnosticsOnly` 验证实时日志和编译、关闭工程后的批处理错误定位，成功日志包含 `UNITY_EDITOR_DIAGNOSTICS_PASSED`。

## 项目采用与覆盖

用 `-Action actions` 查当前项目可调用的操作。事务范围是一个场景，检查点按单个场景或 Prefab 资产保存。成组操作的预览、执行结果和恢复入口沿用 Unity-Skills 的文档组织思路。 [Unity-Skills 操作与治理说明](https://github.com/Besty0728/Unity-Skills/blob/main/docs/OPERATING_MODES.md) · [本仓库实测记录](../validation/latest.md)
