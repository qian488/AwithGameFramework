using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Networking.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using WireInput = Test.Application.Protocol.PvpInputFrame;
using WirePlayer = Test.Application.Protocol.PvpPlayerSnapshot;
using WireCue = Test.Application.Protocol.PvpCombatCue;

namespace Test.Server.Pvp
{
    internal sealed class PvpRealtimeModule : GameModule
    {
        private const int Rate = PvpCombatConfig.StepsPerSecond;
        private const int InputWindow = 12;
        private const int ReconnectTicks = Rate * 12;
        private const int MaximumMatchTicks = Rate * 180;
        private const int MessageLimit = 16384;
        private const int ReplayBatchSize = 48;
        private const int MaxReplayFrames = ReplayBatchSize * 24;
        private readonly int port;
        private readonly object gate = new object();
        private readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
        private readonly HashSet<Peer> peers = new HashSet<Peer>();
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private static readonly BinaryRealtimeCodec Codec = new BinaryRealtimeCodec();
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private WebApplication web;
        private double elapsed;
        private long clock;
        private long epoch;

        internal PvpRealtimeModule(int port) => this.port = port;
        public override string Id => "Test.pvp-realtime";

        public override async Task StartAsync(GameContext context, CancellationToken token)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            web = builder.Build();
            web.UseWebSockets();
            web.MapGet("/pvp/status", (Func<IResult>)(() => Results.Ok(new { ready = true, protocolVersion = GameProtocol.Version })));
            web.MapGet("/pvp/session", RunPeerAsync);
            await web.StartAsync(token);
        }

        public override async Task StopAsync(GameContext context, CancellationToken token)
        {
            stopping.Cancel();
            lock (gate)
            {
                foreach (var peer in peers) peer.Socket.Abort();
                rooms.Clear();
            }
            if (web == null) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { await web.StopAsync(deadline.Token); }
            finally { await web.DisposeAsync(); }
        }

        public override void Tick(GameContext context, double deltaSeconds)
        {
            lock (gate)
            {
                elapsed += deltaSeconds;
                double step = 1d / Rate;
                while (elapsed + 1e-9 >= step)
                {
                    elapsed -= step;
                    clock++;
                    foreach (var room in rooms.Values.ToArray()) Advance(room);
                }
            }
        }

        private void Advance(Room room)
        {
            if ((room.State == "WaitingForPlayers" || room.State == "Finished") && room.Players.All(seat => seat?.Peer == null))
            {
                rooms.Remove(room.Id);
                return;
            }

            if (room.State != "Running") return;
            var first = room.Players[0];
            var second = room.Players[1];
            if (first.Peer == null || second.Peer == null)
            {
                bool firstExpired = first.Peer == null && clock >= first.ResumeThrough;
                bool secondExpired = second.Peer == null && clock >= second.ResumeThrough;
                if (firstExpired || secondExpired)
                    Finish(room, first.Peer != null ? 0 : second.Peer != null ? 1 : -1, "PlayerDisconnected");
                return;
            }

            int tick = room.Match.Snapshot.Tick + 1;
            var firstControls = Consume(first, tick);
            var secondControls = Consume(second, tick);
            var result = room.Match.Advance(PvpFrameSync.Decode(tick, 0, firstControls), PvpFrameSync.Decode(tick, 1, secondControls));
            if (room.Frames != null)
                room.Frames.Add(new PvpSimulationFrame
                {
                    Tick = tick, Controls = new[] { firstControls, secondControls }, StateHash = result.Snapshot.StateHash
                });
            else
                room.Cues.AddRange(result.Cues);
            if (result.Snapshot.Result != null)
                Finish(room, result.Snapshot.Result.Winner, "Combat");
            else if (tick >= MaximumMatchTicks)
                Finish(room, -1, "TimeLimit");
            else if (tick % 2 == 0 || result.Cues.Count != 0)
                Broadcast(room);
        }

        private static PvpFrameControls Consume(Seat seat, int tick)
        {
            if (seat.Inputs.Remove(tick, out var input))
            {
                seat.Confirmed = input.InputSequence;
                seat.LastApplied = input;
                return Controls(input, (PvpButtons)input.Buttons);
            }

            var prior = seat.LastApplied;
            if (prior != null && tick - prior.SimulationTick <= 6)
                return Controls(prior, (PvpButtons)prior.Buttons & (PvpButtons.Guard | PvpButtons.Finish));
            return new PvpFrameControls();
        }

