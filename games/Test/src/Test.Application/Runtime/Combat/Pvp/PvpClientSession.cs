using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking.Realtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using WireInput = Test.Application.Protocol.PvpInputFrame;
using WirePlayer = Test.Application.Protocol.PvpPlayerSnapshot;
using WireCue = Test.Application.Protocol.PvpCombatCue;

namespace Test.Application.Combat.Pvp
{
    public sealed class PvpClientSession : IAsyncDisposable
    {
        private readonly RealtimeClient realtime;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly TaskCompletionSource<PvpJoinResponse> initialJoin =
            new TaskCompletionSource<PvpJoinResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        private PvpJoinResponse active;
        private Task running;
        private long sequence;
        private long lastSnapshot;
        private RainPassPvpNetworkMatch simulation;
        private string simulationMatchId;
        private bool tournament;
        private PvpTournamentState tournamentState;
        private int simulationTick;
        private int replayedFrames;
        private int disposed;

        public PvpClientSession(Uri websocketUri, IWebSocketTransportFactory transportFactory = null)
        {
            realtime = new RealtimeClient(websocketUri, transportFactory ?? new ClientWebSocketTransportFactory(), new BinaryRealtimeCodec(),
                new ConnectionRecovery(), new RealtimeOptions(16384, maxReconnectAttempts: 0,
                    connectTimeout: TimeSpan.FromSeconds(2), heartbeatInterval: TimeSpan.FromSeconds(2),
                    heartbeatTimeout: TimeSpan.FromSeconds(2)));
        }

        public bool IsConnected => Volatile.Read(ref active)?.Accepted == true && realtime.IsConnected;
        public long SessionEpoch => Volatile.Read(ref active)?.SessionEpoch ?? 0;
        public string RoomId => Volatile.Read(ref active)?.RoomId;
        public string PlayerId => Volatile.Read(ref active)?.PlayerId;
        public int PlayerSlot => Volatile.Read(ref active)?.PlayerSlot ?? -1;
        public string MatchId => Volatile.Read(ref active)?.MatchId;
        public int SimulationTick => Volatile.Read(ref simulationTick);
        public int ReplayedFrames => Volatile.Read(ref replayedFrames);

        public event Action<PvpRoomMessage> RoomChanged;
        public event Action<PvpSnapshotMessage> SnapshotReceived;
        public event Action<WirePlayer> PlayerReceived;
        public event Action<WireCue> CueReceived;
        public event Action<IReadOnlyList<Vector3>> ArrowsReceived;
        public event Action<PvpRecoveryResponse> RecoveryReceived;
        public event Action<PvpMatchResultMessage> ResultReceived;
        public event Action<PvpTournamentState> TournamentChanged;

        public async Task<PvpJoinResponse> JoinAsync(string requestedRoomId, string requestedPlayerId, string mode = "rain-pass",
            CancellationToken cancellationToken = default, string resumeToken = null)
        {
            if (running != null) throw new InvalidOperationException("This PvP session has already been started.");
            if (mode == "rain-pass" || mode == null) simulation = new RainPassPvpNetworkMatch();
            tournament = mode == PvpTournamentRules.Tournament;
            using var cancelInitialJoin = cancellationToken.Register(() =>
            {
                if (!initialJoin.Task.IsCompletedSuccessfully) lifetime.Cancel();
            });
            running = RunAsync(new PvpJoinRequest
            {
                ProtocolVersion = GameProtocol.Version, RoomId = requestedRoomId, PlayerId = requestedPlayerId,
                Mode = mode, ResumeToken = resumeToken
            });
            return await initialJoin.Task.ConfigureAwait(false);
        }

        public Task ReadyTournamentAsync(CancellationToken cancellationToken = default)
        {
            var state = Volatile.Read(ref tournamentState);
            if (!IsConnected || state?.Phase != PvpTournament.AwaitingReady)
                throw new InvalidOperationException("The tournament is not waiting for players to ready up.");
            return SendAsync("tournamentReady", new PvpTournamentReady
            {
                TournamentId = state.TournamentId, TournamentVersion = state.TournamentVersion
            }, cancellationToken);
        }

