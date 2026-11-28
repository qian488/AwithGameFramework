using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 残血撤退：装饰器。血量高于阈值时完全交给内层策略，低于阈值则脱离战斗往自家后方走。
    // 组合而不是继承，所以「残血撤退 + 风筝」这类叠加只是换一个内层。
    public sealed class RetreatWhenLowEngagement : IEngagementStrategy
    {
        readonly IEngagementStrategy inner;
        readonly float healthThreshold;
        readonly float retreatDistance;
        readonly float speedScale;

        public RetreatWhenLowEngagement(
            IEngagementStrategy inner,
            float healthThreshold = 0.3f,
            float retreatDistance = 2.5f,
            float speedScale = 1.35f)
        {
            this.inner = inner;
            this.healthThreshold = Mathf.Clamp01(healthThreshold);
            this.retreatDistance = Mathf.Max(0.1f, retreatDistance);
            this.speedScale = speedScale;
        }

        public string DisplayName => $"Retreat below {Mathf.RoundToInt(healthThreshold * 100f)}% → {inner.DisplayName}";

        public EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world)
        {
            if (actor.HealthRatio > healthThreshold)
                return inner.Resolve(actor, target, world);

            Vector2 rear = EngagementMath.RearDirection(actor.Camp);
            return EngagementIntent.At(actor.Position + rear * retreatDistance, false, speedScale);
        }
    }
}
