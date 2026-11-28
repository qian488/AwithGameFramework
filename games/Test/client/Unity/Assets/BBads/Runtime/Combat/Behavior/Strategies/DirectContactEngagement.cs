using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 激进：不排队、不预约槽位，直接压到攻击距离边缘，允许彼此重叠。
    // 视觉上是一拥而上，用来对比基线的槽位站位。
    public sealed class DirectContactEngagement : IEngagementStrategy
    {
        readonly float speedScale;

        public DirectContactEngagement(float speedScale = 1.15f)
        {
            this.speedScale = speedScale;
        }

        public string DisplayName => "Direct contact";

        public EngagementIntent Resolve(CombatActor actor, ICombatTarget target, CombatWorld world)
        {
            Vector2 anchor = EngagementMath.AnchorOf(target, actor.Position);
            Vector2 delta = anchor - actor.Position;
            float distance = delta.magnitude;
            // 留一点余量，确保停下时仍在 GetAttackDistance 判定内。
            float contact = Mathf.Max(0.01f, actor.GetAttackDistance(target) - 0.05f);
            if (distance <= contact)
                return EngagementIntent.Hold(actor);

            Vector2 direction = EngagementMath.SafeDirection(delta, actor.Camp);
            return EngagementIntent.At(anchor - direction * contact, true, speedScale);
        }
    }
}
