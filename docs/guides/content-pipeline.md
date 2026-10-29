# 引擎资源构建与内容更新

通用 `ContentUpdateService` 分发文件；引擎构建工具先生成 AssetBundle/PCK 和地址目录，再使用同一套发布、校验下载和版本快照。资源目录与包文件一起发布，运行时从已安装快照读取，避免将新目录和旧资源混用。

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

原生异步请求无法中断，取消时等待迟到结果再释放。实例必须先销毁，再归还资产租约；最后所有者释放时调用 `AssetBundle.Unload(true)`，继续使用已归还的原生资产会失效。框架不包含 Addressables 和代码热更新。

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
