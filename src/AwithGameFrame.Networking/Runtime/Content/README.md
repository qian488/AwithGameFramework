# 内容发布与更新

`ContentManifest` 描述版本、逻辑 key、相对路径、字节长度与 SHA-256；构造和解析时拒绝路径穿越、重复/冲突路径及非便携文件名。`tools/publish-content.ps1` 从独立源目录复制文件，生成 `content-manifest.json` 并核对输出：

```powershell
powershell -File tools/publish-content.ps1 -SourceDirectory 'content/source' -OutputDirectory 'artifacts/content/v1' -Version '1.0.0'
```

每次发布使用新输出目录。将输出文件和清单放到 HTTP(S) 内容目录；清单的获取、认证和版本选择由应用负责，基础 URI 必须以 `/` 结尾。

```csharp
using AwithGameFrame.Networking.Content;
using AwithGameFrame.Core.Resources;

var manifest = ContentManifest.FromJson(manifestBytes);
await using var updater = new ContentUpdateService(installationDirectory);
var snapshot = await updater.UpdateAsync(manifest, new Uri("https://cdn.example/game/1.0.0/"), token);
await using var resources = new ResourceStore(new FileResourceLoader(snapshot.GetPath),
    cacheOptions: new ResourceCacheOptions(32 * 1024 * 1024));
await resources.PreloadAsync<byte[]>("config/levels.json", token);
await using var scope = new ResourceScope(resources);
var config = await scope.LoadAsync<byte[]>("config/levels.json", token);
```

更新先复用当前版本中 hash/大小一致的已校验文件，其余文件流式下载到独立 staging。截断或取消后的 partial 可用 HTTP Range 续传；服务器忽略 Range 返回 200 时从头重写，206 必须严格匹配长度与范围。全部文件长度和 hash 校验通过才原子切换 active 指针；失败保持当前版本。默认每文件下载超时 60 秒，覆盖正文读取，可在构造时调整。取消、超时、HTTP 错误与校验失败均由调用方处理，不隐式重试整个更新。

`OpenCurrent()` 重新验证已安装版本；无版本返回 null，损坏明确报错。`RollbackAsync()` 验证并原子切回上一版。`ContentSnapshot` 固定版本，更新及回退不改变现存快照的文件；新会话用新快照创建资源 resolver/store，避免同一个 key 在旧会话中突然换内容。框架保留历史版本，不自动清理磁盘；产品应在确认不再持有旧快照后安排清理。同进程相同安装目录的写入串行化，跨进程协调由宿主负责。

这是原始文件分发基础。Unity AssetBundle/Addressables、Godot PCK 的构建、依赖关系、装载与卸载需要专用引擎后端；不能把文件下载成功等同于这些格式的完整更新管线。SHA-256 检查损坏，不验证发布者身份；应用决定可信清单和内容来源。本包不处理代码热更新。
