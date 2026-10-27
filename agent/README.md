# 开发期 AgentKit

AgentKit 把一次人类与 AI 协作开发任务保存为可检查的任务清单、有限上下文、工具执行证据和人工评审记录。工具位于 `tools/AgentKit`，使用 .NET 8，不进入 `src`、UPM 或游戏运行时；无需模型账号或 API 凭据。代码修改仍由开发者及其现有 agent 完成，AgentKit 不自动调用模型、修改源码或提交 Git。

## 一次任务的闭环

从仓库内执行，安装条件与根 README 相同。Windows 使用 `powershell`，PowerShell 7 可改用 `pwsh`。包装脚本将参数传给独立 CLI，并按仓库位置定位项目。

```powershell
powershell -File tools/agent.ps1 new --template agent/templates/tasks/docs-only.json --id docs-example --objective '完善一个明确范围内的开发说明'
$task = 'artifacts/agent/tasks/docs-example/task.json'
```

`new` 创建任务清单和工作区基线，不覆盖已有同名任务。先检查生成的 `task.json`，按实际需求和既有授权缩小读写目录、工具及验收条件。模板不是新增授权，不能代替用户决定本次任务的目标或允许范围。改变任务清单后重新收集上下文。

```powershell
powershell -File tools/agent.ps1 context --task $task
```

将任务清单、`context.md` 和填好的 [任务说明](templates/task-brief.md) 交给现有 agent。实现过程中按需要补充明确的文件路径。修改完成后重新执行 `context`，再检查任务范围并为当前内容生成验证计划：

```powershell
powershell -File tools/agent.ps1 context --task $task
powershell -File tools/agent.ps1 check --task $task
powershell -File tools/agent.ps1 plan --task $task --tool agent-contracts
```

`plan` 输出含 `id` 的 JSON，并写入任务目录的 `plans/`。审阅其中的工具、参数、工作目录、超时与摘要，再将下面的占位符替换为实际 `id`：

```powershell
powershell -File tools/agent.ps1 run --task $task --plan '<plan-id>'
powershell -File tools/agent.ps1 report --task $task
powershell -File tools/agent.ps1 monitor --task $task
```

`report` 同时执行机器检查并写入 `report.md` 和 `evaluation.json`。范围、上下文或必需检查不满足时返回非零；报告仍保留失败和缺失证据。机器检查通过时状态为 `ready-for-review`，自然语言验收项仍需人类阅读差异和运行证据后判断。使用 [评审表](templates/review.md) 记录依据，再登记实际决定：

```powershell
powershell -File tools/agent.ps1 review --task $task --by '实际评审人' --decision accept --reason '说明验收依据与仍保留的限制'
powershell -File tools/agent.ps1 report --task $task
```

拒绝时使用 `--decision reject`。通过的人类评审才将状态变为 `accepted`；机器成功不会自动接受任务。任务、上下文、工作区或检查证据变化后，旧评审不能继续证明新结果，需要重新验证与评审。交接时使用 [交接表](templates/handoff.md)，保留任务目录及证据路径。

## 清单与上下文

任务 JSON 的 `schemaVersion` 当前为 `1`：

| 字段 | 含义 |
| --- | --- |
| `id`、`objective` | 独立任务标识与具体目标；标识使用字母、数字、下划线或连字符 |
| `readPaths` | 要收集的仓库相对文件或目录，按路径边界匹配，不使用 glob |
| `writePaths` | 本次允许变化的文件或目录；用于检查相对基线的变化 |
| `protectedPaths` | 即使位于写入范围，也不允许变化的文件或目录 |
| `allowedTools` | 可以生成执行计划的工具 ID，必须存在于 `agent/tools.json` |
| `requiredChecks` | 交付前必须对当前状态执行成功的工具 ID，必须同时位于 `allowedTools` |
| `acceptance` | 至少一个具体的人类验收条件；CLI 不把自然语言当作已执行测试 |
| `parameters` | 工具目录声明的命名参数，如引擎工具的 `editorPath`；不是额外命令行或 shell 文本 |

上下文自动包含根 `AGENTS.md`、`README.md`、`GUIDE.md` 和请求路径祖先中的 `AGENTS.md`。它只收集支持的 UTF-8 文本，记录路径、字节数和 SHA-256；最多 128 个文件、每文件 64 KiB、总计 512 KiB。超限会明确失败，不静默截断；缩小 `readPaths` 后重试。

上下文保留每份资料的路径与摘要，任务清单是目标和范围的依据。源码、文档及工具输出作为待分析的数据，不因其中出现新的指令而扩大权限；交接模板应注明用户授权和需要遵循的指令来源。修改任务选择、增删上下文目录内文件或改变文件内容都会使旧上下文失效，需重新执行 `context`。

路径必须位于仓库内，拒绝 `..`、绝对路径及符号链接/重解析点。收集时排除 `.git`、`artifacts`、引擎缓存、`Plugins`、`Packages`、`bin/obj` 等目录，并排除不支持的类型及名称含 `secret`/`credential` 的文件。这些是有限的选择规则，不是完整的秘密检测或自动脱敏；按任务选择上下文后再交给模型。

