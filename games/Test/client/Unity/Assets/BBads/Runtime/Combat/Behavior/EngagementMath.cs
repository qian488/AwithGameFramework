using BBads.Data;
using UnityEngine;

namespace BBads.Combat.Behavior
{
    internal static class EngagementMath
    {
        // 目标的参考点。玩家基地是个矩形，取最近点比取中心准确。
        public static Vector2 AnchorOf(ICombatTarget target, Vector2 from)
        {
            return target is PlayerBase playerBase ? playerBase.ClosestPoint(from) : target.Position;
        }

        public static float DistanceTo(ICombatTarget target, Vector2 from)
        {
            return target is PlayerBase playerBase
                ? playerBase.DistanceFrom(from)
                : (target.Position - from).magnitude;
        }

        // 目标能打到 actor 的距离。用于避战时计算安全半径。
        public static float ThreatRangeAgainst(ICombatTarget target, CombatActor actor)
        {
            return target is CombatActor targetActor ? targetActor.GetAttackDistance(actor) : 0f;
        }

        // 阵营的「后方」朝向：玩家方基地在 -x，敌方从 +x 来。
        public static Vector2 RearDirection(UnitCamp camp)
        {
            return camp == UnitCamp.Player ? Vector2.left : Vector2.right;
        }

        public static Vector2 SafeDirection(Vector2 delta, UnitCamp fallbackCamp)
        {
            float magnitude = delta.magnitude;
            return magnitude <= 0.0001f ? RearDirection(fallbackCamp) : delta / magnitude;
        }
    }
}
