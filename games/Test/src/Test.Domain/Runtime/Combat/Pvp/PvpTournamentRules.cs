using System;

namespace Test.Domain.Combat.Pvp
{
    public static class PvpTournamentRules
    {
        public const string Tournament = "tournament";
        public const string RainPass = "rain-pass";
        public const string BBads = "bbads";

        public static string ModeFor(int roundNumber) => roundNumber switch
        {
            1 or 3 => RainPass,
            2 => BBads,
            _ => throw new ArgumentOutOfRangeException(nameof(roundNumber))
        };

        public static (int First, int Second, bool Finished, int Winner) Score(int[] roundWinners)
        {
            if (roundWinners == null || roundWinners.Length > 3)
                throw new ArgumentException("A tournament has up to three rounds.", nameof(roundWinners));
            int first = 0, second = 0;
            foreach (int winner in roundWinners)
            {
                if (winner == 0) first++;
                else if (winner == 1) second++;
                else if (winner != -1) throw new ArgumentOutOfRangeException(nameof(roundWinners));
            }
            bool finished = roundWinners.Length == 3 || roundWinners.Length == 2 && first != second;
            return (first, second, finished, !finished || first == second ? -1 : first > second ? 0 : 1);
        }

        public static int OpponentOf(int slot) => slot switch
        {
            0 => 1,
            1 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }
}
