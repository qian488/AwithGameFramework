namespace BBads.Combat.Behavior
{
    public enum CombatBehaviorMode
    {
        // 与重构前逐帧一致，作为对比基准，不要修改它的策略组合。
        Baseline = 0,
        Aggressive = 1,
        Kite = 2,
        Avoid = 3,
        RetreatWhenLow = 4,
        FocusFire = 5
    }
}
