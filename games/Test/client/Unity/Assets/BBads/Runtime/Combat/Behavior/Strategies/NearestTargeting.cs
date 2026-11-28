namespace BBads.Combat.Behavior
{
    // 最近目标索敌。lockOn = true 时咬住当前目标不换（现有基线行为）；
    // false 时每次节流窗口都重新挑最近的，表现为「见谁近打谁」。
    public sealed class NearestTargeting : ITargetingStrategy
    {
        readonly bool lockOn;

        public NearestTargeting(bool lockOn)
        {
            this.lockOn = lockOn;
        }

        public string DisplayName => lockOn ? "Nearest (lock on)" : "Nearest (switching)";

        public ICombatTarget SelectTarget(CombatActor seeker, CombatWorld world)
        {
            return world.FindTarget(seeker);
        }

        public bool ShouldReevaluate(CombatActor seeker, ICombatTarget current)
        {
            if (!lockOn)
                return true;
            if (current == null || !current.IsAlive || current.Camp == seeker.Camp)
                return true;
            // 基地是兜底目标，只要还有活体单位就应该优先换过去。
            if (current is PlayerBase)
                return true;
            return !seeker.IsTargetInSearchRange(current);
        }
    }
}
