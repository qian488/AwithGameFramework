using System.Collections.Generic;
using UnityEngine;

namespace BBads.Combat.Behavior
{
    // 集火：在搜索范围内挑血量最低的敌人。范围内没人时退回最近目标逻辑（含基地兜底）。
    public sealed class LowestHealthTargeting : ITargetingStrategy
    {
        public string DisplayName => "Lowest health";

        public ICombatTarget SelectTarget(CombatActor seeker, CombatWorld world)
        {
            IReadOnlyList<CombatActor> actors = world.Actors;
            float range = Mathf.Max(0f, seeker.EffectiveSearchRange);
            float rangeSqr = range * range;
            CombatActor best = null;
            float bestHealth = float.MaxValue;

            for (int i = 0; i < actors.Count; i++)
            {
                CombatActor candidate = actors[i];
                if (candidate == null || candidate == seeker || !candidate.IsAlive)
                    continue;
                if (candidate.Camp == seeker.Camp)
                    continue;
                if ((candidate.Position - seeker.Position).sqrMagnitude > rangeSqr)
                    continue;
                if (candidate.CurrentHealth >= bestHealth)
                    continue;
                bestHealth = candidate.CurrentHealth;
                best = candidate;
            }

            return best != null ? best : world.FindTarget(seeker);
        }

        // 目标掉血或死亡随时会改变「最低血量」的答案，所以持续重算。
        public bool ShouldReevaluate(CombatActor seeker, ICombatTarget current) => true;
    }
}
