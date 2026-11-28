using System;
using System.Linq;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;

namespace Test.Application.Combat.Pvp
{
    public sealed class PvpTournament
    {
        public const string AwaitingReady = "AwaitingReady";
        public const string Running = "Running";
        public const string Finished = "Finished";
        public const string StorageError = "StorageError";

        public string Id { get; set; }
        public int Version { get; set; } = 1;
        public string MatchId { get; set; }
        public string EndReason { get; set; }
        public int Winner { get; set; } = -1;
        public PvpTournamentRound[] Rounds { get; set; } = Array.Empty<PvpTournamentRound>();

        public bool IsFinished => EndReason != null;
        public int RoundNumber => Rounds.Length + 1;
        public string RoundMode => IsFinished ? null : PvpTournamentRules.ModeFor(RoundNumber);

        public PvpTournament Start(string matchId) => new PvpTournament
        {
            Id = Id, Version = Version + 1, MatchId = matchId, Rounds = Rounds
        };

        public PvpTournament Confirm(PvpTournamentRound round)
        {
            if (IsFinished || string.IsNullOrEmpty(MatchId) || round.Summary.MatchId != MatchId ||
                Rounds.Any(previous => previous.Summary.MatchId == MatchId))
                return this;
            var rounds = Rounds.Append(round).ToArray();
            var score = PvpTournamentRules.Score(rounds.Select(result => result.Summary.Winner).ToArray());
            return new PvpTournament
            {
                Id = Id, Version = Version + 1, Rounds = rounds,
                Winner = score.Winner, EndReason = score.Finished ? "Completed" : null
            };
        }

        public PvpTournament End(int winner, string reason) => IsFinished ? this : new PvpTournament
        {
            Id = Id, Version = Version + 1, Rounds = Rounds, Winner = winner, EndReason = reason
        };

        public PvpTournamentState Describe(string roomId, int ready, int connected, bool running, bool storageError)
        {
            var score = PvpTournamentRules.Score(Rounds.Select(round => round.Summary.Winner).ToArray());
            return new PvpTournamentState
            {
                RoomId = roomId, TournamentId = Id, TournamentVersion = Version,
                Phase = storageError ? StorageError : IsFinished ? Finished : running ? Running : AwaitingReady,
                RoundNumber = IsFinished ? Rounds.Length : RoundNumber, Mode = RoundMode,
                MatchId = running ? MatchId : null, FirstScore = score.First, SecondScore = score.Second,
                ReadyMask = ready, ConnectedMask = connected, Winner = Winner, EndReason = EndReason,
                Rounds = Rounds.Select(round => round.Summary).ToArray()
            };
        }
    }

    public sealed class PvpTournamentRound
    {
        // Rain Pass currently starts from a fixed, seedless initial state.
        public const int RainPassSeed = 0;
        private const string RainPassRulesVersion = "rain-pass-pvp-1";

        public PvpTournamentRoundSummary Summary { get; set; }
        public int InitialSeed { get; set; }
        public string ConfigurationFingerprint { get; set; }
        public PvpSimulationFrame[] Inputs { get; set; }

        public static PvpTournamentRound Capture(string matchId, string mode, int winner, string reason,
            PvpNetworkSnapshot snapshot, PvpSimulationFrame[] inputs)
        {
            return new PvpTournamentRound
            {
                Summary = new PvpTournamentRoundSummary
                {
                    MatchId = matchId, Mode = mode, Winner = winner, Reason = reason,
                    FinalTick = snapshot.Tick, StateHash = snapshot.StateHash,
                    FirstBackpack = DescribeBackpack(snapshot.PlayerOne),
                    SecondBackpack = DescribeBackpack(snapshot.PlayerTwo)
                },
                InitialSeed = RainPassSeed,
                ConfigurationFingerprint = inputs == null ? null : RainPassFingerprint(new RainPassPvpNetworkMatch()),
                Inputs = inputs
            };
        }

        public void VerifyRainPass()
        {
            var match = new RainPassPvpNetworkMatch();
            if (Summary.Mode != PvpTournamentRules.RainPass || Inputs == null ||
                InitialSeed != RainPassSeed || ConfigurationFingerprint != RainPassFingerprint(match))
                throw new InvalidOperationException("The Rain Pass replay requires its original rules and configuration.");
            foreach (var frame in Inputs)
            {
                var snapshot = match.Advance(PvpFrameSync.Decode(frame.Tick, 0, frame.Controls[0]),
                    PvpFrameSync.Decode(frame.Tick, 1, frame.Controls[1])).Snapshot;
                if (snapshot.Tick != frame.Tick || snapshot.StateHash != frame.StateHash)
                    throw new InvalidOperationException($"Rain Pass replay diverged at tick {frame.Tick}.");
            }
            if (match.Snapshot.Tick != Summary.FinalTick || match.Snapshot.StateHash != Summary.StateHash ||
                (match.Snapshot.Result?.Winner ?? -1) != Summary.Winner)
                throw new InvalidOperationException("Rain Pass replay disagrees with the saved result.");
        }

        private static string RainPassFingerprint(RainPassPvpNetworkMatch match) =>
            RainPassRulesVersion + ":" + match.Snapshot.StateHash;

        private static string[] DescribeBackpack(PvpNetworkPlayerState player) =>
            player.Backpack?.Placements.Select(item => $"{item.Item}:{item.X}:{item.Z}").ToArray();
    }
}
