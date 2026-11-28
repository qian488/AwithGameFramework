# Unity 纹理导入 Profile

项目用 JSON 维护纹理分组、导入设置和单资产例外。Unity 适配包提供预览、应用和报告，可在使用 `com.awithgameframe.unity` 的 Unity 2022.3 工程中直接运行。 [编辑器源码](../../adapters/unity/Editor/Content/UnityArtImportProfileProcessor.cs)

## 配置

以下示例扫描 `Assets/Art`，给界面纹理设置默认大小和 Android 压缩格式。单个图标覆盖过滤模式和 Android 最大尺寸，另一张图保留自己的导入配置。

```json
{
  "schemaVersion": 1,
  "assetRoots": ["Assets/Art"],
  "groups": [
    {
      "id": "interface",
      "pathPrefixes": ["Assets/Art/Interface"],
      "extensions": [".png", ".jpg"],
      "settings": {
        "filterMode": "Bilinear",
        "wrapMode": "Clamp",
        "textureCompression": "Compressed",
        "maxTextureSize": 2048,
        "mipmapEnabled": false,
        "platforms": [
          { "name": "Android", "format": "ASTC_6x6", "maxTextureSize": 1024 }
        ]
      }
    }
  ],
  "exceptions": [
    {
      "assetPath": "Assets/Art/Interface/PixelIcon.png",
      "settings": {
        "filterMode": "Point",
        "platforms": [{ "name": "Android", "maxTextureSize": 512 }]
      }
    },
    { "assetPath": "Assets/Art/Interface/EditorOnly.png", "exclude": true }
  ]
}
```

- `assetRoots` 默认为 `["Assets"]`。填写时应列出现有目录。路径使用以 `Assets/` 开头的 Unity 资产路径或根目录 `Assets`，并使用正斜线。
- 按 `groups` 顺序取首个匹配组。填写多个目录前缀时匹配任意一个，按完整目录边界匹配。填写扩展名时还须匹配其中一个扩展名。目录和扩展名比较忽略大小写。省略或清空任一数组会取消对应条件。
- `exceptions` 使用扫描范围内的精确文件路径，比较时忽略大小写。`exclude: true` 跳过资产。`groupId` 可指定另一组。`settings` 按字段覆盖组设置，也可直接为未分组资产指定设置。例外所指路径必须解析到 `TextureImporter`。
- `settings` 支持 `filterMode`、`wrapMode`、`textureCompression`、`maxTextureSize`、`compressionQuality`、`anisoLevel`、`mipmapEnabled`、`readable`、`alphaIsTransparency` 和 `srgb`。省略字段或填 `null` 会沿用组设置或导入器原值。`false` 和 `0` 是显式设置。枚举使用 Unity 枚举名称。
- `platforms` 支持 `Standalone`、`Android`、`iPhone` 和 `WebGL`。每个平台可设置 `format`、`maxTextureSize`、`compressionQuality` 和 `crunchedCompression`，填写任一项会启用该平台覆盖。例外按平台和字段合并。`overridden: false` 用于清除该平台覆盖，应单独填写。
- `schemaVersion` 必须为 `1`。未知字段会导致整份 profile 被拒绝。纹理类型、Sprite 导入模式及每单位像素数属于受保护设置，提交这些转换字段会在导入前报错。

## 执行

从 Unity 菜单 **Tools → AwithGameFrame → Content → Art Import Profile** 选择 **Preview…** 或 **Apply…**，再选择 profile 和 JSON 报告文件。后台执行使用同一入口。

```powershell
& 'C:/Unity/Editor/Unity.exe' -batchmode -nographics `
  -projectPath 'C:/work/Game' `
  -executeMethod AwithGameFrame.Unity.Editor.Content.UnityArtImportProfileMenu.RunBatch `
  -awith-art-profile 'Assets/Config/art-import.json' `
  -awith-art-profile-mode preview `
  -awith-art-profile-output 'artifacts/art-import-preview.json' `
  -logFile 'C:/work/Game/art-import.log'
```

将模式改为 `apply` 可应用差异。Profile 和报告路径可以是绝对路径，相对路径基于 Unity 项目根目录。批处理成功退出码为 `0`，遇到无效 profile 或资产失败时为 `1`。

## 报告和 Sprite 保护

每项记录路径、所属分组、处理状态和错误信息。已匹配资产的 `before` 和 `expected` 只记录本次管理的设置。平台字段使用 `platforms.Android.format` 这样的键。`applied` 记录 Apply 读回的设置。Preview 只读，`applied` 为空。重复 Apply 时，符合预期的纹理标为 `unchanged`，直接跳过重导入。

Apply 在重导入前后读取 Sprite 切片、Pivot、Border 等序列化几何元数据，并通过 `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` 核对 Sprite 子资产的 GUID 和 local file ID。只有实际比较时才会填写 `spriteGeometryPreserved`。几何、标识或读回设置出现差异会将该资产标为 `failed`，工具会恢复该资产原始 `.meta` 并记录恢复结果。报告里的 `applied` 保留恢复前的读回结果，便于定位。每个资产独立处理，其他成功应用的资产继续保留。
