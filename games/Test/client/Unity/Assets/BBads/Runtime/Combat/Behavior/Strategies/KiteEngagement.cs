using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 风筝：把自己维持在射程外沿的一条距离带里。太近就后退且停火，太远就贴到带子边缘。
    // 近战单位射程很短，这条带子会退化成贴身，属于预期表现。
    public sealed class KiteEngagement : IEngagementStrategy
    {
        readonly float minRangeFactor;
        readonly float retreatSpeedScale;

        public KiteEngagement(float minRangeFactor = 0.65f, float retreatSpeedScale = 1.1f)
        {
            this.minRangeFactor = Mathf.Clamp(minRangeFactor, 0.05f, 0.95f);
            this.retreatSpeedScale = retreatSpeedScale;
        }

        public string DisplayName => "Kite";

        public EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world)
        {
            Vector2 anchor = EngagementMath.AnchorOf(target, actor.Position);
            float attackDistance = actor.GetAttackDistance(target);
            float outer = Mathf.Max(0.05f, attackDistance - 0.04f);
            float inner = outer * minRangeFactor;

            Vector2 away = actor.Position - anchor;
            float distance = away.magnitude;
            Vector2 direction = EngagementMath.SafeDirection(away, actor.Camp);

            if (distance < inner)
                return EngagementIntent.At(anchor + direction * outer, false, retreatSpeedScale);
            if (distance > outer)
                return EngagementIntent.At(anchor + direction * outer, true, 1f);
            return EngagementIntent.Hold(actor);
        }
    }
}
