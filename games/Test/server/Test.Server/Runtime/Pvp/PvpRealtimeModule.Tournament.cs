using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using SaveSerializer = AwithGameFrame.Foundation.DataPersistence.JsonSerializer;

namespace Test.Server.Pvp
{
    internal sealed partial class PvpRealtimeModule
    {
        private readonly string tournamentDirectory;
        private static readonly SaveSerializer TournamentSerializer = new SaveSerializer();

        private static VersionedSaveService<TournamentSave> TournamentStore(string directory, string roomId) =>
            new VersionedSaveService<TournamentSave>(Path.Combine(directory, roomId), TournamentSerializer, 1, maxSlots: 1);

        internal static async Task<IReadOnlyList<PvpTournamentRoundSummary>> VerifyTournamentAsync(string directory, string roomId)
        {
            string id = roomId.ToLowerInvariant();
            if (!ValidTournamentRoom(id)) throw new ArgumentException("Enter a valid tournament room code.", nameof(roomId));
            var saved = await TournamentStore(directory, id).LoadAsync(0);
            if (saved.Status == SaveLoadStatus.NotFound) throw new FileNotFoundException($"Tournament {id} was not found.");
            var rounds = saved.Data.Tournament.Rounds.Where(round => round.Summary.Mode == PvpTournamentRules.RainPass).ToArray();
            if (rounds.Length == 0) throw new InvalidOperationException($"Tournament {id} has no Rain Pass result to verify.");
            foreach (var round in rounds) round.VerifyRainPass();
            return rounds.Select(round => round.Summary).ToArray();
        }

        private async Task LoadTournamentsAsync(CancellationToken token)
        {
            if (!Directory.Exists(tournamentDirectory)) return;
            foreach (string path in Directory.EnumerateDirectories(tournamentDirectory))
            {
                string id = Path.GetFileName(path);
                if (!ValidTournamentRoom(id) || id != id.ToLowerInvariant()) continue;
                var saved = await TournamentStore(tournamentDirectory, id).LoadAsync(0, token);
                if (saved.Status == SaveLoadStatus.NotFound) continue;
                var room = NewTournament(id, saved.Data.Tournament);
                for (int slot = 0; slot < room.Players.Length; slot++)
                {
                    var identity = saved.Data.Seats[slot];
                    if (identity != null)
                        room.Players[slot] = new Seat(identity.Id, slot, identity.Token) { ResumeThrough = ReconnectTicks };
                }
                // The twelve-second recovery window begins on reconnect after a server restart.
                room.WaitingForRecovery = true;
                rooms.Add(id, room);
            }
        }

        private static Room NewTournament(string id, PvpTournament tournament) =>
            new Room(id, PvpTournamentRules.Tournament)
            {
                Tournament = tournament,
                State = tournament.IsFinished ? PvpTournament.Finished : PvpTournament.AwaitingReady
            };

        private static bool ValidTournamentRoom(string id) =>
            ValidId(id) && id != "con" && id != "prn" && id != "aux" && id != "nul" &&
            !(id.Length == 4 && (id.StartsWith("com", StringComparison.Ordinal) || id.StartsWith("lpt", StringComparison.Ordinal)) &&
              id[3] >= '0' && id[3] <= '9');

