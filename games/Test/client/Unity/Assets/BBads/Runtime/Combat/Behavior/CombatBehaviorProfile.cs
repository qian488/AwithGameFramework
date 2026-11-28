namespace BBads.Combat.Behavior
{
    // 一套完整的战斗行为：索敌策略 + 交战策略。策略实现都是无状态的，
    // 所以 profile 可以被任意数量的 CombatActor 共享。
    public sealed class CombatBehaviorProfile
    {
        public CombatBehaviorProfile(
            CombatBehaviorMode mode,
            string displayName,
            string description,
            ITargetingStrategy targeting,
            IEngagementStrategy engagement)
        {
            Mode = mode;
            DisplayName = displayName;
            Description = description;
            Targeting = targeting;
            Engagement = engagement;
        }

        public CombatBehaviorMode Mode { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public ITargetingStrategy Targeting { get; }
        public IEngagementStrategy Engagement { get; }
    }
}
