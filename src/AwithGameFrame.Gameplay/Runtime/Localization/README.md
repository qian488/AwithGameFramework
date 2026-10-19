# 宿主本地化

新应用使用 `LocalizationService` 实例。旧 `LocalizationManager` / `LocalizationAPI` 保持原有静态兼容行为；两者不共享语言、字典或事件。

```csharp
var localization = new LocalizationService(host.Context, fallbackLanguage: "en");
localization.ReplaceDatasets(new[] {
    new LocalizationDataset {
        LanguageCode = "en",
        Entries = new List<LocalizationEntry> {
            new LocalizationEntry { Key = "menu.start", Value = "Start" }
        }
    },
    new LocalizationDataset {
        LanguageCode = "zh-CN",
        Entries = new List<LocalizationEntry> {
            new LocalizationEntry { Key = "menu.start", Value = "开始" }
        }
    }
});
host.AddModule(new LocalizationModule(localization));
using var textBinding = localization.BindText("menu.start", nativeView.SetLabel);
localization.SwitchLanguage("zh-CN");
```

`ReplaceDatasets` 先验证完整目录，再复制并一次替换。回退语言数据必须存在；重复语言、重复/空键、空值都会拒绝，当前目录保持有效。修改传入 DTO 不会改变运行中的字典。模块也接受 `loadAsync` 数据源和依赖 ID，可在资源模块之后异步加载；从工作线程返回的数据通过宿主 Dispatcher 发布。引擎驱动器必须在启动/关闭阶段继续泵送 Dispatcher。

文本逐键查找当前区域语言、父语言，最后查找默认语言及其父语言。例如 `fr-CA` 缺失的键会查 `fr`，再查 `en`。`SwitchLanguage` 要求当前语言或其父语言有目录；默认语言不是任意语言的支持声明。目录替换移除当前语言时自动退回默认语言。缺失键返回调用者提供的 fallback，未提供则返回键本身。`GetFormat` 使用所选区域的数字/日期格式，格式错误会抛出 `FormatException`。

`LanguageChanged` 和 `LocalizationCatalogChanged` 通过此宿主的 `GameContext.Events` 同步发布。订阅可以绑定宿主生命周期；`BindText` 会立即同步，随后响应语言及目录变化，应随原生视图关闭一起释放，模块关闭也会释放全部文本绑定。事件失败发生在状态提交之后，会汇总抛出；在监听器或文本 setter 中重入修改会被拒绝，需通过 Dispatcher 安排后续更新。读取可在任意线程进行，创建及修改必须在宿主线程。

统一用户偏好持久化见 [Settings](../Settings/README.md)。语言目录由应用/内容管线提供，本模块不读取 Unity 或 Godot 资源。
