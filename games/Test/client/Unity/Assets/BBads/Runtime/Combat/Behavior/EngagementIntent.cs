using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 交战意图：策略只描述「想站到哪、能不能开火、移动速度倍率」，
    // 由 CombatActor 负责执行。始终通过工厂方法构造，避免 default 值导致速度为 0。
    public struct EngagementIntent
    {
        public Vector2 desiredPosition;
        public bool allowAttack;
        public float speedScale;
        public bool clampToArena;

        public static EngagementIntent At(
            Vector2 position,
            bool allowAttack = true,
            float speedScale = 1f,
            bool clampToArena = true)
        {
            return new EngagementIntent
            {
                desiredPosition = position,
                allowAttack = allowAttack,
                speedScale = Mathf.Clamp(speedScale, 0f, 4f),
                clampToArena = clampToArena
            };
        }

        // 原地不动。距离为 0 会让 CombatActor 跳过移动分支。
        public static EngagementIntent Hold(CombatActor actor, bool allowAttack = true)
        {
            return At(actor.Position, allowAttack, 1f);
        }
    }
}
