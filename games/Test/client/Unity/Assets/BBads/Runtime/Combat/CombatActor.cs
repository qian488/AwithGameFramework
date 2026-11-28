using Test.Domain.BBads;
using System;
using System.Collections.Generic;
using BBads.Combat.Behavior;
using BBads.Data;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class CombatActor : MonoBehaviour, ICombatTarget
    {
        UnitDefinition definition;
        CombatWorld world;
        CombatArena arena;
        ICombatTarget target;
        float currentHealth;
        float attackCooldown;
        double pendingAttackAt = -1d;
        ICombatTarget pendingTarget;
        int targetSearchPhaseOffset;
        int lastTargetSearchFrame = -1;
        UnitView view;
        WorldHealthBar healthBar;
        CombatPresentationPool presentationPool;
        bool dying;
        bool attackLocked;
        Vector2 lastDamageDirection;
        CombatBehaviorProfile activeProfile;
        EngagementIntent currentIntent;
        int behaviorRevisionSeen = -1;

        const int TargetSearchIntervalFrames = 5;
        const float SeparationStrength = 0.25f;
        const int SeparationRecalcIntervalFrames = 5;
        int lastSeparationFrame = -1;
        Vector2 separationForce;
        readonly List<CombatActor> areaDamageTargets = new List<CombatActor>();

        public event Action<CombatActor> Died;
        public event Action<float, float> HealthChanged;

        public UnitDefinition Definition => definition;
        public UnitCamp Camp => definition != null ? definition.camp : UnitCamp.Player;
        public Vector2 Position => transform.position;
        public bool IsAlive => isActiveAndEnabled && definition != null && currentHealth > 0f;
        internal uint SpawnVersion { get; private set; }
        public float CurrentHealth => currentHealth;
        public float BodyRadius => definition == null
            ? 0.22f
            : Mathf.Max(
                0.01f,
                world != null && world.UseImprovedEngagementAvoidance
                    ? definition.bodyRadius
                    : definition.attackSize);
        public float HitRadius => definition != null ? Mathf.Max(0.01f, definition.attackSize) : 0.18f;
        public float EffectiveSearchRange => definition == null
            ? 0f
            : definition.searchRange > 0f ? definition.searchRange : definition.attackRange;
        public float HealthRatio => definition == null
            ? 0f
            : Mathf.Clamp01(currentHealth / Mathf.Max(1f, definition.maxHealth));

        // 调试面板和 Gizmos 用来展示「这个单位现在在想什么」。
        public ICombatTarget CurrentTarget => target;
        public EngagementIntent CurrentIntent => currentIntent;
        public bool AttackLocked => attackLocked;
        public CombatBehaviorProfile ActiveProfile => activeProfile;

        public void Initialize(
            UnitDefinition value,
            CombatWorld combatWorld,
            CombatArena combatArena,
            CombatPresentationPool valuePresentationPool = null)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            SpawnVersion++;
            world?.ReleaseEngagementSlot(this);
            definition = value;
            world = combatWorld;
            arena = combatArena;
            presentationPool = valuePresentationPool;
            target = null;
            pendingTarget = null;
            pendingAttackAt = -1f;
            currentHealth = Mathf.Max(1f, definition.maxHealth);
            attackCooldown = 0f;
            targetSearchPhaseOffset = Mathf.Abs(GetInstanceID()) % TargetSearchIntervalFrames;
            lastTargetSearchFrame = -1;
            lastSeparationFrame = -1;
            separationForce = Vector2.zero;
            dying = false;
            attackLocked = false;
            activeProfile = null;
            currentIntent = EngagementIntent.At(transform.position);
            behaviorRevisionSeen = world != null ? world.BehaviorRevision : -1;
            lastDamageDirection = value.camp == UnitCamp.Player ? Vector2.left : Vector2.right;
            view = GetComponent<UnitView>();
            view?.SetClock(world != null ? world.Clock : null);
            view?.Configure(definition);
            view?.RefreshSortingOrder(transform.position.y);
            healthBar = GetComponent<WorldHealthBar>() ?? gameObject.AddComponent<WorldHealthBar>();
            healthBar.SetCamp(definition.camp);
            healthBar.SetOffsetY(definition.hpBarOffset);
            healthBar.SetValue(currentHealth, definition.maxHealth);
            healthBar.SetVisible(false);
            world?.Register(this);
            HealthChanged?.Invoke(currentHealth, definition.maxHealth);
        }

        void OnDisable()
        {
            world?.Unregister(this);
            target = null;
            pendingTarget = null;
            pendingAttackAt = -1f;
            dying = false;
            attackLocked = false;
            presentationPool = null;
        }

        void Update()
        {
            if (!IsAlive || world == null)
                return;
            float deltaTime = world.Clock != null ? world.Clock.Delta : Time.deltaTime;
            if (deltaTime <= 0f)
                return;
            double elapsed = world.Clock != null ? world.Clock.Elapsed : Time.timeAsDouble;

            // 模式切换时丢掉上一个模式的锁定和在挥的那一击，但保留目标：
            // 换不换目标由新模式的 ShouldReevaluate 决定，避免切换瞬间集体空转。
            if (behaviorRevisionSeen != world.BehaviorRevision)
            {
                behaviorRevisionSeen = world.BehaviorRevision;
                pendingTarget = null;
                pendingAttackAt = -1f;
                attackLocked = false;
                lastTargetSearchFrame = -1;
            }

            if (pendingAttackAt >= 0f)
            {
                if (elapsed < pendingAttackAt)
                {
                    if (pendingTarget != null)
                        view?.SetFacingByDirection(pendingTarget.Position - Position);
                    return;
                }

                ICombatTarget strikeTarget = pendingTarget;
                pendingTarget = null;
                pendingAttackAt = -1f;
                if (strikeTarget != null && strikeTarget.IsAlive && strikeTarget.Camp != Camp)
                    ResolveStrike(strikeTarget);
                attackCooldown = Mathf.Max(0.01f, definition.attackInterval);
                view?.PlayIdle();
                return;
            }

            attackCooldown = Mathf.Max(0f, attackCooldown - deltaTime);
            activeProfile = world.GetProfile(Camp);
            ITargetingStrategy targeting = activeProfile.Targeting;
            if (ShouldSearchForTarget(targeting))
                SetTarget(targeting.SelectTarget(this, world));
            else if (target == null || !target.IsAlive || target.Camp == Camp || !IsTargetInSearchRange())
            {
                SetTarget(null);
                view?.PlayIdle();
                return;
            }
            if (target == null)
            {
                view?.PlayIdle();
                return;
            }

            currentIntent = activeProfile.Engagement.Resolve(this, target, world);
            Vector2 targetPosition = currentIntent.desiredPosition;
            bool improvedEngagement = world.UseImprovedEngagementAvoidance;
            if (currentIntent.clampToArena && arena != null)
                targetPosition = arena.Clamp(targetPosition);
            Vector2 delta = targetPosition - Position;
            float distance = delta.magnitude;
            Vector2 targetDelta = target.Position - Position;
            float targetDistance = target is PlayerBase playerBase
                ? playerBase.DistanceFrom(Position)
                : targetDelta.magnitude;
            float attackDistance = GetAttackDistance(target);
            float exitDistance = attackDistance + Mathf.Max(0.08f, BodyRadius * 0.45f);
            float alignmentTolerance = improvedEngagement
                ? Mathf.Max(0.06f, BodyRadius * (attackLocked ? 0.75f : 0.5f))
                : Mathf.Max(0.08f, BodyRadius * (attackLocked ? 0.85f : 0.6f));
            bool engagementAligned = improvedEngagement
                ? distance <= alignmentTolerance
                : !definition.isMelee || Mathf.Abs(delta.y) <= alignmentTolerance;
            float allowedDistance = attackLocked ? exitDistance : attackDistance;
            if (currentIntent.allowAttack && targetDistance <= allowedDistance && engagementAligned)
            {
                attackLocked = true;
                view?.SetFacingByDirection(targetDelta);
                if (attackCooldown <= 0f)
                {
                    AttackProfileDefinition attackProfile = definition.AttackProfile;
                    view?.PlayAttack(attackProfile?.attackAnimation);
                    Vector2 facingDirection = target.Position - Position;
                    if (attackProfile != null && presentationPool != null)
                    {
                        Vector2 castOffset = OrientOffset(attackProfile.castEffectOffset, facingDirection);
                        presentationPool.SpawnCastEffect(attackProfile, Position + castOffset);
                    }
                    pendingTarget = target;
                    pendingAttackAt = elapsed + Mathf.Max(0.01f, attackProfile?.hitDelay ?? 0.3f);
                }
                else
                {
                    view?.PlayIdle();
                }
                return;
            }

            attackLocked = false;
            if (distance <= 0.0001f)
            {
                // 停火类模式（避战/后退中）会原地待命，这里补一次待机动画并保持朝向，
                // 否则会卡在上一帧的走路动作上。
                if (!currentIntent.allowAttack)
                {
                    view?.SetFacingByDirection(targetDelta);
                    view?.PlayIdle();
                }
                return;
            }
            RefreshSeparation();
            view?.SetFacingByDirection(delta);
            view?.PlayWalk();
            Vector2 desiredDirection = delta / distance;
            float moveSpeed = definition.moveSpeed * currentIntent.speedScale;
            Vector2 velocity = desiredDirection * moveSpeed +
                               separationForce * (SeparationStrength * moveSpeed);
            if (velocity.sqrMagnitude > moveSpeed * moveSpeed)
                velocity = velocity.normalized * moveSpeed;
            Vector2 next = Position + velocity * deltaTime;
            if (arena != null)
                next = arena.Clamp(next);
            transform.position = new Vector3(next.x, next.y, transform.position.z);
        }

        void LateUpdate()
        {
            if (IsAlive)
                view?.RefreshSortingOrder(transform.position.y);
        }

        // 帧节流留在这里（性能问题），「是否愿意换目标」交给策略（战术问题）。
        bool ShouldSearchForTarget(ITargetingStrategy targeting)
        {
            if (lastTargetSearchFrame == Time.frameCount)
                return false;
            if (!targeting.ShouldReevaluate(this, target))
                return false;
            lastTargetSearchFrame = Time.frameCount;
            return (Time.frameCount + targetSearchPhaseOffset) % TargetSearchIntervalFrames == 0;
        }

        bool IsTargetInSearchRange() => IsTargetInSearchRange(target);

        public bool IsTargetInSearchRange(ICombatTarget value)
        {
            if (value == null)
                return false;
            if (value is PlayerBase)
                return true;
            float range = Mathf.Max(0f, EffectiveSearchRange);
            return (value.Position - Position).sqrMagnitude <= range * range;
        }

        public float GetAttackDistance(ICombatTarget value)
        {
            float targetRadius = value is CombatActor actor ? actor.HitRadius : 0f;
            return Mathf.Max(0.01f, definition.attackRange + HitRadius + targetRadius);
        }

        // 基线站位：走 CombatWorld 的槽位预约。保留旧的远程单位短路分支，
        // 让 Baseline 模式与重构前完全一致。
        public Vector2 ResolveSlotEngagementPosition(ICombatTarget value)
        {
            if (world == null ||
                (!world.UseImprovedEngagementAvoidance && !definition.isMelee))
                return value.Position;
            return world.GetEngagementPosition(this, value);
        }

        void SetTarget(ICombatTarget value)
        {
            if (ReferenceEquals(target, value))
                return;
            world?.ReleaseEngagementSlot(this);
            target = value;
            attackLocked = false;
        }

        void ResolveStrike(ICombatTarget strikeTarget)
        {
            AttackProfileDefinition attackProfile = definition.AttackProfile;
            Vector2 strikeDirection = strikeTarget.Position - Position;
            Vector2 spawnOffset = attackProfile != null
                ? OrientOffset(attackProfile.projectileSpawnOffset, strikeDirection)
                : Vector2.up * 0.15f;
            if (!definition.isMelee && presentationPool != null &&
                presentationPool.LaunchProjectile(
                    definition,
                    attackProfile,
                    world,
                    strikeTarget,
                    Position + spawnOffset))
                return;

            ResolveImpact(strikeTarget, strikeTarget.Position);
        }

        void ResolveImpact(ICombatTarget strikeTarget, Vector2 impactPoint)
        {
            Vector2 direction = impactPoint - Position;
            AttackProfileDefinition attackProfile = definition.AttackProfile;
            Vector2 effectPosition = impactPoint + (attackProfile != null
                ? OrientOffset(attackProfile.hitEffectOffset, direction)
                : Vector2.zero);
            float hitEffectScale = attackProfile?.hitEffectScale ?? 1f;
            bool randomHitRotation = attackProfile?.randomHitRotation ?? true;
            if (strikeTarget is PlayerBase || definition.aoeRange <= 0f || world == null)
            {
                ApplyDamage(strikeTarget, definition.attack, direction);
                presentationPool?.SpawnHitEffect(
                    attackProfile?.hitEffectResourcePath,
                    effectPosition,
                    0.65f * hitEffectScale,
                    randomHitRotation);
                return;
            }

            world.CollectOpponentsInRange(Camp, impactPoint, definition.aoeRange, areaDamageTargets);
            for (int i = 0; i < areaDamageTargets.Count; i++)
            {
                CombatActor hit = areaDamageTargets[i];
                if (hit != null && hit.IsAlive)
                    hit.ReceiveDamage(definition.attack, hit.Position - Position);
            }
            areaDamageTargets.Clear();
            presentationPool?.SpawnHitEffect(
                attackProfile?.hitEffectResourcePath,
                effectPosition,
                Mathf.Max(0.75f, definition.aoeRange) * hitEffectScale,
                randomHitRotation);
        }

        static Vector2 OrientOffset(Vector2 offset, Vector2 direction)
        {
            if (direction.x < -0.01f)
                offset.x = -offset.x;
            return offset;
        }

        static void ApplyDamage(ICombatTarget value, float amount, Vector2 direction)
        {
            if (value is CombatActor actor)
                actor.ReceiveDamage(amount, direction);
            else
                value?.ReceiveDamage(amount);
        }

        void RefreshSeparation()
        {
            if (world == null || lastSeparationFrame >= 0 &&
                Time.frameCount - lastSeparationFrame < SeparationRecalcIntervalFrames)
                return;
            lastSeparationFrame = Time.frameCount;
            separationForce = world.GetSeparation(this);
        }

        public void ReceiveDamage(float amount)
        {
            ReceiveDamage(amount, Vector2.zero);
        }

        public void ReceiveDamage(float amount, Vector2 impactDirection)
        {
            if (!IsAlive || amount <= 0f)
                return;
            if (impactDirection.sqrMagnitude > 0.0001f)
                lastDamageDirection = impactDirection.normalized;
            var damage = BBadsCombatRules.ApplyDamage(currentHealth, amount);
            currentHealth = damage.RemainingHealth;
            view?.FlashHit();
            presentationPool?.SpawnHitEffect(
                definition.hitEffectResourcePath,
                Position,
                definition.hitEffectScale,
                false);
            HealthChanged?.Invoke(currentHealth, definition.maxHealth);
            healthBar?.SetValue(currentHealth, definition.maxHealth);
            if (currentHealth > 0f)
                healthBar?.SetVisible(true);
            if (damage.Defeated)
                BeginDeath();
        }

        void BeginDeath()
        {
            if (dying)
                return;
            dying = true;
            world?.Unregister(this);
            target = null;
            pendingTarget = null;
            pendingAttackAt = -1f;
            healthBar?.SetVisible(false);
            presentationPool?.SpawnDeathEffect(definition, Position);
            if (view != null)
                view.PlayDeath(lastDamageDirection, CompleteDeath);
            else
                CompleteDeath();
        }

        void CompleteDeath()
        {
            if (!dying)
                return;
            Died?.Invoke(this);
        }
    }
}
