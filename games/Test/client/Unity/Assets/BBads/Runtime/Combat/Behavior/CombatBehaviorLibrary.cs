using System.Collections.Generic;

namespace BBads.Combat.Behavior
{
    // 所有可切换模式的注册表。策略无状态，所以这里用静态单例即可；
    // 新增模式只要在 Build() 里加一条，切换面板会自动列出来。
    public static class CombatBehaviorLibrary
    {
        static CombatBehaviorProfile[] profiles;
        static readonly Dictionary<CombatBehaviorMode, CombatBehaviorProfile> lookup =
            new Dictionary<CombatBehaviorMode, CombatBehaviorProfile>();

        public const CombatBehaviorMode DefaultMode = CombatBehaviorMode.Baseline;

        public static IReadOnlyList<CombatBehaviorProfile> Profiles
        {
            get
            {
                EnsureBuilt();
                return profiles;
            }
        }

        public static CombatBehaviorProfile Get(CombatBehaviorMode mode)
        {
            EnsureBuilt();
            return lookup.TryGetValue(mode, out CombatBehaviorProfile profile)
                ? profile
                : lookup[DefaultMode];
        }

        static void EnsureBuilt()
        {
            if (profiles != null)
                return;
            profiles = Build();
            lookup.Clear();
            foreach (CombatBehaviorProfile profile in profiles)
                lookup[profile.Mode] = profile;
        }

        static CombatBehaviorProfile[] Build()
        {
            return new[]
            {
                new CombatBehaviorProfile(
                    CombatBehaviorMode.Baseline,
                    "Baseline",
                    "基线：咬住最近目标不换，按交战槽位排队围攻。",
                    new NearestTargeting(true),
                    new SlotEngagement()),

                new CombatBehaviorProfile(
                    CombatBehaviorMode.Aggressive,
                    "Aggressive",
                    "激进：随时改打更近的敌人，不排队直接贴上去。",
                    new NearestTargeting(false),
                    new DirectContactEngagement()),

                new CombatBehaviorProfile(
                    CombatBehaviorMode.Kite,
                    "Kite",
                    "风筝：维持在射程外沿，被贴近就后退并停火。",
                    new NearestTargeting(false),
                    new KiteEngagement()),

                new CombatBehaviorProfile(
                    CombatBehaviorMode.Avoid,
                    "Avoid",
                    "避战：正常索敌并朝向敌人，但退到对方打不到的距离外且不开火。",
                    new NearestTargeting(true),
                    new AvoidEngagement()),

                new CombatBehaviorProfile(
                    CombatBehaviorMode.RetreatWhenLow,
                    "Retreat when low",
                    "残血撤退：血量高于 30% 时按基线打，低于则脱战往自家后方跑。",
                    new NearestTargeting(true),
                    new RetreatWhenLowEngagement(new SlotEngagement())),

                new CombatBehaviorProfile(
                    CombatBehaviorMode.FocusFire,
                    "Focus fire",
                    "集火：搜索范围内优先打血量最低的敌人。",
                    new LowestHealthTargeting(),
                    new SlotEngagement())
            };
        }

        public static string DescribeMode(CombatBehaviorMode mode)
        {
            CombatBehaviorProfile profile = Get(mode);
            return profile != null ? profile.Description : string.Empty;
        }
    }
}
