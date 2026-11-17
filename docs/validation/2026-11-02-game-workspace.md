# 游戏工作区与外部构建检查：2026-11-02

源码状态：基于 `03d56ce` 的未提交工作区，包含此前确认的产品方向文档和本次工作区工具、模板与导航调整。环境为 Windows、Windows PowerShell 5.1、.NET SDK 9.0.318。实际游戏客户端没有创建；命令实例和静态分发夹具均位于忽略提交的 `artifacts`。

## 实际检查

| 范围 | 实际结果 |
| --- | --- |
| `powershell -File tools/verify.ps1 -StaticOnly` | 文档链接、公共运行时依赖、Unity 元数据与包路径通过 |
| `dotnet build AwithGameFrame.sln --configuration Release --nologo` | 成功，0 警告、0 错误；未执行 NUnit 或 Headless |
| AgentKit `contracts` | `AGENT_CONTRACTS_PASSED`；现有模块及新增工具配方通过 |
| 外部代码脚手架 | 隔离副本中创建 Unity/Godot 两种清单、外部 Domain/Application/Server 和独立模块；未生成引擎文件 |
| 外部构建 | 两类清单的 Application、框架依赖和服务端 Release 编译通过，0 警告、0 错误 |
| 更新与失败 | 移除项目引用后旧 DLL 被清理；故意引入编译错误时拒绝发布，上一份 `build.json` 与产物保留；重名模块拒绝创建 |
| 游戏模块发现 | 生成的 `game-check-game` 描述符被真实 AgentKit CLI 发现，源码范围仅游戏 `src` 和 `server` |
| 服务端模板 | 启动、有限时长运行与正常关闭通过；另在隔离实例检查异步启动/关闭、Dispatcher 回宿主线程、启动取消回滚及非法参数拒绝 |
| Unity 分发生成器 | 静态输入夹具检查首次/重复同步、GUID 保留、asmdef 的 DLL 引用转换、Newtonsoft 去重、旧文件清理和人工修改/外来文件保护通过 |
| Godot 适配与分发 | 使用真实 Godot.NET.Sdk 4.5.2 编译现有适配器，0 警告、0 错误；静态分发夹具的 props 路径、Private 引用与依赖检查通过，没有分发 GodotSharp |

工作区命令实例位于 `artifacts/workspace-check`，更新/失败与模块发现证据在其 `validation.log`。服务端实例位于 `artifacts/server-template-check`，分发生成器夹具与本地检查脚本位于 `artifacts/WorkspaceClientValidation`。其中用于触发工具分支的客户端标记和占位 DLL 只是静态输入，不能用来证明引擎已经可用。

## 尚未验证与当前限制

没有执行新分发方式的真实 Unity 导入、Godot 原生启动、断点/符号加载、平台导出或操作系统信号关闭。历史样例的引擎验证不替代这些检查。实际客户端由开发者在 `games/<Name>/client/Unity` 或 `client/Godot` 创建后，再进行对应验证。

本轮按当前任务范围执行编译、静态检查和直接命令验证，没有增加测试平台、运行 Demo 或扩展玩法测试。当前客户端分发支持平铺托管 DLL/PDB；原生库、卫星资源和额外内容需要显式接入。配置/协议目录已定义，实际游戏生成输入与基线、服务端通信/存储/部署及其他引擎仍属于后续工作。