        private static PvpFrameControls Controls(WireInput input, PvpButtons buttons) => new PvpFrameControls
        {
            Buttons = (int)buttons, MoveX = input.MoveX, MoveY = input.MoveY, AimX = input.AimX, AimY = input.AimY
        };

        private async Task RunPeerAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping.Token);
            var peer = new Peer(socket);
            lock (gate) peers.Add(peer);
            var sending = PumpSendsAsync(peer, lifetime.Token);
            try
            {
                var receiving = PumpReceivesAsync(peer, lifetime.Token);
                try { await await Task.WhenAny(sending, receiving); }
                finally
                {
                    lifetime.Cancel();
                    try { await Task.WhenAll(sending, receiving); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (WebSocketException) { }
            catch (Exception error) when (error is JsonException || error is RealtimeProtocolException)
            {
                Console.Error.WriteLine("PvP invalid message: " + error.Message);
            }
            finally
            {
                lock (gate)
                {
                    Detach(peer, false);
                    peers.Remove(peer);
                }
            }
        }

        private static async Task PumpSendsAsync(Peer peer, CancellationToken token)
        {
            await foreach (var message in peer.Outbound.Reader.ReadAllAsync(token))
                await peer.Socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, token);
        }

        private async Task PumpReceivesAsync(Peer peer, CancellationToken token)
        {
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                using var output = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await peer.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (part.MessageType == WebSocketMessageType.Close) return;
                    if (part.MessageType != WebSocketMessageType.Binary || output.Length + part.Count > MessageLimit)
                        throw new RealtimeProtocolException("Expected bounded binary PvP frames.");
                    output.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);

                var message = Codec.Decode(output.ToArray());
                lock (gate)
                {
                    if (message.Kind == RealtimeMessageKind.Ping)
                        peer.Enqueue(new RealtimeMessage(RealtimeMessageKind.Pong, heartbeatId: message.HeartbeatId));
                    else if (message.Kind == RealtimeMessageKind.Business)
                        Command(peer, message.Payload);
                    else
                        throw new RealtimeProtocolException("Expected Ping or Business.");
                }
            }
        }

        private void Command(Peer peer, byte[] data)
        {
            using var document = JsonDocument.Parse(data);
            var body = document.RootElement;
            if (body.ValueKind != JsonValueKind.Object ||
                !body.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                throw new RealtimeProtocolException("A PvP command type is required.");

            switch (type.GetString())
            {
                case "join": Join(peer, body.Deserialize<PvpJoinRequest>(Json)); break;
                case "input": Input(peer, body.Deserialize<WireInput>(Json)); break;
                case "recovery":
                    if (peer.Room != null) Broadcast(peer.Room);
                    break;
                case "leave": Detach(peer, true); break;
                default: throw new RealtimeProtocolException("Unknown PvP command.");
            }
        }

        private void Join(Peer peer, PvpJoinRequest request)
        {
            string mode = request.Mode ?? "rain-pass";
            string reason = null;
            rooms.TryGetValue(request.RoomId ?? "", out var room);
            Seat seat = null;
            if (request.ProtocolVersion != GameProtocol.Version) reason = "ProtocolVersion";
            else if (!ValidId(request.RoomId) || !ValidId(request.PlayerId)) reason = "InvalidIdentity";
            else if (mode != "rain-pass" && mode != "bbads") reason = "InvalidMode";
            else if (peer.Room != null) reason = "AlreadyJoined";
            else if (!string.IsNullOrEmpty(request.ResumeToken))
            {
                seat = room?.Players.SingleOrDefault(candidate => candidate?.Id == request.PlayerId);
                if (seat == null || seat.ResumeToken != request.ResumeToken || room.Mode != mode)
                    reason = "MatchExpired";
                else if (room.Frames != null && (request.LastSimulationTick < 0 || request.LastSimulationTick > room.Frames.Count ||
                    room.Frames.Count - request.LastSimulationTick > MaxReplayFrames))
                    reason = "FrameHistoryUnavailable";
            }
            else if (room != null && room.Mode != mode) reason = "ModeMismatch";
            else if (room != null && room.Players.Any(candidate => candidate?.Id == request.PlayerId)) reason = "PlayerTaken";
            else if (room != null && (room.State != "WaitingForPlayers" || room.Players.All(candidate => candidate != null))) reason = "RoomFull";
            else if (room == null && rooms.Count >= 64) reason = "RoomLimit";

            if (reason != null)
            {
                peer.Send("joinResponse", new PvpJoinResponse
                {
                    ProtocolVersion = GameProtocol.Version, RoomId = request.RoomId ?? "", MatchId = "",
                    PlayerId = request.PlayerId ?? "", PlayerSlot = -1, RoomState = "", Reason = reason
                });
                return;
            }

            if (room == null)
            {
                room = new Room(request.RoomId, mode);
                rooms.Add(room.Id, room);
            }

            if (seat == null)
            {
                int slot = room.Players[0] == null ? 0 : 1;
                seat = new Seat(request.PlayerId, slot);
                room.Players[slot] = seat;
            }
            else if (seat.Peer != null)
            {
                seat.Peer.Room = null;
                seat.Peer.Seat = null;
                seat.Peer.Socket.Abort();
            }

            seat.Peer = peer;
            seat.Epoch = ++epoch;
            seat.Inputs.Clear();
            seat.LastApplied = null;
            peer.Room = room;
            peer.Seat = seat;
            peer.LastSentFrame = room.Frames != null && !string.IsNullOrEmpty(request.ResumeToken)
                ? request.LastSimulationTick : 0;
            seat.LastQueuedTick = room.Match.Snapshot.Tick;
            if (room.Players.All(candidate => candidate != null) && room.State == "WaitingForPlayers")
                room.State = "Running";

            peer.Send("joinResponse", new PvpJoinResponse
            {
                ProtocolVersion = GameProtocol.Version, RoomId = room.Id, MatchId = room.MatchId,
                PlayerId = seat.Id, PlayerSlot = seat.Slot, SessionEpoch = seat.Epoch,
                SnapshotSequence = room.Sequence, ServerTick = room.Match.Snapshot.Tick,
                RoomState = room.State, Accepted = true, ResumeToken = seat.ResumeToken
            });
            if (room.Frames != null)
            {
                while (peer.LastSentFrame < room.Frames.Count)
                {
                    var batch = TakeFrames(room, peer, ReplayBatchSize);
                    peer.Send("frameReplay", batch);
                }
            }
            Broadcast(room);
        }

        private static bool ValidId(string value) => value != null && value.Length is >= 1 and <= 48 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');

        private static void Input(Peer peer, WireInput input)
        {
            var room = peer.Room;
            var seat = peer.Seat;
            long tick = room?.Match.Snapshot.Tick ?? 0;
            const int buttonMask = (int)(PvpButtons.Attack | PvpButtons.Guard | PvpButtons.Dodge | PvpButtons.Lock |
                PvpButtons.Finish | PvpButtons.EquipSword | PvpButtons.EquipBow | PvpButtons.AttackPressed | PvpButtons.AttackReleased);
            string reason = null;
            if (input.ProtocolVersion != GameProtocol.Version) reason = "ProtocolVersion";
            else if (room == null || seat == null) reason = "SessionNotAttached";
            else if (input.RoomId != room.Id || input.MatchId != room.MatchId || input.PlayerId != seat.Id || input.SessionEpoch != seat.Epoch)
                reason = "SessionMismatch";
            else if (room.State != "Running" || !room.Players.All(candidate => candidate?.Peer != null)) reason = "NotRunning";
            else if (input.InputSequence <= 0 || (input.Buttons & ~buttonMask) != 0 ||
                !ValidMoveX(room, input.MoveX) || !ValidAxis(input.MoveY) || !ValidAxis(input.AimX) || !ValidAxis(input.AimY))
                reason = "InvalidInput";
            else if (input.InputSequence <= seat.LastReceived) reason = "Duplicate";
            else if (input.SimulationTick <= tick) reason = "Late";
            else if (input.SimulationTick > tick + InputWindow) reason = "OutsideInputWindow";
            else if (seat.Inputs.ContainsKey((int)input.SimulationTick)) reason = "Duplicate";
            else if (input.SimulationTick <= seat.LastQueuedTick) reason = "OutOfOrder";
            else
            {
                seat.LastReceived = input.InputSequence;
                seat.LastQueuedTick = input.SimulationTick;
                seat.Inputs.Add((int)input.SimulationTick, input);
            }

            peer.Send("ack", new PvpAckMessage
            {
                ProtocolVersion = GameProtocol.Version, RoomId = room?.Id ?? "", MatchId = room?.MatchId ?? "",
                SessionEpoch = seat?.Epoch ?? 0, SnapshotSequence = room?.Sequence ?? 0, ServerTick = tick,
                InputSequence = input.InputSequence, Accepted = reason == null, Reason = reason
            });
        }

        private static bool ValidAxis(int value) => value >= -1000 && value <= 1000;
        private static bool ValidMoveX(Room room, int value) =>
            room.Mode == "bbads" ? value >= 0 && value <= 2000 : ValidAxis(value);

        private void Detach(Peer peer, bool leave)
        {
            var room = peer.Room;
            var seat = peer.Seat;
            if (room == null || seat?.Peer != peer) return;
            peer.Room = null;
            peer.Seat = null;
            seat.Peer = null;
            seat.Inputs.Clear();
            seat.LastApplied = null;
            seat.LastQueuedTick = room.Match.Snapshot.Tick;
            seat.ResumeThrough = clock + ReconnectTicks;
            if (room.State == "Running" && leave)
                Finish(room, 1 - seat.Slot, "PlayerLeft");
            else if (room.State == "WaitingForPlayers")
                room.Players[seat.Slot] = null;
            Broadcast(room);
        }

        private static void Finish(Room room, int winner, string reason)
        {
            if (room.State == "Finished") return;
            room.State = "Finished";
            room.Winner = winner;
            room.EndReason = reason;
            Broadcast(room);
            foreach (var seat in room.Players)
                seat?.Peer?.Send("result", new { tick = room.Match.Snapshot.Tick, winner, loser = winner < 0 ? -1 : 1 - winner, reason });
        }

        private static void Broadcast(Room room)
        {
            long sequence = ++room.Sequence;
            foreach (var seat in room.Players)
                if (seat?.Peer != null) SendState(room, seat.Peer, room.Cues, sequence);
            room.Cues.Clear();
        }

        private static void SendState(Room room, Peer peer, IReadOnlyList<PvpNetworkCue> cues, long sequence)
        {
            var snapshot = room.Match.Snapshot;
            var seat = peer.Seat;
            var header = new PvpSnapshotMessage
            {
                ProtocolVersion = GameProtocol.Version, RoomId = room.Id, MatchId = room.MatchId,
                SessionEpoch = seat.Epoch, SnapshotSequence = sequence, ServerTick = snapshot.Tick,
                ConfirmedInputSequence = seat.Confirmed, StateHash = snapshot.StateHash, PlayerCount = 2,
                CueCount = cues.Count, RoomState = room.State, Mode = room.Mode, Winner = room.Winner,
                PeerConnected = room.Players[1 - seat.Slot]?.Peer != null,
                RemainingTicks = snapshot.CountdownTicksRemaining, Phase = snapshot.Phase?.ToString()
            };
            if (room.Frames != null)
            {
                peer.Send("frames", new
                {
                    header, frames = TakeFrames(room, peer, ReplayBatchSize),
                    playerIds = room.Players.Select(player => player?.Id ?? "").ToArray(), endReason = room.EndReason
                });
                return;
            }

            var players = new[] { ToWirePlayer(room, seat, snapshot.PlayerOne, sequence), ToWirePlayer(room, seat, snapshot.PlayerTwo, sequence) };
            var events = cues.Select(cue => new WireCue
            {
                ProtocolVersion = GameProtocol.Version, RoomId = room.Id, MatchId = room.MatchId,
                SessionEpoch = seat.Epoch, SnapshotSequence = sequence, CueSequence = cue.Sequence,
                ServerTick = cue.Tick, PlayerId = room.Players[cue.Attacker]?.Id ?? "", CueType = cue.Result.ToString(),
                Value = cue.Target, IsArrow = cue.IsArrow
            }).ToArray();
            peer.Send("snapshot", new
            {
                header,
                players,
                cues = events,
                arrows = snapshot.Arrows.Select(arrow => new { x = arrow.Position.X, y = arrow.Position.Y, z = arrow.Position.Z }).ToArray(),
                endReason = room.EndReason
            });
        }

        private static PvpSimulationFrame[] TakeFrames(Room room, Peer peer, int max)
        {
            int count = Math.Min(room.Frames.Count - peer.LastSentFrame, max);
            var batch = new PvpSimulationFrame[count];
            room.Frames.CopyTo(peer.LastSentFrame, batch, 0, count);
            peer.LastSentFrame += count;
            return batch;
        }

        private static WirePlayer ToWirePlayer(Room room, Seat recipient, PvpNetworkPlayerState state, long sequence)
        {
            return new WirePlayer
            {
                ProtocolVersion = GameProtocol.Version, RoomId = room.Id, MatchId = room.MatchId,
                SessionEpoch = recipient.Epoch, SnapshotSequence = sequence, PlayerId = room.Players[state.PlayerId]?.Id ?? "",
                PlayerSlot = state.PlayerId, PositionX = state.Position.X, PositionY = state.Position.Z,
                Health = (int)Math.Ceiling(state.Health), Posture = state.Posture, Arrows = state.Arrows,
                ActionState = state.ActionState, Alive = state.Alive, FacingX = state.Facing.X, FacingY = state.Facing.Z,
                Weapon = state.Weapon, PhaseProgress = state.PhaseProgress, Guarding = state.Guarding,
                Dodging = state.Dodging, Moving = state.Moving, Broken = state.Broken, LockedTarget = state.LockedTarget,
                Backpack = state.Backpack?.Placements.Select(item => $"{item.Item}:{item.X}:{item.Z}").ToArray(),
                Ready = state.Backpack?.Ready ?? false
            };
        }

        private sealed class Peer
        {
            internal readonly WebSocket Socket;
            internal readonly Channel<byte[]> Outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
            {
                SingleReader = true
            });
            internal Room Room;
            internal Seat Seat;
            internal int LastSentFrame;
            private long sequence;
            internal Peer(WebSocket socket) => Socket = socket;

            internal void Send(string type, object message)
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, message }, Json);
                Enqueue(new RealtimeMessage(RealtimeMessageKind.Push, payload, ++sequence));
            }

            internal void Enqueue(RealtimeMessage message)
            {
                if (!Outbound.Writer.TryWrite(Codec.Encode(message))) Socket.Abort();
            }
        }

        private sealed class Room
        {
            internal readonly string Id;
            internal readonly string Mode;
            internal readonly string MatchId = Guid.NewGuid().ToString("N");
            internal readonly Seat[] Players = new Seat[2];
            internal readonly IPvpNetworkMatch Match;
            internal readonly List<PvpSimulationFrame> Frames;
            internal readonly List<PvpNetworkCue> Cues = new List<PvpNetworkCue>();
            internal string State = "WaitingForPlayers";
            internal string EndReason;
            internal int Winner = -1;
            internal long Sequence;

            internal Room(string id, string mode)
            {
                Id = id;
                Mode = mode;
                Match = mode == "bbads" ? new BBadsPvpNetworkMatch() : new RainPassPvpNetworkMatch();
                if (mode == "rain-pass") Frames = new List<PvpSimulationFrame>();
            }
        }

        private sealed class Seat
        {
            internal readonly string Id;
            internal readonly int Slot;
            internal readonly string ResumeToken = Guid.NewGuid().ToString("N");
            internal readonly Dictionary<int, WireInput> Inputs = new Dictionary<int, WireInput>();
            internal Peer Peer;
            internal WireInput LastApplied;
            internal long Epoch;
            internal long LastReceived;
            internal long LastQueuedTick;
            internal long Confirmed;
            internal long ResumeThrough;

            internal Seat(string id, int slot)
            {
                Id = id;
                Slot = slot;
            }
        }
    }
}
