namespace BBads.Combat.Behavior
{
    // 交战策略：给定目标，决定站位、进退与是否开火。避战、风筝、撤退都在这一层。
    public interface IEngagementStrategy
    {
        string DisplayName { get; }

        EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world);
    }
}