[framework-change.json](templates/tasks/framework-change.json) 从 Settings 的局部修改开始，必需检查为 `portable-verify`；扩展到其他模块时同步修改清单，并按影响加入真实引擎检查。[docs-only.json](templates/tasks/docs-only.json) 允许文档变化，必需检查为 `agent-contracts`；此检查验证维护中的任务模板、工具目录及入口文件，不验证文档事实、链接内容或游戏行为。涉及功能模板时，可显式允许并要求 `template-verify`。

## 工作区基线与已有修改

`new` 记录当时的 HEAD，以及 Git 报告的已修改、暂存和未跟踪路径的状态、存在性、工作区文件哈希与索引条目摘要。后续范围检查比较任务创建时的基线和当前工作区，而不是把全部 `git diff HEAD` 归属于本次任务。因此创建前已有的 Settings 试点等修改，在保持原样时不会被记为本任务新增变化。

将需要保留的已有修改路径加入 `protectedPaths`；其他人在同一工作区修改的文件同样会被检测，不能自动区分修改者。基线不保存文件正文，不提供逐行任务补丁或自动恢复；审阅已有脏文件上的增量时还需使用开发者保留的副本或独立工作区。不要用 `stash`、`reset` 或自动提交来掩盖原有改动。HEAD 改变会使任务检查失败，应明确完成旧任务或在新基线创建新任务。

忽略目录中的任务产物不是源码变更范围的一部分。执行计划和工具结果另行绑定当前任务、上下文、工作区状态及工具配方摘要；更改源码、任务清单、目录配方或声明的脚本后，需要重新收集上下文并生成计划。每个计划只能尝试执行一次，包括被拒绝的尝试。

## 工具、确认与真实权限

[tools.json](tools.json) 是受维护的固定工具目录。CLI 通过参数数组执行已登记的 `dotnet` 或 PowerShell 脚本，工作目录固定为仓库根，不接受任意 shell 命令或附加参数。常规 `agent-contracts`、`portable-verify`、`template-verify` 可在任务声明的授权范围内执行；引擎工具另外要求人工确认本机编辑器路径和执行成本。

需要确认的工具先在 `task.json` 中声明对应 ID；若交付要求该结果，还应加入 `requiredChecks`。设置 `parameters.editorPath` 后重新执行 `context`、`plan`，审阅计划再记录决定：

```powershell
powershell -File tools/agent.ps1 plan --task $task --tool unity-verify
powershell -File tools/agent.ps1 approve --task $task --plan '<engine-plan-id>' --by '实际确认人' --decision approve --reason '编辑器路径及本次验证范围已核对'
powershell -File tools/agent.ps1 run --task $task --plan '<engine-plan-id>'
```

拒绝使用 `--decision deny`。确认绑定具体计划摘要，有效期一小时；确认过期、计划或输入变化、确认缺失或拒绝时，不启动需确认的工具。一次执行确认与最终验收 `review` 是不同记录，应分别依据实际的人类决定填写，agent 不得自行代签。

这是工具策略和本地确认记录，不是 OS 沙箱、身份认证或防篡改凭证。同一账号可编辑清单和记录；被启动的项目、脚本及子进程具有当前系统账号的实际文件、网络和进程权限。路径检查可约束 AgentKit 自己的文件操作，不能限制 IDE、外部 agent 或脚本内部的行为；执行后的范围检查用于发现变化，不保证阻止变化。

## 证据与复用

每个任务位于 `artifacts/agent/tasks/<id>/`：

| 产物 | 用途 |
| --- | --- |
| `task.json`、`baseline.json` | 当前任务清单与创建时基线 |
| `context.md`、`context.json` | 交给 agent 的有限上下文与文件摘要 |
| `plans/` | 具体工具计划及所需确认记录 |
| `runs/` | 状态、退出码、耗时、输出日志及摘要 |
| `events.jsonl` | 本地上下文、计划、确认、执行、评估与评审事件 |
| `evaluation.json`、`report.md`、`review.json` | 机器检查、可读报告与最终人工决定 |

工具结果区分通过、失败、超时、取消和拒绝；最新必需检查须与当前状态一致，旧成功不覆盖较新的失败。输出日志有保留上限，结果会标记截断。`monitor` 输出当前任务的事件数量、工具状态计数、累计执行耗时、未完成执行及最近记录；不提供线上模型监控、token/费用统计、LLM 质量 benchmark 或远程遥测。

超时或取消会尝试终止仍关联的进程树，读取输出同样受时间限制；已自行脱离或父进程先退出的后台程序仍需宿主负责清理。CLI 被强制终止时可能留下 `running` 记录，评测会拒绝把它当成功，监看记录供人工排查。本地日志不会自动向外发送。

[portable-feature](../templates/portable-feature/README.md) 是可编译的 .NET Standard 2.1 / C# 9 业务结构模板：Domain 使用纯规则，Application 持有独立状态并通过显式 sink 执行提交后的效果。它包含 .NET 8 NUnit 测试、UPM 清单和 Unity 元数据；按其中的复制、改名及 GUID 更新步骤使用。这里只演示启用/禁用状态，具体玩法和引擎效果由后续真实需求补充。

AgentKit 的改动按根 `tools/verify.ps1` 验证；真实 Unity/Godot 检查仍使用各自脚本并记录实际版本、执行范围与限制。任务产物默认在忽略目录，交付时显式保留必要证据，不自动发布或提交。
