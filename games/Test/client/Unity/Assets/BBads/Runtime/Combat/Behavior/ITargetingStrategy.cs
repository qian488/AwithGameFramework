namespace BBads.Combat.Behavior
{
    // 索敌策略：决定打谁，以及是否需要重新评估当前目标。
    public interface ITargetingStrategy
    {
        string DisplayName { get; }

        ICombatTarget SelectTarget(CombatActor seeker, CombatWorld world);

        // 返回 false 表示咬住当前目标不换。CombatActor 仍会按帧节流，
        // 所以这里只需要表达战术意愿，不需要考虑性能。
        bool ShouldReevaluate(CombatActor seeker, ICombatTarget current);
    }
}