        private void JoinTournament(Peer peer, PvpJoinRequest request)
        {
            string id = request.RoomId?.ToLowerInvariant();
            string reason = null;
            rooms.TryGetValue(id ?? "", out var room);
            Seat seat = room?.Players.SingleOrDefault(candidate => candidate != null && candidate.Id == request.PlayerId);
            bool resuming = !string.IsNullOrEmpty(request.ResumeToken);
            if (request.ProtocolVersion != GameProtocol.Version) reason = "ProtocolVersion";
            else if (!ValidTournamentRoom(id) || !ValidId(request.PlayerId)) reason = "InvalidIdentity";
            else if (peer.Room != null) reason = "AlreadyJoined";
            else if (room != null && room.Tournament == null) reason = "ModeMismatch";
            else if (room?.StorageFailed == true) reason = "StorageError";
            else if (resuming && (seat == null || seat.ResumeToken != request.ResumeToken)) reason = "MatchExpired";
            else if (!resuming && seat != null) reason = "PlayerTaken";
            else if (!resuming && room != null && (room.Tournament.IsFinished || room.Players.All(candidate => candidate != null)))
                reason = "RoomFull";
            else if (room == null && rooms.Values.Count(candidate => candidate.Tournament?.IsFinished != true) >= 64)
                reason = "RoomLimit";

            int after = 0;
            if (reason == null && resuming && room.Frames != null)
            {
                after = request.LastMatchId == room.MatchId ? request.LastSimulationTick : 0;
                if (after < 0 || after > room.Frames.Count) reason = "FrameHistoryUnavailable";
            }

            if (reason != null)
            {
                RejectJoin(peer, request, reason);
                return;
            }
            if (resuming)
            {
                CompleteTournamentJoin(peer, room, seat, after);
                return;
            }

            if (room == null)
            {
                room = NewTournament(id, new PvpTournament { Id = Guid.NewGuid().ToString("N") });
                rooms.Add(id, room);
            }
            int slot = room.Players[0] == null ? 0 : 1;
            seat = new Seat(request.PlayerId, slot) { ResumeThrough = clock + ReconnectTicks };
            room.Players[slot] = seat;
            PersistTournament(room, room.Tournament, () => CompleteTournamentJoin(peer, room, seat, 0),
                () => RejectJoin(peer, request, "StorageError"));
        }

        private void CompleteTournamentJoin(Peer peer, Room room, Seat seat, int after)
        {
            if (!peers.Contains(peer) || peer.Socket.State != WebSocketState.Open) return;
            if (room.WaitingForRecovery)
            {
                room.WaitingForRecovery = false;
                foreach (var existing in room.Players)
                    if (existing != null) existing.ResumeThrough = clock + ReconnectTicks;
            }
            Attach(peer, room, seat);
            peer.LastSentFrame = after;
            SendJoin(peer);
            SendTournament(room, peer);
            SendReplay(room, peer);
            Broadcast(room);
            foreach (var other in room.Players)
                if (other?.Peer != null && other.Peer != peer) SendTournament(room, other.Peer);
        }

        private void ReadyTournament(Peer peer, PvpTournamentReady request)
        {
            var room = peer.Room;
            if (room?.Tournament == null) return;
            if (room.State != PvpTournament.AwaitingReady || room.Pending != null || room.StorageFailed ||
                room.Players.Any(seat => seat?.Peer == null) || request.TournamentId != room.Tournament.Id ||
                request.TournamentVersion != room.Tournament.Version)
            {
                SendTournament(room, peer);
                return;
            }
            room.ReadyMask |= 1 << peer.Seat.Slot;
            BroadcastTournament(room);
            if (room.ReadyMask != 3) return;

            var next = room.Tournament.Start(Guid.NewGuid().ToString("N"));
            PersistTournament(room, next, () =>
            {
                room.StartRound(next.RoundMode, next.MatchId);
                room.ReadyMask = 0;
                room.State = PvpTournament.Running;
                BroadcastTournament(room);
                Broadcast(room);
            });
        }

        private bool AdvanceTournament(Room room)
        {
            if (room.Pending != null || room.StorageFailed || room.Tournament.IsFinished || room.WaitingForRecovery) return false;
            if (room.LeaveMask != 0)
            {
                int winner = room.LeaveMask == 3 ? -1 : PvpTournamentRules.OpponentOf(room.LeaveMask == 1 ? 0 : 1);
                EndTournament(room, winner, winner < 0 ? "BothPlayersLeft" : "PlayerLeft");
                return false;
            }
            var first = room.Players[0];
            var second = room.Players[1];
            if (first == null || second == null)
            {
                var occupied = first ?? second;
                if (occupied != null && occupied.Peer == null && clock >= occupied.ResumeThrough)
                    EndTournament(room, -1, "LobbyExpired");
                return false;
            }
            if (first.Peer == null || second.Peer == null)
            {
                bool firstExpired = first.Peer == null && clock >= first.ResumeThrough;
                bool secondExpired = second.Peer == null && clock >= second.ResumeThrough;
                if (firstExpired && secondExpired)
                    EndTournament(room, -1, "BothPlayersDisconnected");
                else if (firstExpired && second.Peer != null || secondExpired && first.Peer != null)
                    EndTournament(room, firstExpired ? 1 : 0, "PlayerDisconnected");
                return false;
            }
            return room.State == PvpTournament.Running;
        }

