using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using UnityEngine;
using UnityEngine.SceneManagement;
using InputFrame = Test.Application.Protocol.PvpInputFrame;
using PlayerSnapshot = Test.Application.Protocol.PvpPlayerSnapshot;

namespace Test.Client.Combat
{
    internal sealed class PvpPlayerSmoke : MonoBehaviour
    {
        private const string Finished = "Finished";
        private readonly Report report = new Report();
        private string output;
        private PvpClientShowcase client;
        private string endReason;
        private string sessionFailure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartFromCommandLine()
        {
            if (UnityEngine.Application.isEditor ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-pvp-smoke") < 0) return;
            var runner = new GameObject("PvP Player smoke").AddComponent<PvpPlayerSmoke>();
            DontDestroyOnLoad(runner.gameObject);
            _ = runner.RunAsync();
        }

        private async Task RunAsync()
        {
            try
            {
                output = RequiredArgument("-pvp-report");
                report.mode = RequiredArgument("-pvp-smoke");
                report.server = RequiredArgument("-pvp-server");
                report.roomId = RequiredArgument("-pvp-room");
                report.playerId = RequiredArgument("-pvp-player");
                report.unityVersion = UnityEngine.Application.unityVersion;
                report.batchMode = UnityEngine.Application.isBatchMode;
                if (report.mode != PvpClientShowcase.RainPassMode && report.mode != PvpClientShowcase.BBadsMode)
                    throw new ArgumentException("-pvp-smoke expects rain-pass or bbads.");

                UnityEngine.Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                UnityEngine.Application.targetFrameRate = 60;
                if (SceneManager.GetActiveScene().path != CombatShowcase.ScenePath)
                    SceneManager.LoadScene(CombatShowcase.ScenePath, LoadSceneMode.Single);

                CombatShowcase showcase = null;
                await WaitForAsync(() =>
                {
                    if (showcase == null) showcase = FindObjectOfType<CombatShowcase>();
                    if (showcase != null && showcase.Failure != null)
                        throw new InvalidOperationException("CombatShowcase failed to start.", showcase.Failure);
                    return showcase != null && showcase.IsReady;
                }, 30, "CombatShowcase did not become ready.");
                if (!showcase.OpenPvp()) throw new InvalidOperationException("Could not open Online PvP from CombatShowcase.");

                client = showcase.ActivePvp;
                await client.StartSmokeAsync(this, report.server, report.roomId, report.playerId, report.mode);
                if (report.slot < 0) throw new InvalidOperationException(client.ConnectionError ?? "PvP join failed.");

                await WaitForAsync(() => report.finalSnapshot != null || sessionFailure != null, 35,
                    "Timed out waiting for the final PvP snapshot.");
                if (sessionFailure != null) throw new InvalidOperationException(sessionFailure);
                ValidateResult();
                report.passed = true;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
            }
            finally
            {
                if (client != null)
                {
                    var closing = client.LeaveAsync();
                    await Task.WhenAny(closing, Task.Delay(TimeSpan.FromSeconds(3)));
                }
                Finish();
            }
        }

        private static async Task WaitForAsync(Func<bool> completed, float seconds, string timeout)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!completed())
            {
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException(timeout);
                await Task.Yield();
            }
        }

        private static string RequiredArgument(string name)
        {
            string value = RainPassCamp.Argument(name);
            if (string.IsNullOrWhiteSpace(value) || value[0] == '-')
                throw new ArgumentException("Missing value for " + name);
            return value;
        }

        internal void RecordJoin(PvpJoinResponse join)
        {
            report.matchId = join.MatchId;
            report.slot = join.PlayerSlot;
            report.joinEpoch = join.SessionEpoch;
            Debug.Log($"PVP_SMOKE_JOINED mode={report.mode} matchId={report.matchId} slot={report.slot}");
        }

        internal void RecordRoom(PvpRoomMessage room)
        {
            if (room.RoomState == Finished) endReason = room.Reason;
        }

        internal void RecordSentInput() => report.inputsSent++;
        internal void RecordFailure(string message) => sessionFailure = message;

        internal void RecordSnapshot(PvpSnapshotMessage snapshot, PlayerSnapshot[] players, int localSimulationTick)
        {
            var evidence = new Snapshot
            {
                sequence = snapshot.SnapshotSequence,
                serverTick = snapshot.ServerTick,
                localSimulationTick = localSimulationTick,
                stateHash = snapshot.StateHash,
                sessionEpoch = snapshot.SessionEpoch,
                confirmedInputSequence = snapshot.ConfirmedInputSequence,
                roomState = snapshot.RoomState,
                phase = snapshot.Phase,
                peerConnected = snapshot.PeerConnected,
                remainingTicks = snapshot.RemainingTicks,
                cueCount = snapshot.CueCount,
                players = new[] { RecordPlayer(players[0]), RecordPlayer(players[1]) }
            };
            report.snapshots.Add(evidence);
            if (!string.IsNullOrEmpty(evidence.phase) && !report.phases.Contains(evidence.phase))
                report.phases.Add(evidence.phase);
            if (report.finalSnapshot != null || snapshot.RoomState != Finished && snapshot.Phase != Finished) return;

            report.finalSnapshot = evidence;
            report.winner = snapshot.Winner;
            report.endReason = endReason;
        }

        private static Player RecordPlayer(PlayerSnapshot player) => new Player
        {
            playerId = player.PlayerId,
            slot = player.PlayerSlot,
            health = player.Health,
                arrows = player.Arrows,
            posture = player.Posture,
            positionX = player.PositionX,
            positionY = player.PositionY,
            alive = player.Alive,
            ready = player.Ready,
            backpack = player.Backpack ?? Array.Empty<string>()
        };

