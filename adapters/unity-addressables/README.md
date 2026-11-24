# AwithGameFrame Unity Addressables

这是 Unity Addressables 的可选资源后端。它把 Addressables 的异步句柄和释放操作接入通用 `ResourceStore`，调用方继续使用逻辑 key、资源类型和租约，不直接持有 Addressables 句柄。

本包通过 UPM 引用 Addressables 1.21.21。使用 DLL 工作区时，在客户端包清单添加 `com.unity.addressables`，再执行 `game.ps1 sync`。同步将适配源码写入生成包并引用现有框架 DLL。

```csharp
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Unity.Addressables;

var resources = new ResourceStore(new AddressablesResourceLoader(), host.Dispatcher);
// Register a ResourceModule or dispose the store when the host shuts down.
var lease = await resources.LoadAsync<GameObject>("actors/player");
// Hold the lease while any instance relies on the asset.
```

`AddressablesResourceLoader` 的释放由 `ResourceStore` 租约负责。取消会停止调用方等待，原生 Addressables 请求完成后仍会被释放，避免遗留句柄。资源实例必须先销毁，再归还租约。

`AddressablesSceneBackend` 可接入 `SceneService`。它用 Additive 模式加载，交由服务激活和卸载。卸载服务拥有的场景会归还对应句柄，并保留启动场景。

`AddressablesCatalog.LoadAsync` 加载外部 Catalog。为每个版本创建各自的资源存储和场景后端。新版本加载成功后，由游戏选择使用它的时机。保留旧版本的存储和租约可继续读旧资产，回退时使用旧 Catalog。Catalog 并存时应在 Addressables 构建设置启用唯一 Bundle ID。

使用 [verify-unity-addressables.ps1](../../tools/verify-unity-addressables.ps1) 在隔离工程中构建 Addressables 内容并检查资产租约、HTTP Catalog 和场景生命周期。传入 `-Name Test` 验证 DLL 工作区路径。
