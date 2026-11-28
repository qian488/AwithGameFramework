using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 避战：始终有目标（仍然索敌、仍然朝向），但主动退到目标打不到的半径之外并且不开火。
    // 用来直观看出「索敌」和「交战」是两件独立的事。
    public sealed class AvoidEngagement : IEngagementStrategy
    {
        readonly float margin;
        readonly float speedScale;

        public AvoidEngagement(float margin = 0.35f, float speedScale = 1.2f)
        {
            this.margin = Mathf.Max(0f, margin);
            this.speedScale = speedScale;
        }

        public string DisplayName => "Avoid";

        public EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world)
        {
            Vector2 anchor = EngagementMath.AnchorOf(target, actor.Position);
            float threat = EngagementMath.ThreatRangeAgainst(target, actor);
            float safe = threat + actor.BodyRadius + margin;

            Vector2 away = actor.Position - anchor;
            float distance = away.magnitude;
            if (distance >= safe)
                return EngagementIntent.Hold(actor, false);

            Vector2 direction = EngagementMath.SafeDirection(away, actor.Camp);
            return EngagementIntent.At(anchor + direction * safe, false, speedScale);
        }
    }
}
