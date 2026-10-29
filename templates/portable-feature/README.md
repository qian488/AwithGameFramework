# Portable feature 模板

这是可复制的最小业务结构，目标为 .NET Standard 2.1 / C# 9。它只演示启用/禁用状态，没有具体玩法，也不依赖框架运行时或引擎。`.csproj` 与 Unity asmdef 仅编译同一份 `Runtime/` 源码，`noEngineReferences=true`。

```text
Runtime/
  Domain/        普通状态 DTO、纯规则
  Application/   状态所有权、显式副作用接口与命令服务
Tests~/          .NET 8 NUnit 测试；Unity 忽略此目录
```

`FeatureRules.SetEnabled` 不修改输入，每次返回独立候选，包括值未变化的情况。`FeatureState` 有意保留为可变 DTO；增加集合或嵌套数据后，复制规则必须同时深复制，并补充嵌套数据隔离测试。规则没有文件、设备、时间、事件或静态状态依赖。

`FeatureService` 复制初值、返回独立 `Snapshot`，先提交候选，再调用构造时显式传入的 `IFeatureStateSink.OnStateChanged`。sink 接收前后状态的独立副本，可以将它们转成宿主事件、设备操作或其他应用效果。服务不查找全局事件总线，不隐式保存。初值不会通知，相同值不会重复通知。

sink 抛错会直接传给命令调用方，状态仍已提交；再次设置相同值不会重试失败的通知。调用方应观察错误并明确选择恢复策略，不能把异常当作“状态未变化”。此示例不提供外部副作用回滚、持久化事务或自动重试。服务用于串行调用，不提供线程安全或自动调度；sink 可读取快照，重入修改会被拒绝，宿主应排队后续命令。

```csharp
using AwithGameFrame.FeatureTemplate.Application;
using AwithGameFrame.FeatureTemplate.Domain;

// 应用提供 IFeatureStateSink 实现；规则计算不需要该依赖。
var initial = new FeatureState { IsEnabled = false };
var preview = FeatureRules.SetEnabled(initial, true); // initial 保持 false
var feature = new FeatureService(initial, applicationStateSink);
feature.SetEnabled(preview.IsEnabled); // 提交，然后通知显式 sink
```

## 复制与改名

1. 将整个目录复制到本仓库的 `src/AwithGameFrame.YourFeature`，保留 `Runtime/` 为唯一运行时源码目录。不要把模板自身加入运行时依赖。它目前没有加入根 solution，复制后按实际需求注册项目和验证入口。
2. 重命名 `AwithGameFrame.FeatureTemplate.csproj`、`Runtime/AwithGameFrame.FeatureTemplate.asmdef`、`Tests~/AwithGameFrame.FeatureTemplate.Tests.csproj`，同时重命名对应 `.meta` 文件。将命名空间、asmdef 的 `name/rootNamespace`、测试的 `ProjectReference` 和文档中的 `AwithGameFrame.FeatureTemplate` 改为新名称。
3. 修改 `package.json` 的 `name`（例如 `com.awithgameframe.your-feature`）、`displayName`、版本和描述。按实际领域重命名 `Feature*` 类型；新增依赖须同时维护 `.csproj`、asmdef、UPM 清单，并遵守根 `AGENTS.md` 的依赖方向。
4. **复制出新包后，为每个 `.meta` 生成新的唯一 `guid`**；模板和新包不能共享 GUID。此模板没有序列化资源或按 GUID 引用，更新各 `.meta` 的 `guid:` 即可。已有资源的移动仍须保留原 GUID，不能使用复制新包的规则处理移动。
5. 更新 `module.json` 的唯一 `id`、职责、所有路径、模块依赖和验证工具 ID。原模板路径和 `portable-feature-template` ID 不能留在新包；入口文件与 README 必须位于新模块目录内。通过 `powershell -File tools/agent.ps1 modules --id <new-id>` 和 `contracts` 检查清单。字段定义见 [AgentKit](../../agent/README.md)。
6. 在宿主/适配层实现 `IFeatureStateSink`，显式构造服务。原生对象、路径、设备与异步资源生命周期留在宿主，不加入 Domain。需要宿主模块时由调用方组合，不要求这个最小模板预设模块或全局单例。
7. 验证复制后项目和测试，再将新功能接入根验证脚本。不要只更改命名空间而忘记测试引用、程序集名称或包名称。

在本仓库，父级 `Directory.Build.props` 将运行时和测试构建输出集中到 `artifacts`，本模板不覆盖输出路径。复制到其他仓库时，应提供同类集中输出配置，避免 `bin/obj` 被 Unity 包导入。运行时项目关闭 SDK 默认源码 glob，仅包含 `Runtime/**/*.cs`，因此不会编译 `Tests~` 中的 NUnit 源码。

Unity 可导入的 Runtime 目录、子目录、源码、asmdef、清单、项目和此文档均带 `.meta`。`Tests~` 及其内部文件按 Unity 的忽略目录规则不提供 `.meta`，也没有测试 asmdef；这里的测试由 .NET 执行。

## 验证

```powershell
dotnet build templates/portable-feature/AwithGameFrame.FeatureTemplate.csproj
dotnet test "templates/portable-feature/Tests~/AwithGameFrame.FeatureTemplate.Tests.csproj"
```

测试验证输入/输出与两服务实例隔离、初值/快照隔离、通知数据被修改后的隔离、幂等命令不重复通知、sink 可读取已提交状态、通知失败保留提交并传播原异常，以及重入失败后服务仍可继续使用。它们验证普通 C# 语义，不代表 Unity 或 Godot 原生后端已完成接入。
