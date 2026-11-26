# Test.Server

.NET 8 进程入口，组合本游戏的 Application 与 `GameHost`。提供 JSON 配置、标准输出 JSONL 日志、固定步长 Tick、Ctrl+C 关闭；Linux/macOS 另外处理 SIGTERM。按配置开放本机探针或委托成绩服务。

启动时读取输出目录的 `server.json`，`TickRate` 范围为 1–240，`DispatchBudgetPerTick` 范围为 1–65536。可用 `--config <路径>` 指定其他文件，路径相对当前工作目录；`--run-for-seconds <正数>` 在指定秒数后正常关闭，便于本地检查。未知参数和非法配置以非零退出码结束。

`Program` 在同一线程创建并驱动宿主，通过同步上下文泵送异步生命周期和 Dispatcher；启动、关闭期间仍持续驱动清理。普通 `await` 回到宿主线程，后台任务需要使用 Dispatcher；不要在模块中用 `ConfigureAwait(false)` 后直接访问线程绑定资源，也不要同步阻塞等待排队工作。

Tick 使用固定秒数并有限追赶；严重卡顿时丢弃积压并写警告，避免无限追赶阻塞关闭。这是进程技术骨架，不是确定性实时联机模拟器。模块必须响应取消并完成清理；进程不会强杀忽略取消的模块。

`TestModule` 是共享组合入口。服务端自己的模块和基础设施写在本项目 `Runtime`，启动前注册；不要把数据库或部署实现反向放入 Domain/Application。生命周期日志含宿主、模块和耗时，外部日志采集与服务管理按实际部署需求接入。

本机联调使用 `OnlineProbePort` 启用 ASP.NET Core 端点。它监听回环地址，提供 `/status`、`/session` 和 `/recovery`。验证脚本自动启动进程并检查心跳、命令、按序推送和断线后补查。使用 Node 22+，传入已构建的 Test.Server DLL 和新报告目录。

```powershell
node games/Test/verify-online.mjs artifacts/bin/Test.Server/Debug/net8.0/Test.Server.dll artifacts/TestOnlineCheck
```

## 委托成绩

设置 `ScorePort` 启用回环监听，并为 `ScoreDirectory` 指定独立存档目录。配置可通过 `--config` 传给 Test.Server。

```json
{
  "ScorePort": 17830,
  "ScoreDirectory": "artifacts/TestScoreLedger"
}
```

客户端经 `/contract-scores/session` 提交成绩，在 `/contract-scores/recovery?after=<序号>` 补查确认。HTTP POST `/contract-scores/submissions` 接受同一协议。每次写盘提交成绩及其确认序号。同一个提交 ID 和相同内容返回原确认，改用该 ID 提交不同成绩时返回冲突。重启后从原存档目录恢复账本。

`DropFirstScoreConfirmation` 是后台验收专用的故障注入开关。它在首次 WebSocket 提交写盘后断开连接，并在该进程退出前暂停补查。正常运行时保持默认值 false。
