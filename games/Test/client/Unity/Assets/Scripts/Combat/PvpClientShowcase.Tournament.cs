using System;
using System.Threading.Tasks;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using UnityEngine;

namespace Test.Client.Combat
{
    public sealed partial class PvpClientShowcase
    {
        private PvpTournamentState tournament;
        private Task readyRequest;
        private TournamentLobbyView tournamentLobby;
        internal TournamentLobbyView TournamentLobby => tournamentLobby;
        private bool TournamentLobbyVisible => tournamentLobby != null && tournamentLobby.IsOpen;

        private bool IsTournament => mode == PvpTournamentRules.Tournament;
        private string GameplayMode => IsTournament ? tournament?.Mode : mode;
        private string TournamentSeatKey =>
            $"Test.PvpTournament.{serverUrl.TrimEnd('/')}.{activeRoom.ToLowerInvariant()}.{playerId.Trim()}";

        internal Task StartTournamentReadyAsync()
        {
            if (readyRequest != null && !readyRequest.IsCompleted) return readyRequest;
            var current = connection;
            return readyRequest = SendTournamentReadyAsync(current);
        }

        private async Task SendTournamentReadyAsync(Connection current)
        {
            if (current == null) return;
            try
            {
                await current.Session.ReadyTournamentAsync(current.Token);
                if (connection == current) error = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (connection == current) error = exception.Message;
            }
        }

        private void ReceiveTournament(PvpTournamentState state)
        {
            var previous = tournament;
            tournament = state;
            if (previous == null || previous.MatchId != state.MatchId || previous.Phase != state.Phase) ResetRoundView();
            roomState = state.Phase;
            if (arena != null) arena.gameObject.SetActive(state.Phase == Running && GameplayMode == RainPassMode);
            UpdateTournamentLobby();
            smoke?.RecordTournament(state);
        }

        private void UpdateTournamentLobby()
        {
            if (!IsTournament || tournament?.Phase == Running && IsConnected &&
                tournament.ConnectedMask == 3 && snapshot?.PeerConnected != false)
            {
                if (TournamentLobbyVisible) tournamentLobby.Hide();
                return;
            }
            if (tournamentLobby == null)
            {
                var prefab = Resources.Load<GameObject>("Tournament/Lobby")?.GetComponent<TournamentLobbyView>();
                if (prefab == null) throw new InvalidOperationException("Tournament/Lobby prefab is required.");
                tournamentLobby = Instantiate(prefab, transform, false);
                tournamentLobby.Initialize(serverUrl, roomId, playerId, ConnectFromLobbyAsync, StartTournamentReadyAsync,
                    ReconnectAsync, () =>
                    {
                        if (owner != null) owner.ClosePvp();
                        else _ = LeaveAsync();
                    });
            }
            var state = tournament;
            bool canJoin = connection == null && joined == null;
            bool canReady = state?.Phase == PvpTournament.AwaitingReady && joined != null && IsConnected &&
                state.ConnectedMask == 3 && (state.ReadyMask & (1 << joined.PlayerSlot)) == 0;
            bool canReconnect = joined != null && !IsConnected && ConnectionStatus == "Disconnected";
            bool finished = state?.Phase is PvpTournament.Finished or PvpTournament.StorageError;
            string page = canJoin || joined == null ? "join" : finished ? "results" :
                !IsConnected || state?.Phase == Running ? "reconnect" : "lobby";
            string message = error;
            if (string.IsNullOrEmpty(message))
                message = page switch
                {
                    "join" => canJoin ? "Join a tournament" : "Connecting…",
                    "reconnect" => IsConnected ? "Opponent reconnecting…" : ConnectionStatus,
                    "results" => TournamentOutcome(state),
                    _ when state?.ConnectedMask != 3 => "Waiting for the other player",
                    _ when state?.Rounds?.Length > 0 => $"Round {state.Rounds.Length} confirmed",
                    _ => "Both players connected"
                };
            tournamentLobby.Present(page, message, state, joined?.PlayerSlot ?? -1, canJoin, canReady, canReconnect);
        }

        private Task ConnectFromLobbyAsync()
        {
            serverUrl = tournamentLobby.Server.text;
            roomId = tournamentLobby.Room.text;
            playerId = tournamentLobby.Player.text;
            return ConnectAsync();
        }

        private string TournamentOutcome(PvpTournamentState state)
        {
            if (state.Phase == PvpTournament.StorageError) return "The server could not save the tournament.";
            string outcome = state.Winner < 0 && state.EndReason == "Completed" ? "Draw" :
                state.Winner < 0 ? "Tournament stopped" : state.Winner == joined?.PlayerSlot ? "Victory" : "Defeat";
            return (outcome + "   " + TournamentReason(state.EndReason)).TrimEnd();
        }

        private static string TournamentReason(string value) => value switch
        {
            "PlayerLeft" => "Opponent forfeited or left",
            "PlayerDisconnected" => "Reconnect timed out",
            "BothPlayersLeft" => "Lobby closed",
            "LobbyExpired" => "Lobby expired",
            "BothPlayersDisconnected" => "Both players disconnected",
            "TimeLimit" => "Time limit",
            "Combat" => "Battle finished",
            _ => ""
        };
    }
}
