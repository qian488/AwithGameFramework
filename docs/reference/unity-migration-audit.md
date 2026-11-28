# Unity 迁移预检

给 `tools/verify-unity-migration.ps1` 传入 Unity 工程目录和编辑器可执行文件。它复制 `Assets`、`Packages`、`ProjectSettings` 和本地包，在副本里以无图形模式导入和编译。项目副本、Unity 日志和原始报告位于 `artifacts/UnityMigrationAudit` 的独立运行目录。

```powershell
powershell -File tools/verify-unity-migration.ps1 -ProjectPath 'E:/projects/My Unity Project' -EditorPath 'E:/Unity/Editor/Unity.exe'
```

使用 `-OutputPath` 指定汇总 JSON，使用 `-TimeoutSeconds` 调整每个编辑器进程的超时。相对路径以调用者的当前目录为准。报告中的工程路径指向输入目录，资产路径沿用 Unity 的 `Assets/...` 和 `Packages/...` 格式，保留大小写和文件名。

## 对照源工程和目标工程

```powershell
powershell -File tools/verify-unity-migration.ps1 -SourceProjectPath 'E:/projects/Source' -SourceEditorPath 'E:/Unity6/Editor/Unity.exe' -TargetProjectPath 'E:/projects/Target' -EditorPath 'E:/Unity2022/Editor/Unity.exe'
```

两个工程分别使用各自的副本导入，报告按 `source`、`target` 的顺序保存两次结果。省略 `-SourceEditorPath` 时两次使用同一个编辑器。输入工程必须互相独立，运行目录和报告位于工程外。副本保留资产相对位置及 `.meta`，直接 `file:` 包依赖按需复制并重定位。目录链接会在复制前被拒绝。Unity 导入会运行工程和包自带的编辑器代码，适用范围为可信工程。

## 报告和退出码

`schemaVersion: 1` 的汇总包含工程声明版本、实际运行的编辑器版本、已注册包的版本和来源、审计状态、场景与 Prefab 数量及排序后的发现项。每个项目的 `audit` 字段来自编辑器。批处理启动、包解析、编译或扫描中断时，`status` 为 `incomplete`，详细原因可在命令输出所指的 `unity.log` 查找。

| 退出码 | 结果 |
| --- | --- |
| `0` | 全部审计完成且没有发现问题 |
| `2` | 审计完成并记录发现项 |
| `1` | 参数无效或至少一项审计未完成 |

- `missing-script` 来自 Unity 实际加载的场景和 Prefab 层级，包含对象路径和缺失组件数。
- `unresolved-serialized-guid` 指 Unity 无法解析的外部对象引用，包含资产路径、行号、GUID 和文件 ID。扫描覆盖 `Assets/` 下的场景与 Prefab，以及从它们到达的 Unity YAML 文本依赖。
- `missing-asset-dependency` 指 Unity 依赖图或已解析的序列化引用指向缺失文件。`missing-object-reference` 指 Unity 加载组件后仍能识别的断开对象引用。
- `asset-import-failed` 指扫描范围内的场景或 Prefab 在导入后无法加载。

二进制资产依赖 Unity 导入结果和对象引用检查，GUID 和行号来自文本序列化。通过字符串、配置或运行时逻辑加载的资源键需要用对应项目的运行时检查。  [Unity 验证入口](verification.md)

仓库提供无业务依赖的场景与 Prefab 夹具生成器，可在独立工程中调用 `UnityMigrationAuditFixture.Create`。 [夹具源码](../../tools/UnityFixtures/UnityMigrationAuditFixture.cs)
