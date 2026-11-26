using System;
using Test.Domain.Combat;

namespace Test.Domain.Expedition
{
    public static class ExpeditionRules
    {
        public const string SaltCausewayId = "salt-causeway";
        public const string EmberFoundryId = "ember-foundry";
        public const string CedarGroveId = "cedar-grove";

        public const string StandardGear = "standard";
        public const string GuardGear = "guard";
        public const string ArcherGear = "archer";

        public const string ArrowsSupply = "arrows";
        public const string PostureSupply = "posture";

        public const int FirstClearReward = 1;
        private const int CheckpointArrowRefill = 3;

        public static bool IsRoute(string route) => route is SaltCausewayId or EmberFoundryId;

        public static bool IsGear(string gear) => gear is StandardGear or GuardGear or ArcherGear;

        public static bool IsSupply(string supply) => supply is ArrowsSupply or PostureSupply;

        /// <summary>Returns the contract at a zero-based leg index, or null for an invalid route or leg.</summary>
        public static string ContractForLeg(string route, int legIndex)
        {
            if (!IsRoute(route)) return null;

            return legIndex switch
            {
                0 => route,
                1 => CedarGroveId,
                _ => null
            };
        }

        /// <summary>Uses standard capacity when the gear ID is missing or unrecognized.</summary>
        public static int ArrowCapacity(string gear) => gear switch
        {
            GuardGear => 3,
            ArcherGear => 9,
            _ => BowRules.QuiverCapacity
        };

        /// <summary>Returns the number of ticks to subtract from the normal posture recovery interval.</summary>
        public static int PostureRecoveryBonus(string gear) => gear == GuardGear ? 1 : 0;

        /// <summary>Adds checkpoint arrows to the carried count when the arrow supply is selected.</summary>
        public static int RestockArrows(int current, int capacity, string supply)
        {
            capacity = Math.Max(0, capacity);
            int carried = Math.Clamp(current, 0, capacity);
            if (supply != ArrowsSupply) return carried;

            return carried + Math.Min(CheckpointArrowRefill, capacity - carried);
        }

        public static int AwardFirstClear(bool alreadyAwarded) => alreadyAwarded ? 0 : FirstClearReward;

        public static bool CanUnlock(int balance, int price, bool alreadyOwned) =>
            !alreadyOwned && price > 0 && balance >= price;

        public static bool TrySpend(int balance, int price, bool alreadyOwned, out int remainingBalance)
        {
            bool canUnlock = CanUnlock(balance, price, alreadyOwned);
            remainingBalance = canUnlock ? balance - price : balance;
            return canUnlock;
        }
    }
}
