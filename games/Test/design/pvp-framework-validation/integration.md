# Test PvP 验证

执行 `powershell -File tools/game.ps1 run-server -Name Test`。两个 Windows 客户端打开 `CombatShowcase`，从选关页进入 `Online PvP`。默认服务器地址为 `http://127.0.0.1:17840`。两端选择相同玩法和房号，各用一个玩家名。

```powershell
dotnet test games/Test/server/Test.Server.Tests/Test.Server.Tests.csproj
& tools/verify-game-unity.ps1 -Name Test -EditorPath '<Unity.exe>' -RunTests -BuildScene 'Assets/Scenes/CombatShowcase.unity' -ReleasePlayer
$server = 'artifacts/bin/Test.Server/Debug/net8.0/Test.Server.dll'
$player = '<Player/Test.exe>'
node games/Test/verify-pvp.mjs $server $player 'artifacts/TestPvpRainPass' rain-pass
node games/Test/verify-pvp.mjs $server $player 'artifacts/TestPvpBBads' bbads
```

脚本后台启动 Test.Server 和两个 Windows Player。报告记录双方席位、终局步、状态哈希、本地模拟步和胜者。人工试玩检查 Rain Pass 的键鼠手感、镜头和音画反馈，以及 BBads 的背包操作和结算可读性。
