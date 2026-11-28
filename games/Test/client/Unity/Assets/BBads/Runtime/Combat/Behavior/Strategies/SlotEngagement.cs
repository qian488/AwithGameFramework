namespace BBads.Combat.Behavior
{
    // 基线交战：沿用 CombatWorld 的交战槽位预约，围在目标侧面排队。
    // clampToArena 跟随 UseImprovedEngagementAvoidance，以保证与重构前逐帧一致。
    public sealed class SlotEngagement : IEngagementStrategy
    {
        public string DisplayName => "Reserved slot";

        public EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world)
        {
            return EngagementIntent.At(
                actor.ResolveSlotEngagementPosition(target),
                true,
                1f,
                world.UseImprovedEngagementAvoidance);
        }
    }
}