        internal InputFrame BuildInput(PvpSnapshotMessage snapshot, PlayerSnapshot[] players, int slot, long tick)
        {
            if (report.mode == PvpClientShowcase.BBadsMode)
                return BuildBackpackInput(snapshot, players[slot], slot, tick);

            var local = players[slot];
            var peer = players[1 - slot];
            var direction = new Vector2((float)(peer.PositionX - local.PositionX), (float)(peer.PositionY - local.PositionY));
            var aim = direction.normalized;
            var buttons = PvpButtons.None;
            var movement = Vector2.zero;
            if (slot == 0)
            {
                if (tick < 24)
                    movement = new Vector2(.5f, .5f);
                else if (local.Arrows == Test.Domain.Combat.BowRules.QuiverCapacity)
                    buttons = local.Weapon == (int)PvpWeapon.Bow ? PvpButtons.Attack : PvpButtons.EquipBow;
                else if (local.Weapon == (int)PvpWeapon.Bow)
                    buttons = PvpButtons.EquipSword;
                else if (direction.sqrMagnitude > 2.2f * 2.2f) movement = aim;
                else buttons = peer.Broken ? PvpButtons.Finish : PvpButtons.Attack;
            }
            return new InputFrame
            {
                SimulationTick = tick,
                Buttons = (int)buttons,
                MoveX = Mathf.RoundToInt(movement.x * 1000),
                MoveY = Mathf.RoundToInt(movement.y * 1000),
                AimX = Mathf.RoundToInt(aim.x * 1000),
                AimY = Mathf.RoundToInt(aim.y * 1000)
            };
        }

        private static InputFrame BuildBackpackInput(PvpSnapshotMessage snapshot, PlayerSnapshot local, int slot, long tick)
        {
            if (snapshot.Phase != "Preparing" || local.Ready) return null;
            var placements = local.Backpack ?? Array.Empty<string>();
            int fishColumn = slot + 1;
            if (Array.IndexOf(placements, "Weapon:0:0") < 0)
                return new InputFrame { SimulationTick = tick, Buttons = (int)(PvpButtons.EquipSword | PvpButtons.AttackPressed) };
            if (Array.IndexOf(placements, $"Fish:{fishColumn}:0") < 0)
                return new InputFrame
                {
                    SimulationTick = tick,
                    Buttons = (int)(PvpButtons.EquipBow | PvpButtons.AttackPressed),
                    MoveX = fishColumn * 1000
                };
            return new InputFrame { SimulationTick = tick, Buttons = (int)PvpButtons.Finish };
        }

        private void ValidateResult()
        {
            var final = report.finalSnapshot;
            if (report.winner != 0 || string.IsNullOrEmpty(final.stateHash) || final.confirmedInputSequence == 0 ||
                report.inputsSent == 0 || report.endReason != "Combat" ||
                final.players[report.slot].playerId != report.playerId ||
                string.IsNullOrEmpty(final.players[1 - report.slot].playerId) ||
                final.players[0].health <= 0 || final.players[1].alive)
                throw new InvalidOperationException("Final PvP snapshot did not confirm the scripted two-player win.");

            if (report.mode == PvpClientShowcase.BBadsMode &&
                (!report.phases.Contains("Preparing") || !report.phases.Contains("Countdown") ||
                 !report.phases.Contains("Fighting") || !report.phases.Contains(Finished) ||
                 final.localSimulationTick != 0 ||
                 !HasPreparedBackpack(final.players[0], 1) || !HasPreparedBackpack(final.players[1], 2)))
                throw new InvalidOperationException("BBads snapshot did not confirm both backpacks and all battle phases.");
            if (report.mode == PvpClientShowcase.RainPassMode &&
                (final.localSimulationTick != final.serverTick ||
                 final.players[0].arrows >= Test.Domain.Combat.BowRules.QuiverCapacity ||
                 Math.Abs(final.players[0].positionY) < .01))
                throw new InvalidOperationException("Rain Pass did not confirm movement, bow use and a locally simulated final frame.");
        }

        private static bool HasPreparedBackpack(Player player, int fishColumn) =>
            player.ready && Array.IndexOf(player.backpack, "Weapon:0:0") >= 0 &&
            Array.IndexOf(player.backpack, $"Fish:{fishColumn}:0") >= 0;

        private void Finish()
        {
            try
            {
                if (!string.IsNullOrEmpty(output))
                {
                    string path = Path.GetFullPath(output);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, JsonUtility.ToJson(report, true));
                }
            }
            catch (Exception exception)
            {
                report.passed = false;
                report.error = exception.ToString();
            }

            if (report.passed) Debug.Log($"PVP_SMOKE_PASSED mode={report.mode} matchId={report.matchId} slot={report.slot}");
            else Debug.LogError("PVP_SMOKE_FAILED " + report.error);
            UnityEngine.Application.Quit(report.passed ? 0 : 1);
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string error, mode, server, roomId, playerId, matchId, unityVersion, endReason;
            public bool batchMode;
            public int slot = -1, winner = -1, inputsSent;
            public long joinEpoch;
            public Snapshot finalSnapshot;
            public List<string> phases = new List<string>();
            public List<Snapshot> snapshots = new List<Snapshot>();
        }

        [Serializable]
        private sealed class Snapshot
        {
            public long sequence, serverTick, sessionEpoch, confirmedInputSequence;
            public int localSimulationTick;
            public string stateHash, roomState, phase;
            public bool peerConnected;
            public int remainingTicks, cueCount;
            public Player[] players;
        }

        [Serializable]
        private sealed class Player
        {
            public string playerId;
            public int slot, health, posture, arrows;
            public double positionX, positionY;
            public bool alive, ready;
            public string[] backpack;
        }
    }
}
