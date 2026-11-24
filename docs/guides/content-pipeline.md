# 引擎资源构建与内容更新

通用 `ContentUpdateService` 分发文件；引擎构建工具先生成 AssetBundle/PCK 和地址目录，再使用同一套发布、校验下载和版本快照。资源目录与包文件一起发布，运行时从已安装快照读取，避免将新目录和旧资源混用。

## 程序化资产

程序化内容同时支持运行时生成和预生成资产时，使用同一套参数和生成器。建议把流程拆成以下职责：

```text
参数资源 → 确定性生成器 → 运行时结果或预生成资产
                         ↓
            生成器版本、输入哈希、来源标记和 manifest
                         ↓
                 过期检查、清理和重建
```

参数资源保存可维护输入，生成器负责结果，编辑器工具负责预览、烘焙和校验，运行时负责选择生成模式和使用结果。生成器的具体规则、资源类型和资产路径归具体游戏所有，公共框架提供流程约束和验证思路。

`games/Test` 的 Rain Pass 是完整示例。它把环境参数保存为 Unity 资源，运行时可以按固定种子生成环境，也可以读取烘焙 Prefab；烘焙结果包含材质、纹理、来源标记和 manifest。修改参数或生成器版本后，输入哈希使旧资产进入过期状态，清理操作只处理 manifest 登记的输出。

程序化资产的自动检查覆盖输入哈希、生成器版本、来源标记、输出清单、资源引用和可重新实例化。画面观感、材质质量、声音和手感仍由实际引擎运行与人工验收确认。

公共 `AwithGameFrame.Data.Generation.GeneratedAssetManifest` 只保存这些过程的可验证身份。它记录生成器、输入 hash、seed、输出路径/大小/hash 和结果 fingerprint，支持运行时选择或编辑器烘焙后的过期判断。具体生成器和 Unity/Godot 资产仍放在游戏客户端区域。

## Unity AssetBundle

在安装 Unity 适配包的工程中准备 JSON specification。资源路径使用工程内 `Assets/...`，同一个资源只能显式归入一个 bundle；共享材质等依赖应独立分包。场景独立于普通资产分包，并标记 `scene: true`。

```json
{
  "bundles": [
    { "name": "shared", "assetPaths": ["Assets/Content/Material.mat"] },
    { "name": "prefabs", "assetPaths": ["Assets/Content/Actor.prefab"] },
    { "name": "levels", "assetPaths": ["Assets/Content/Level.unity"] }
  ],
  "assets": [
    { "key": "actor", "bundle": "prefabs", "assetPath": "Assets/Content/Actor.prefab" },
    { "key": "level", "bundle": "levels", "assetPath": "Assets/Content/Level.unity", "scene": true }
  ]
}
```

```powershell
powershell -File tools/build-unity-content.ps1 -EditorPath '<Unity.exe>' -ProjectPath '<Unity project>' -SpecificationPath '<spec.json>' -OutputDirectory '<new bundle directory>' -BuildTarget Win64
powershell -File tools/verify-content.ps1 -SourceDirectory '<bundle directory>' -Version 'game-v1' -OutputDirectory '<new verification directory>'
```

构建器使用真实 `BuildPipeline.BuildAssetBundles`，生成 `bundle-catalog.json`，记录平台、Unity 版本、逻辑地址和直接依赖。加载器递归固定依赖，跨 store 共享原生 bundle；目录检查重复地址、缺失依赖和循环。目标平台须与 Player 匹配，Unity 版本兼容性按项目使用的版本验证。

运行时读取 `ContentSnapshot.GetPath("bundle-catalog.json")`，用 `UnityBundleCatalog.FromJson` 解析，再创建 `UnityAssetBundleLoader(catalog, snapshot.GetPath)`，交给 `ResourceStore`。普通资产按目录 key 加载；场景配合 `UnityBundleSceneBackend` 与 `SceneService`，场景 scope 将 bundle 固定到原生卸载结束。

取消时等待原生请求完成并释放资源。先销毁实例，再归还资产租约。最后一个所有者释放时调用 `AssetBundle.Unload(true)`。

使用 Addressables 的项目可接入 [Unity Addressables 资源后端](../../adapters/unity-addressables/README.md)，通过相同的资源租约使用 Catalog 中的资产。

## Godot PCK

资源源目录是独立内容根；其中 `res://` 依赖相对于这个根，构建时一起重写到不可变的 `res://awith-content/<Version>/`。场景/资源容器使用文本 `.tscn/.tres`，不接受无法重写的二进制 `.scn/.res` 源。构建副本移除资源声明中的旧 UID，让隔离 import 按新路径生成依赖标识；普通文本值不受 UID 清理影响。外部资源声明必须有 path，只有 UID 的引用会明确拒绝。内容包不引入脚本或程序集，依赖宿主代码的场景需由项目单独设计兼容约定。

```powershell
powershell -File tools/build-godot-content.ps1 -EditorPath '<Godot .NET console executable>' -SourceDirectory 'samples/Content/Godot' -AddressMapPath 'samples/Content/godot-addresses.json' -OutputDirectory '<new pack directory>' -Version 'game-v1'
powershell -File tools/verify-content.ps1 -SourceDirectory '<pack directory>' -Version 'game-v1' -OutputDirectory '<new verification directory>'
```

地址映射是 `key -> 源目录内相对路径`。工具在隔离工程中执行 import 和 export-pack，原生导出器收集纹理等依赖；生成 `content.pck` 与 `pack-catalog.json`，包含包 SHA-256、地址与 UID 映射。

运行时从快照解析 `GodotPackCatalog.FromJson`，将 `snapshot.GetPath(catalog.PackKey)` 传给 `GodotPackResourceLoader`，再创建 `ResourceStore`。加载器校验哈希、命名空间和 UID 冲突后，以 `replaceFiles:false` 挂载；视图/场景继续使用 `GodotViewFactory`、`GodotSceneBackend`。

**Godot 不支持卸载已挂载的 PCK。** 包挂载和 UID 登记属于进程，归还租约只释放资源及实例。更新版本必须使用新 namespace，旧磁盘包的回收通常在进程重启后执行。当前提供不可变内容版本链路，未提供在线替换同名资源或进程内卸载。

## 发布与验收

`tools/publish-content.ps1` 生成文件大小/SHA-256 清单，`ContentUpdateService` 支持校验、增量复用、续传和原子激活。`tools/verify-content.ps1` 先发布，再通过临时 localhost HTTP 服务实际下载、安装并检查当前快照，输出 `Install-report.json`。此工具用于验收；生产 CDN、发布者认证和签名密钥由项目接入。

双引擎验证脚本均自动构建内容包、发布、通过 HTTP 安装，并将安装目录交给真实引擎。Unity 断言覆盖共享依赖、跨 store 引用、bundle 场景与最后卸载；Godot 断言覆盖导入纹理、外部依赖、PackedScene/Control 和实例释放。完整命令与实测范围见 [验证说明](../reference/verification.md)。
