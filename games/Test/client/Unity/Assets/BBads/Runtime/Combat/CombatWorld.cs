using System.Collections.Generic;
using BBads.Combat.Behavior;
using BBads.Data;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class CombatWorld : MonoBehaviour
    {
        [SerializeField] bool useImprovedEngagementAvoidance = true;

        [Header("战斗行为模式")]
        [Tooltip("双方共用的行为模式；勾选下面的分阵营覆盖后本项失效。")]
        [SerializeField] CombatBehaviorMode globalMode = CombatBehaviorLibrary.DefaultMode;
        [Tooltip("为玩家方和敌方分别指定行为模式，便于观察两种行为互相对抗。")]
        [SerializeField] bool overridePerCamp;
        [SerializeField] CombatBehaviorMode playerMode = CombatBehaviorLibrary.DefaultMode;
        [SerializeField] CombatBehaviorMode enemyMode = CombatBehaviorLibrary.DefaultMode;

        sealed class EngagementReservation
        {
            public ICombatTarget target;
            public int slotIndex;
        }

        [SerializeField] PlayerBase playerBase;
        readonly List<CombatActor> actors = new List<CombatActor>();
        readonly Dictionary<CombatActor, int> spawnOrders = new Dictionary<CombatActor, int>();
        readonly Dictionary<CombatActor, EngagementReservation> engagementReservations =
            new Dictionary<CombatActor, EngagementReservation>();
        int nextSpawnOrder;

        const int EngagementRowsPerColumn = 7;
        const int MaxEngagementSlots = 63;
        const float EngagementGap = 0.06f;
        const float EngagementRangeInset = 0.08f;

        public PlayerBase PlayerBase => playerBase;
        public bool UseImprovedEngagementAvoidance => useImprovedEngagementAvoidance;
        public IReadOnlyList<CombatActor> Actors => actors;
        internal BBadsBattleClock Clock { get; private set; }
        internal void SetClock(BBadsBattleClock clock) => Clock = clock;

        public CombatBehaviorMode GlobalMode => globalMode;
        public CombatBehaviorMode PlayerMode => playerMode;
        public CombatBehaviorMode EnemyMode => enemyMode;
        public bool OverridePerCamp => overridePerCamp;

        // 每次行为配置变化都会自增；CombatActor 靠它感知切换并清空战斗状态。
        public int BehaviorRevision { get; private set; }

        public void SetPlayerBase(PlayerBase value) => playerBase = value;

        public void SetImprovedEngagementAvoidance(bool value)
        {
            if (useImprovedEngagementAvoidance == value)
                return;
            useImprovedEngagementAvoidance = value;
            InvalidateBehavior();
        }

        public CombatBehaviorMode GetMode(UnitCamp camp)
        {
            if (!overridePerCamp)
                return globalMode;
            return camp == UnitCamp.Player ? playerMode : enemyMode;
        }

        public CombatBehaviorProfile GetProfile(UnitCamp camp) => CombatBehaviorLibrary.Get(GetMode(camp));

        public void SetGlobalMode(CombatBehaviorMode value)
        {
            if (globalMode == value && !overridePerCamp)
                return;
            globalMode = value;
            overridePerCamp = false;
            InvalidateBehavior();
        }

        public void SetCampMode(UnitCamp camp, CombatBehaviorMode value)
        {
            if (camp == UnitCamp.Player)
            {
                if (overridePerCamp && playerMode == value)
                    return;
                playerMode = value;
            }
            else
            {
                if (overridePerCamp && enemyMode == value)
                    return;
                enemyMode = value;
            }
            overridePerCamp = true;
            InvalidateBehavior();
        }

        public void SetOverridePerCamp(bool value)
        {
            if (overridePerCamp == value)
                return;
            overridePerCamp = value;
            InvalidateBehavior();
        }

        void InvalidateBehavior()
        {
            BehaviorRevision++;
            engagementReservations.Clear();
        }

        public void Register(CombatActor actor)
        {
            if (actor == null || actors.Contains(actor))
                return;

            actors.Add(actor);
            spawnOrders[actor] = nextSpawnOrder++;
        }

        public void Unregister(CombatActor actor)
        {
            if (actor == null)
                return;

            actors.Remove(actor);
            spawnOrders.Remove(actor);
            ReleaseEngagementSlot(actor);

            List<CombatActor> staleAttackers = null;
            foreach (KeyValuePair<CombatActor, EngagementReservation> pair in engagementReservations)
            {
                if (!ReferenceEquals(pair.Value.target, actor))
                    continue;
                staleAttackers ??= new List<CombatActor>();
                staleAttackers.Add(pair.Key);
            }

            if (staleAttackers == null)
                return;
            for (int i = 0; i < staleAttackers.Count; i++)
                engagementReservations.Remove(staleAttackers[i]);
        }

        public Vector2 GetSeparation(CombatActor seeker)
        {
            if (seeker == null)
                return Vector2.zero;

            if (!spawnOrders.TryGetValue(seeker, out int seekerOrder))
                return Vector2.zero;

            Vector2 force = Vector2.zero;
            for (int i = 0; i < actors.Count; i++)
            {
                CombatActor candidate = actors[i];
                if (candidate == null)
                {
                    actors.RemoveAt(i);
                    i--;
                    continue;
                }
                if (candidate == seeker || !candidate.IsAlive || candidate.Camp != seeker.Camp)
                    continue;
                if (!spawnOrders.TryGetValue(candidate, out int candidateOrder) || candidateOrder >= seekerOrder)
                    continue;

                Vector2 delta = seeker.Position - candidate.Position;
                float distanceSqr = delta.sqrMagnitude;
                float desiredDistance = (seeker.BodyRadius + candidate.BodyRadius) * 1.15f;
                if (distanceSqr >= desiredDistance * desiredDistance)
                    continue;

                Vector2 direction;
                float distance;
                if (distanceSqr <= 0.000001f)
                {
                    int sign = seeker.GetInstanceID() < candidate.GetInstanceID() ? -1 : 1;
                    direction = new Vector2(sign, ((seeker.GetInstanceID() ^ candidate.GetInstanceID()) & 1) == 0 ? -0.35f : 0.35f).normalized;
                    distance = 0f;
                }
                else
                {
                    distance = Mathf.Sqrt(distanceSqr);
                    direction = delta / distance;
                }

                float overlap = 1f - distance / Mathf.Max(0.01f, desiredDistance);
                force += direction * overlap;
            }
            return Vector2.ClampMagnitude(force, 1f);
        }

        public Vector2 ResolveSpawnPosition(
            UnitCamp camp,
            float bodyRadius,
            Vector2 requestedPosition,
            CombatArena arena = null)
        {
            float radius = Mathf.Max(0.01f, bodyRadius);
            float step = Mathf.Clamp(radius * 1.6f, 0.12f, 0.3f);
            for (int attempt = 0; attempt < 15; attempt++)
            {
                int offsetIndex = attempt == 0 ? 0 : (attempt + 1) / 2;
                float sign = attempt == 0 || (attempt & 1) == 1 ? 1f : -1f;
                Vector2 candidatePosition = requestedPosition + Vector2.up * (offsetIndex * step * sign);
                if (arena != null)
                    candidatePosition = arena.Clamp(candidatePosition);
                if (!OverlapsSameCamp(camp, radius, candidatePosition))
                    return candidatePosition;
            }
            return arena != null ? arena.Clamp(requestedPosition) : requestedPosition;
        }

        bool OverlapsSameCamp(UnitCamp camp, float radius, Vector2 position)
        {
            for (int i = actors.Count - 1; i >= 0; i--)
            {
                CombatActor candidate = actors[i];
                if (candidate == null)
                {
                    actors.RemoveAt(i);
                    continue;
                }
                if (!candidate.IsAlive || candidate.Camp != camp)
                    continue;

                float desiredDistance = (radius + candidate.BodyRadius) * 1.05f;
                if ((candidate.Position - position).sqrMagnitude < desiredDistance * desiredDistance)
                    return true;
            }
            return false;
        }

        public Vector2 GetEngagementPosition(CombatActor attacker, ICombatTarget target)
        {
            if (attacker == null || target == null)
                return target != null ? target.Position : Vector2.zero;
            if (!useImprovedEngagementAvoidance)
                return GetLegacyEngagementPosition(attacker, target);

            if (!engagementReservations.TryGetValue(attacker, out EngagementReservation reservation) ||
                !ReferenceEquals(reservation.target, target))
            {
                ReleaseEngagementSlot(attacker);
                reservation = new EngagementReservation
                {
                    target = target,
                    slotIndex = FindFreeEngagementSlot(attacker, target)
                };
                engagementReservations[attacker] = reservation;
            }
            else if (ReservationConflictsWithEarlier(attacker, reservation))
            {
                reservation.slotIndex = FindFreeEngagementSlot(attacker, target);
            }

            return CalculateEngagementPosition(attacker, target, reservation.slotIndex);
        }

        Vector2 GetLegacyEngagementPosition(CombatActor attacker, ICombatTarget target)
        {
            if (!engagementReservations.TryGetValue(attacker, out EngagementReservation reservation) ||
                !ReferenceEquals(reservation.target, target))
            {
                ReleaseEngagementSlot(attacker);
                reservation = new EngagementReservation
                {
                    target = target,
                    slotIndex = FindFreeLegacyEngagementSlot(target)
                };
                engagementReservations[attacker] = reservation;
            }

            float targetRadius = target is CombatActor targetActor ? targetActor.BodyRadius : 0f;
            float spacing = Mathf.Clamp((attacker.BodyRadius + targetRadius) * 0.65f, 0.1f, 0.24f);
            Vector2 result = target is PlayerBase playerBase
                ? playerBase.ClosestPoint(attacker.Position)
                : target.Position;
            result.y += DecodeSlotOffset(reservation.slotIndex) * spacing;
            return result;
        }

        public void ReleaseEngagementSlot(CombatActor attacker)
        {
            if (attacker != null)
                engagementReservations.Remove(attacker);
        }

        int FindFreeEngagementSlot(CombatActor attacker, ICombatTarget target)
        {
            for (int slot = 0; slot < MaxEngagementSlots; slot++)
            {
                Vector2 candidate = CalculateEngagementPosition(attacker, target, slot);
                if (IsEngagementPositionFree(attacker, candidate))
                    return slot;
            }
            return MaxEngagementSlots - 1;
        }

        int FindFreeLegacyEngagementSlot(ICombatTarget target)
        {
            for (int slot = 0; slot < 31; slot++)
            {
                bool occupied = false;
                foreach (EngagementReservation reservation in engagementReservations.Values)
                {
                    if (!ReferenceEquals(reservation.target, target) || reservation.slotIndex != slot)
                        continue;
                    occupied = true;
                    break;
                }
                if (!occupied)
                    return slot;
            }
            return 30;
        }

        bool ReservationConflictsWithEarlier(CombatActor attacker, EngagementReservation reservation)
        {
            if (!spawnOrders.TryGetValue(attacker, out int attackerOrder))
                return false;

            Vector2 position = CalculateEngagementPosition(attacker, reservation.target, reservation.slotIndex);
            foreach (KeyValuePair<CombatActor, EngagementReservation> pair in engagementReservations)
            {
                CombatActor other = pair.Key;
                if (other == null || other == attacker || !other.IsAlive)
                    continue;
                if (!spawnOrders.TryGetValue(other, out int otherOrder) || otherOrder >= attackerOrder)
                    continue;

                Vector2 otherPosition = CalculateEngagementPosition(other, pair.Value.target, pair.Value.slotIndex);
                float spacing = attacker.BodyRadius + other.BodyRadius + EngagementGap;
                if ((position - otherPosition).sqrMagnitude < spacing * spacing)
                    return true;
            }
            return false;
        }

        bool IsEngagementPositionFree(CombatActor attacker, Vector2 candidate)
        {
            foreach (KeyValuePair<CombatActor, EngagementReservation> pair in engagementReservations)
            {
                CombatActor other = pair.Key;
                if (other == null || other == attacker || !other.IsAlive)
                    continue;

                Vector2 otherPosition = CalculateEngagementPosition(other, pair.Value.target, pair.Value.slotIndex);
                float spacing = attacker.BodyRadius + other.BodyRadius + EngagementGap;
                if ((candidate - otherPosition).sqrMagnitude < spacing * spacing)
                    return false;
            }
            return true;
        }

        static Vector2 CalculateEngagementPosition(
            CombatActor attacker,
            ICombatTarget target,
            int slotIndex)
        {
            Vector2 anchor;
            float targetBodyRadius;
            if (target is PlayerBase playerBase)
            {
                Vector2 closest = playerBase.ClosestPoint(attacker.Position);
                anchor = new Vector2(closest.x, playerBase.Position.y);
                targetBodyRadius = 0f;
            }
            else if (target is CombatActor targetActor)
            {
                anchor = targetActor.Position;
                targetBodyRadius = targetActor.BodyRadius;
            }
            else
            {
                anchor = target.Position;
                targetBodyRadius = 0f;
            }

            int column = Mathf.Max(0, slotIndex) / EngagementRowsPerColumn;
            int rowSlot = Mathf.Max(0, slotIndex) % EngagementRowsPerColumn;
            int row = DecodeSlotOffset(rowSlot);
            float bodyDiameter = attacker.BodyRadius * 2f;
            float rowSpacing = Mathf.Max(0.12f, bodyDiameter + EngagementGap);
            float columnSpacing = Mathf.Max(0.12f, bodyDiameter + EngagementGap);
            float contactDistance = targetBodyRadius + attacker.BodyRadius + EngagementGap;
            float furthestAttackPosition = Mathf.Max(
                contactDistance,
                attacker.GetAttackDistance(target) - EngagementRangeInset);
            float distanceFromTarget = attacker.Definition != null && attacker.Definition.isMelee
                ? Mathf.Min(furthestAttackPosition, contactDistance + column * columnSpacing)
                : Mathf.Max(contactDistance, furthestAttackPosition - column * columnSpacing);
            float side = attacker.Camp == UnitCamp.Player ? -1f : 1f;
            return anchor + new Vector2(side * distanceFromTarget, row * rowSpacing);
        }

        static int DecodeSlotOffset(int slotIndex)
        {
            if (slotIndex <= 0)
                return 0;
            int distance = (slotIndex + 1) / 2;
            return (slotIndex & 1) == 1 ? -distance : distance;
        }

        public void CollectOpponentsInRange(
            UnitCamp sourceCamp,
            Vector2 center,
            float radius,
            List<CombatActor> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (radius <= 0f)
                return;

            float radiusSqr = radius * radius;
            for (int i = actors.Count - 1; i >= 0; i--)
            {
                CombatActor candidate = actors[i];
                if (candidate == null)
                {
                    actors.RemoveAt(i);
                    continue;
                }
                if (!candidate.IsAlive || candidate.Camp == sourceCamp)
                    continue;
                float hitRadius = radius + candidate.HitRadius;
                if ((candidate.Position - center).sqrMagnitude <= hitRadius * hitRadius)
                    results.Add(candidate);
            }
        }

        public ICombatTarget FindTarget(CombatActor seeker)
        {
            if (seeker == null)
                return null;

            CombatActor nearestInAttackRange = null;
            CombatActor nearestInSearchRange = null;
            float nearestAttackSqr = float.MaxValue;
            float searchRange = Mathf.Max(0f, seeker.EffectiveSearchRange);
            float nearestSearchSqr = searchRange * searchRange;
            for (int i = actors.Count - 1; i >= 0; i--)
            {
                CombatActor candidate = actors[i];
                if (candidate == null)
                {
                    actors.RemoveAt(i);
                    continue;
                }
                if (!candidate.IsAlive || candidate == seeker || candidate.Camp == seeker.Camp)
                    continue;

                float sqr = (candidate.Position - seeker.Position).sqrMagnitude;
                float attackRange = seeker.GetAttackDistance(candidate);
                if (sqr <= attackRange * attackRange && sqr < nearestAttackSqr)
                {
                    nearestAttackSqr = sqr;
                    nearestInAttackRange = candidate;
                }
                else if (sqr <= nearestSearchSqr)
                {
                    nearestSearchSqr = sqr;
                    nearestInSearchRange = candidate;
                }
            }

            if (nearestInAttackRange != null)
                return nearestInAttackRange;
            if (nearestInSearchRange != null)
                return nearestInSearchRange;
            if (seeker.Camp == UnitCamp.Enemy && playerBase != null && playerBase.IsAlive)
                return playerBase;
            return null;
        }
    }
}
