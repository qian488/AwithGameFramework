# 输入动作

`AwithGameFrame.Gameplay.Input` 将普通字符串 control 映射到 action，以 `InputActionEvent` 发布按下、每帧保持和抬起，以及 `[-1, 1]` 内的 float 值。服务使用显式传入的 Core `EventBus`，没有静态输入状态。

```csharp
var input = new InputService(host.Events);
var game = input.CreateContext("game");
game.Bind("jump", "keyboard/space");
game.Bind("move-left", "keyboard/a", scale: -1);
host.AddModule(new InputModule(input, nativeBackend));
using var subscription = host.Events.Subscribe<InputActionEvent>(HandleAction);
// game.Dispose() 释放这个上下文；InputModule 在宿主关闭时释放整个服务。
```

每个 action 在一个 context 中绑定一个 control；多个 context 可分别绑定相同 control。`Rebind` 默认拒绝同 context 的 control 冲突，`ReplaceExisting` 显式解除冲突 action 的绑定。替换绑定前会发布旧 action 的抬起事件。绑定支持有限、非零、绝对值不超过 1 的 scale，以及 `[0, 1)` 的 deadZone；设备值先限制到 `[-1, 1]`，通过 deadZone 后乘 scale。

高 priority 先处理，相同 priority 后创建的 context 优先。非模态 context 消费本帧有效的已绑定 control，低层不会重复收到；模态 context 阻断所有低层输入，即使它自身没有绑定。被阻断的已按下 action 会收到抬起。关闭或停用 context、服务停用、失焦均补发抬起，避免粘键；恢复后仍物理按住的 control 会重新产生按下。

`Poll(IInputBackend)` 读取焦点与本帧所有已绑定 control；`ProcessFrame(IReadOnlyDictionary<string, float>)` 供工具、回放或测试提交完整帧，未提供的 control 为 0。整帧先校验有限数值再修改状态。原生映射不属于通用层：Unity 后端映射 KeyCode/鼠标/旧 Input axis，Godot 后端映射物理键/鼠标/joy button/axis。默认两端提供 `keyboard/space`、`enter`、`escape`、`w/a/s/d` 与 `mouse/left/right`。

`InputUpdateResult.Events/Errors` 可观察路由与监听器失败，模块自动 `ThrowIfFailed`。直接调用 SetEnabled/SetFocused 的调用方也需观察返回结果；context 的 Bind/Rebind/SetEnabled/Dispose 在提交变更并通知全部事件后聚合监听器错误。

所有调用属于创建线程，监听器不能重入修改输入服务，应通过宿主 Dispatcher 安排下一帧修改。InputModule 在 host Tick 中 poll，暂停时释放并停用，恢复时还原暂停前的启用状态。焦点变化由 native backend 的 IsFocused 在下一次 poll 接入，宿主也可主动 SetFocused。服务不调度工作线程；`CaptureBindings` / `ApplyBindings` 提供普通绑定快照，统一设置保存恢复见 [UserSettingsService](../Settings/README.md)。

传入 `IInputRouter`，例如 `new InputService(host.Events, new ViewInputRouter(views))`，使 UI 优先处理动作，未消费的事件才广播给玩法。`ViewInput.Payload` 是原 `InputActionEvent`。已交付玩法的持键被 UI 接管时合成 Released，并抑制到物理释放；弹窗关闭后不会由同一次按住再触发 Pressed/Held。路由内视图变更通过 Dispatcher 排队。

验证：`dotnet test tests/AwithGameFrame.Core.Tests/AwithGameFrame.Core.Tests.csproj --filter FullyQualifiedName~InputServiceTests`。