        private void DetachTournament(Room room, Seat seat, bool leave)
        {
            room.ReadyMask &= ~(1 << seat.Slot);
            if (leave && !room.Tournament.IsFinished)
                room.LeaveMask |= room.Players[1 - seat.Slot] == null ? 3 : 1 << seat.Slot;
            // Do not publish a simulation that completed while the result is still being saved.
            if (room.Pending != null) return;
            if (leave && !room.Tournament.IsFinished) AdvanceTournament(room);
            BroadcastTournament(room);
            if (room.Pending == null) Broadcast(room);
        }

        private void FinishTournamentRound(Room room, int winner, string reason)
        {
            var report = PvpTournamentRound.Capture(room.MatchId, room.Mode, winner, reason,
                room.Match.Snapshot, room.Frames?.ToArray());
            var next = room.Tournament.Confirm(report);
            if (ReferenceEquals(next, room.Tournament)) return;
            PersistTournament(room, next, () =>
            {
                room.State = PvpTournament.Finished;
                room.Winner = winner;
                room.EndReason = reason;
                Broadcast(room);
                CloseTournamentRound(room);
            });
        }

        private void EndTournament(Room room, int winner, string reason) =>
            PersistTournament(room, room.Tournament.End(winner, reason), () => CloseTournamentRound(room));

        private static void CloseTournamentRound(Room room)
        {
            room.Match = null;
            room.MatchId = room.Mode = null;
            room.Frames = null;
            room.Cues.Clear();
            room.ReadyMask = 0;
            room.State = room.Tournament.IsFinished ? PvpTournament.Finished : PvpTournament.AwaitingReady;
            BroadcastTournament(room);
        }

        private static void BroadcastTournament(Room room)
        {
            foreach (var seat in room.Players)
                if (seat?.Peer != null) SendTournament(room, seat.Peer);
        }

        private static void SendTournament(Room room, Peer peer)
        {
            int connected = (room.Players[0]?.Peer != null ? 1 : 0) | (room.Players[1]?.Peer != null ? 2 : 0);
            // Until the save succeeds, clients keep the last confirmed stage and score.
            bool running = room.Match != null && room.State == PvpTournament.Running;
            peer.Send("tournamentState", room.Tournament.Describe(room.Id, room.ReadyMask, connected, running, room.StorageFailed));
        }

        private void PersistTournament(Room room, PvpTournament next, Action committed, Action failed = null)
        {
            var saved = new TournamentSave
            {
                Tournament = next,
                Seats = room.Players.Select(seat => seat == null ? null : new TournamentSeat { Id = seat.Id, Token = seat.ResumeToken }).ToArray()
            };
            room.Pending = Task.Run(async () =>
            {
                try
                {
                    await TournamentStore(tournamentDirectory, room.Id).SaveAsync(0, saved, next.Id, stopping.Token);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Tournament {room.Id} version {next.Version} could not be saved: {error}");
                    lock (gate)
                    {
                        room.Pending = null;
                        room.StorageFailed = true;
                        failed?.Invoke();
                        BroadcastTournament(room);
                    }
                    return;
                }
                lock (gate)
                {
                    room.Pending = null;
                    if (stopping.IsCancellationRequested) return;
                    room.Tournament = next;
                    committed();
                }
            });
        }

        internal sealed class TournamentSave
        {
            public PvpTournament Tournament { get; set; }
            public TournamentSeat[] Seats { get; set; }
        }

        internal sealed class TournamentSeat
        {
            public string Id { get; set; }
            public string Token { get; set; }
        }
    }
}