        public Task SendInputAsync(long simulationTick, PvpButtons buttons, int moveX, int moveY, int aimX, int aimY,
            CancellationToken cancellationToken = default, string expectedMatchId = null)
        {
            var current = Volatile.Read(ref active);
            if (expectedMatchId != null && expectedMatchId != current?.MatchId) return Task.CompletedTask;
            if (current == null || !realtime.IsConnected) throw new RealtimeDisconnectedException("The PvP session is reconnecting.");
            return SendAsync("input", new WireInput
            {
                ProtocolVersion = GameProtocol.Version, RoomId = current.RoomId, MatchId = current.MatchId,
                PlayerId = current.PlayerId, SessionEpoch = current.SessionEpoch, SimulationTick = simulationTick,
                InputSequence = Interlocked.Increment(ref sequence), Buttons = (int)buttons,
                MoveX = moveX, MoveY = moveY, AimX = aimX, AimY = aimY
            }, cancellationToken);
        }

        public async Task LeaveAsync()
        {
            if (!IsConnected) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await SendAsync("leave", null, deadline.Token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            await realtime.DisposeAsync().ConfigureAwait(false);
            if (running != null) await running.ConfigureAwait(false);
            lifetime.Dispose();
        }

        private async Task RunAsync(PvpJoinRequest request)
        {
            var token = lifetime.Token;
            Exception failure = null;
            try
            {
                int attempts = tournament ? 20 : 4;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    if (attempt != 0) await Task.Delay(tournament ? 500 : attempt * 250, token).ConfigureAwait(false);
                    var reply = new TaskCompletionSource<PvpJoinResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task connection = realtime.RunSessionAsync(0, (push, cancellation) =>
                        ApplyAsync(push, reply, cancellation), cancellationToken: token);
                    try
                    {
                        while (!realtime.IsConnected)
                        {
                            if (connection.IsCompleted) await connection.ConfigureAwait(false);
                            await Task.Delay(20, token).ConfigureAwait(false);
                        }
                        request.LastSimulationTick = SimulationTick;
                        request.LastMatchId = simulationMatchId;
                        await SendAsync("join", request, token).ConfigureAwait(false);
                        var join = await AwaitJoinAsync(reply.Task, connection, token).ConfigureAwait(false);
                        initialJoin.TrySetResult(join);
                        if (!join.Accepted)
                        {
                            if (request.ResumeToken != null) ReportRecovery(join.Reason);
                            return;
                        }
                        request.ResumeToken = join.ResumeToken;
                        request.LastSnapshotSequence = Interlocked.Read(ref lastSnapshot);
                        request.LastInputSequence = Interlocked.Read(ref sequence);
                        attempt = 0;
                        await connection.ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException || error is TimeoutException ||
                        error is System.Net.WebSockets.WebSocketException || error is PushRecoveryException ||
                        error is PushApplicationException { InnerException: FrameGapException })
                    {
                        failure = error;
                        if (request.ResumeToken == null) break;
                    }
                    finally
                    {
                        Volatile.Write(ref active, null);
                        await realtime.StopAsync().ConfigureAwait(false);
                        if (connection.IsFaulted) _ = connection.Exception;
                    }
                }
                if (request.ResumeToken != null) ReportRecovery("ReconnectFailed");
                initialJoin.TrySetException(failure ?? new RealtimeDisconnectedException("The PvP connection could not be established."));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { initialJoin.TrySetCanceled(token); }
            catch (Exception error)
            {
                initialJoin.TrySetException(error);
                if (!token.IsCancellationRequested)
                    ReportRecovery(error is PushApplicationException && error.InnerException != null
                        ? error.InnerException.Message : error.Message);
            }
        }

        private void ReportRecovery(string reason) =>
            RecoveryReceived?.Invoke(new PvpRecoveryResponse { Accepted = false, Reason = reason });

        private static async Task<PvpJoinResponse> AwaitJoinAsync(Task<PvpJoinResponse> reply, Task connection, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            var delay = Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
            var completed = await Task.WhenAny(reply, connection, delay).ConfigureAwait(false);
            timeout.Cancel();
            if (completed == reply) return await reply.ConfigureAwait(false);
            if (completed == connection) await connection.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("The PvP room did not reply.");
        }

        private Task SendAsync(string type, object message, CancellationToken token)
        {
            var command = message == null ? new JObject() : JObject.FromObject(message);
            command["type"] = type;
            return realtime.SendAsync(Encoding.UTF8.GetBytes(command.ToString(Formatting.None)), token);
        }

        private Task ApplyAsync(RealtimePush push, TaskCompletionSource<PvpJoinResponse> reply, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var envelope = JObject.Parse(Encoding.UTF8.GetString(push.Payload));
            var message = envelope["message"];
            switch ((string)envelope["type"])
            {
                case "joinResponse":
                    var join = message.ToObject<PvpJoinResponse>();
                    if (join.Accepted) Volatile.Write(ref active, join);
                    reply.TrySetResult(join);
                    break;
                case "tournamentState":
                    ApplyTournament(message.ToObject<PvpTournamentState>());
                    break;
                case "snapshot":
                    if (simulation != null) throw new RealtimeProtocolException("Rain Pass expects input frames.");
                    ApplySnapshot(message);
                    break;
                case "frames":
                    ApplyFrameBatch(message);
                    break;
                case "frameReplay":
                    ApplyFrames(message.ToObject<PvpSimulationFrame[]>(), null);
                    break;
                case "result":
                    if (Volatile.Read(ref active) != null) ResultReceived?.Invoke(message.ToObject<PvpMatchResultMessage>());
                    break;
            }
            return Task.CompletedTask;
        }

        private void ApplyTournament(PvpTournamentState state)
        {
            var current = Volatile.Read(ref active);
            if (!tournament || current == null || state.RoomId != current.RoomId) return;
            var previous = Volatile.Read(ref tournamentState);
            if (previous != null && (previous.TournamentId != state.TournamentId ||
                previous.TournamentVersion > state.TournamentVersion)) return;
            if (state.MatchId != simulationMatchId)
            {
                simulation = state.Mode == PvpTournamentRules.RainPass && state.MatchId != null
                    ? new RainPassPvpNetworkMatch() : null;
                simulationMatchId = state.MatchId;
                Volatile.Write(ref simulationTick, 0);
                Interlocked.Exchange(ref lastSnapshot, 0);
            }
            Volatile.Write(ref active, new PvpJoinResponse
            {
                Accepted = true, ProtocolVersion = current.ProtocolVersion, RoomId = current.RoomId,
                MatchId = state.MatchId, PlayerId = current.PlayerId, PlayerSlot = current.PlayerSlot,
                SessionEpoch = current.SessionEpoch, ResumeToken = current.ResumeToken, RoomState = state.Phase
            });
            Volatile.Write(ref tournamentState, state);
            TournamentChanged?.Invoke(state);
        }

        private void ApplySnapshot(JToken message)
        {
            var header = message["header"].ToObject<PvpSnapshotMessage>();
            var joined = Volatile.Read(ref active);
            if (!IsCurrent(header, joined)) return;

            var players = message["players"].ToObject<WirePlayer[]>();
            var cues = message["cues"].ToObject<WireCue[]>();
            if (players.Length != 2 || cues.Length != header.CueCount)
                throw new RealtimeProtocolException("Incomplete PvP snapshot.");
            var positions = message["arrows"].Select(arrow =>
                new Vector3((float)arrow["x"], (float)arrow["y"], (float)arrow["z"])).ToArray();
            Publish(header, joined, players, cues, positions, (string)message["endReason"]);
        }

        private void ApplyFrameBatch(JToken message)
        {
            var header = message["header"].ToObject<PvpSnapshotMessage>();
            var joined = Volatile.Read(ref active);
            if (!IsCurrent(header, joined)) return;
            var playerIds = message["playerIds"].ToObject<string[]>();
            if (playerIds.Length != 2) throw new RealtimeProtocolException("A Rain Pass room requires two slots.");
            var contacts = new List<PvpNetworkCue>();
            ApplyFrames(message["frames"].ToObject<PvpSimulationFrame[]>(), contacts);
            var local = simulation.Snapshot;
            if (local.Tick != header.ServerTick)
                throw new FrameGapException($"Expected Rain Pass tick {header.ServerTick}, applied {local.Tick}.");
            if (local.StateHash != header.StateHash)
                throw new RealtimeProtocolException($"Rain Pass state differs at tick {local.Tick}.");
            header.StateHash = local.StateHash;
            header.CueCount = contacts.Count;
            var players = new[] { PvpFrameSync.Project(header, local.PlayerOne, playerIds[0]),
                PvpFrameSync.Project(header, local.PlayerTwo, playerIds[1]) };
            var cues = contacts.Select(cue => new WireCue
            {
                ProtocolVersion = header.ProtocolVersion, RoomId = header.RoomId, MatchId = header.MatchId,
                SessionEpoch = header.SessionEpoch, SnapshotSequence = header.SnapshotSequence,
                CueSequence = cue.Sequence, ServerTick = cue.Tick, PlayerId = playerIds[cue.Attacker],
                CueType = cue.Result.ToString(), Value = cue.Target, IsArrow = cue.IsArrow
            }).ToArray();
            Publish(header, joined, players, cues, local.Arrows.Select(arrow => arrow.Position).ToArray(),
                (string)message["endReason"]);
        }

        private void ApplyFrames(PvpSimulationFrame[] frames, List<PvpNetworkCue> contacts)
        {
            if (simulation == null) throw new RealtimeProtocolException("Unexpected Rain Pass input frames.");
            foreach (var frame in frames)
            {
                if (frame.Tick <= SimulationTick) continue;
                if (frame.Tick != SimulationTick + 1)
                    throw new FrameGapException($"Missing Rain Pass input before tick {frame.Tick}.");
                if (frame.Controls?.Length != 2)
                    throw new RealtimeProtocolException($"Rain Pass frame {frame.Tick} requires two inputs.");
                var step = simulation.Advance(PvpFrameSync.Decode(frame.Tick, 0, frame.Controls[0]),
                    PvpFrameSync.Decode(frame.Tick, 1, frame.Controls[1]));
                if (step.Snapshot.Tick != frame.Tick || step.Snapshot.StateHash != frame.StateHash)
                    throw new RealtimeProtocolException($"Rain Pass simulation diverged at tick {frame.Tick}.");
                Volatile.Write(ref simulationTick, frame.Tick);
                if (contacts == null) Interlocked.Increment(ref replayedFrames);
                else contacts.AddRange(step.Cues);
            }
        }

        private bool IsCurrent(PvpSnapshotMessage header, PvpJoinResponse joined) =>
            joined != null && header.MatchId == joined.MatchId && header.SessionEpoch == joined.SessionEpoch &&
            header.SnapshotSequence > Interlocked.Read(ref lastSnapshot);

        private void Publish(PvpSnapshotMessage header, PvpJoinResponse joined, WirePlayer[] players, WireCue[] cues,
            Vector3[] arrowPositions, string endReason)
        {
            Interlocked.Exchange(ref lastSnapshot, header.SnapshotSequence);

            int playerCount = 0;
            foreach (var player in players)
                if (!string.IsNullOrEmpty(player.PlayerId)) playerCount++;
            RoomChanged?.Invoke(new PvpRoomMessage
            {
                ProtocolVersion = GameProtocol.Version, RoomId = header.RoomId, MatchId = header.MatchId,
                SessionEpoch = header.SessionEpoch, ServerTick = header.ServerTick, RoomState = header.RoomState,
                PlayerCount = playerCount, MaxPlayers = 2, PlayerId = joined.PlayerId, PlayerSlot = joined.PlayerSlot,
                Reason = endReason
            });
            SnapshotReceived?.Invoke(header);
            foreach (var player in players) PlayerReceived?.Invoke(player);
            foreach (var cue in cues) CueReceived?.Invoke(cue);

            ArrowsReceived?.Invoke(Array.AsReadOnly(arrowPositions));
        }

        private sealed class FrameGapException : Exception
        {
            internal FrameGapException(string message) : base(message) { }
        }

        private sealed class ConnectionRecovery : IPushRecovery
        {
            public Task<PushRecoveryBatch> RecoverAsync(long afterSequence, CancellationToken cancellationToken)
            {
                if (afterSequence != 0)
                    throw new PushRecoveryException("A connection gap requires reconnecting to restore the input stream or current match state.");
                return Task.FromResult(new PushRecoveryBatch(0, Array.Empty<RealtimePush>()));
            }
        }
    }

    public sealed class PvpMatchResultMessage
    {
        [JsonProperty("tick")] public int Tick { get; set; }
        [JsonProperty("winner")] public int Winner { get; set; }
        [JsonProperty("loser")] public int Loser { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
    }
}
