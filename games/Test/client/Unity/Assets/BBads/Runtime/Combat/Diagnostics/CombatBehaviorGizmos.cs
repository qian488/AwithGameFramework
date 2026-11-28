#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using BBads.Data;
using UnityEngine;

namespace BBads.Combat.Diagnostics
{
    // 把「索敌 / 站位 / 停火」画出来，用于肉眼比较各模式的差别。
    public sealed class CombatBehaviorGizmos : MonoBehaviour
    {
        [SerializeField] CombatWorld world;
        [SerializeField] bool drawSearchRange;
        [SerializeField] bool drawAttackRange = true;
        [SerializeField] bool drawTargetLink = true;
        [SerializeField] bool drawDesiredPosition = true;

        static readonly Color PlayerColor = new Color(0.35f, 0.75f, 1f);
        static readonly Color EnemyColor = new Color(1f, 0.45f, 0.35f);
        static readonly Color HoldFireColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color DesiredColor = new Color(0.6f, 1f, 0.6f);

        public void Configure(CombatWorld value)
        {
            world = value;
        }

        void Awake()
        {
            world ??= GetComponent<CombatWorld>();
        }

        void OnDrawGizmos()
        {
            if (world == null)
                return;

            IReadOnlyList<CombatActor> actors = world.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CombatActor actor = actors[i];
                if (actor == null || !actor.IsAlive)
                    continue;

                Vector2 position = actor.Position;
                Color campColor = actor.Camp == UnitCamp.Player ? PlayerColor : EnemyColor;

                if (drawSearchRange)
                {
                    Gizmos.color = new Color(campColor.r, campColor.g, campColor.b, 0.18f);
                    DrawCircle(position, actor.EffectiveSearchRange);
                }

                ICombatTarget target = actor.CurrentTarget;
                if (drawAttackRange && target != null)
                {
                    Gizmos.color = actor.AttackLocked ? Color.white : campColor;
                    DrawCircle(position, actor.GetAttackDistance(target));
                }

                if (drawTargetLink && target != null)
                {
                    // 停火状态画黄线：说明它盯着目标但不打（避战 / 风筝后退 / 残血撤退）。
                    Gizmos.color = actor.CurrentIntent.allowAttack ? campColor : HoldFireColor;
                    Gizmos.DrawLine(position, target.Position);
                }

                if (drawDesiredPosition && target != null)
                {
                    Vector2 desired = actor.CurrentIntent.desiredPosition;
                    Gizmos.color = DesiredColor;
                    Gizmos.DrawLine(position, desired);
                    DrawCircle(desired, actor.BodyRadius * 0.6f);
                }
            }
        }

        static void DrawCircle(Vector2 center, float radius, int segments = 24)
        {
            if (radius <= 0.001f)
                return;
            float step = Mathf.PI * 2f / segments;
            Vector3 previous = new Vector3(center.x + radius, center.y, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i;
                Vector3 next = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    0f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
#endif
