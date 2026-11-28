# 双玩法擂台赛验证

运行 `powershell -File tools/game.ps1 run-server -Name Test`，在两个客户端从 `CombatShowcase` 选关页进入 **Online PvP → Tournament**。两人使用相同房间码和不同玩家名。看到两人连接后分别点 **Ready for next round**。先打 Rain Pass，再准备 BBads 背包并进入自动对战。前两局比分相同则另打一局 Rain Pass。完成后查看总比分、各局结果与 BBads 的两份终局背包。复访战报使用同一客户端环境、服务地址、房间码和玩家名，Unity 从本机读取原席位凭据。新赛程使用新房间码。

## 后台复跑

```powershell
dotnet test games/Test/server/Test.Server.Tests/Test.Server.Tests.csproj
& tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -BuildScene 'Assets/Scenes/CombatShowcase.unity' -ReleasePlayer
$server = 'artifacts/bin/Test.Server/Debug/net8.0/Test.Server.dll'
$player = '<Player/Test.exe>'
node games/Test/verify-pvp.mjs $server $player 'artifacts/TournamentSweep' tournament
node games/Test/verify-pvp.mjs $server $player 'artifacts/TournamentRestart' tournament split-restart
```

第一条 Player 路径验证 2 比 0。第二条让双方各赢一局，在 BBads 进行中结束 Test.Server，再从同一存档目录启动。两个隐藏 Windows Player 继续使用原席位，确认 Rain Pass 结果保留、BBads 以新的对局 ID 重赛，然后打完 Rain Pass 决胜局。脚本比对两端的对局 ID、终局步、哈希和本地模拟步，离线复算后清理服务端运行副本和本次存档。Unity 验证脚本默认清理临时项目和缓存。

真实 WebSocket 集成测试覆盖局内补帧、局间重连、BBads 全量快照、过期对局输入、结束后重读战报和弃权。确认丢失用例在接收已落盘结果时中断传输并重新连接，核对该局在恢复后的战报只出现一次。平局计分与决胜局规则由规则用例覆盖。

使用运行服务端时的同一份配置，可以离线复算指定赛程的 Rain Pass 对局。使用默认存档目录时省略 `--config`。命令输出各局终局步和哈希，逐帧不匹配时输出首处分歧并返回非零退出码。

```powershell
dotnet $server --config '<server.json>' --verify-pvp-tournament '<房间码>'
```

## 实现与框架反馈

Domain 计算局序和计分。Application 根据已确认对局生成赛程视图和战报，Rain Pass 记下权威输入、规则指纹和固定初始状态。Test.Server 在同一份版本化存档写入对局结果、比分依据和赛程阶段。存档完成后才推送确认。Unity 按服务端阶段切换玩法并绘制就绪与结算界面。

- `GameHost`、AWRT、`RealtimeClient`、协议生成器和 `VersionedSaveService` 串起完整赛程。实时连接的恢复状态与对局状态各有标识，下一局开跑后旧输入按对局 ID 拒绝。
- Test.Server 在连接重建后先交付当前阶段，再按连接顺序发送补帧或快照。赛程、席位、战报和补发策略由 Test 管理。
- 双 Player 运行期间，Windows 会锁住 Test.Server 加载的程序集，同步外部客户端时实测出现了构建复制重试。后台冒烟脚本改为从自己的运行副本启动 Test.Server，退出时清理该副本。

人工试玩检查局间比分和就绪状态能否一眼区分，从 Rain Pass 切到 BBads 时界面与输入提示是否清楚，以及战报滚动和背包布局的实际观感。 [需求与验收表](brief.md)
