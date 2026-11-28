using System;
using System.Threading.Tasks;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using UnityEngine;
using PlayerSnapshot = Test.Application.Protocol.PvpPlayerSnapshot;

namespace Test.Client.Combat
{
    public sealed partial class PvpClientShowcase
    {
        private PvpTournamentState tournament;
        private Task readyRequest;
        private Vector2 reportScroll;

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
            if (previous == null || previous.MatchId != state.MatchId || previous.Phase != state.Phase)
            {
                ResetRoundView();
                reportScroll = Vector2.zero;
            }
            roomState = state.Phase;
            if (arena != null) arena.gameObject.SetActive(state.Phase == Running && GameplayMode == RainPassMode);
            smoke?.RecordTournament(state);
        }

        private void DrawTournament()
        {
            var state = tournament;
            GUILayout.Label($"Score   {state.FirstScore} – {state.SecondScore}");
            if (state.Phase == PvpTournament.AwaitingReady)
            {
                GUILayout.Label($"Round {state.RoundNumber}   {TournamentModeName(state.Mode)}");
                for (int slot = 0; slot < 2; slot++)
                {
                    int mask = 1 << slot;
                    string status = (state.ConnectedMask & mask) == 0 ? "Waiting to connect" :
                        (state.ReadyMask & mask) == 0 ? "Preparing" : "Ready";
                    GUILayout.Label($"Player {slot + 1}{(slot == joined?.PlayerSlot ? " (you)" : "")}   {status}");
                }
                if (joined != null && IsConnected && state.ConnectedMask == 3 &&
                    (state.ReadyMask & (1 << joined.PlayerSlot)) == 0 && GUILayout.Button("Ready for next round"))
                    _ = StartTournamentReadyAsync();
            }
            else if (state.Phase == PvpTournament.Finished)
            {
                string outcome = state.Winner < 0 && state.EndReason == "Completed" ? "Draw" :
                    state.Winner < 0 ? "Tournament stopped" : state.Winner == joined?.PlayerSlot ? "Victory" : "Defeat";
                GUILayout.Label($"{outcome}   {TournamentReason(state.EndReason)}");
            }
            else if (state.Phase == PvpTournament.StorageError)
                GUILayout.Label("The server could not save the tournament.");

            if (state.Rounds == null || state.Rounds.Length == 0) return;
            reportScroll = GUILayout.BeginScrollView(reportScroll, GUILayout.MaxHeight(245));
            for (int index = 0; index < state.Rounds.Length; index++)
            {
                var round = state.Rounds[index];
                string result = round.Winner < 0 ? "Draw" : $"Player {round.Winner + 1} won";
                GUILayout.Label($"{index + 1}. {TournamentModeName(round.Mode)}   {result}   {TournamentReason(round.Reason)}");
                if (round.FirstBackpack != null && round.SecondBackpack != null)
                {
                    GUILayout.Label("Player 1 backpack");
                    DrawBackpack(new PlayerSnapshot { Backpack = round.FirstBackpack }, editable: false);
                    GUILayout.Label("Player 2 backpack");
                    DrawBackpack(new PlayerSnapshot { Backpack = round.SecondBackpack }, editable: false);
                }
            }
            GUILayout.EndScrollView();
        }

        private static string TournamentModeName(string value) => value == BBadsMode ? "BBads" : "Rain Pass";

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
