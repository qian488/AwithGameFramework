using System;
using System.Collections.Generic;
using BBads.Data;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace BBads.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatPresentationPool : MonoBehaviour
    {
        [SerializeField] Transform projectileRoot;
        [SerializeField] Transform hitEffectRoot;
        [SerializeField, Min(0)] int projectilePrewarmCount = 12;
        [SerializeField, Min(0)] int hitEffectPrewarmCount = 12;
        readonly List<CombatProjectile> projectiles = new List<CombatProjectile>();
        readonly List<CombatHitEffectView> hitEffects = new List<CombatHitEffectView>();
        readonly Dictionary<string, Sprite> spriteCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SkeletonDataAsset> hitEffectCache =
            new Dictionary<string, SkeletonDataAsset>(StringComparer.OrdinalIgnoreCase);
        BBadsBattleClock clock;
        int projectileRents;
        int projectileReturns;
        int peakProjectiles;

        internal float Delta => clock != null ? clock.Delta : Time.deltaTime;
        internal BBadsBattleClock Clock => clock;
        public (int Created, int Rents, int Returns, int Active, int Idle, int Peak) ProjectileCounts
        {
            get
            {
                int active = projectileRents - projectileReturns;
                return (projectiles.Count, projectileRents, projectileReturns, active, projectiles.Count - active, peakProjectiles);
            }
        }

        internal void SetClock(BBadsBattleClock value) => clock = value;

        void Awake()
        {
            if (projectileRoot == null)
                projectileRoot = transform;
            if (hitEffectRoot == null)
                hitEffectRoot = transform;
            for (int i = 0; i < projectilePrewarmCount; i++)
                CreateProjectile();
            for (int i = 0; i < hitEffectPrewarmCount; i++)
                CreateHitEffect();
        }

        public bool LaunchProjectile(
            UnitDefinition definition,
            AttackProfileDefinition attackProfile,
            CombatWorld world,
            ICombatTarget target,
            Vector2 startPosition)
        {
            if (definition == null || attackProfile == null || world == null || target == null ||
                string.IsNullOrWhiteSpace(attackProfile.projectileResourcePath))
                return false;

            Sprite sprite = LoadProjectileSprite(attackProfile.projectileResourcePath);
            if (sprite == null)
            {
                Debug.LogWarning(
                    $"Projectile sprite not found at Resources/{attackProfile.projectileResourcePath}.",
                    this);
                return false;
            }

            CombatProjectile projectile = AcquireProjectile();
            projectile.Launch(
                this,
                sprite,
                world,
                target,
                definition.camp,
                Mathf.Max(0f, definition.attack),
                Mathf.Max(0f, definition.aoeRange),
                attackProfile,
                startPosition,
                attackProfile.projectileSpeed);
            projectileRents++;
            peakProjectiles = Mathf.Max(peakProjectiles, projectileRents - projectileReturns);
            return true;
        }

        public void SpawnCastEffect(AttackProfileDefinition attackProfile, Vector2 position)
        {
            if (attackProfile == null)
                return;
            SpawnSpineEffect(
                attackProfile.castEffectResourcePath,
                position,
                attackProfile.castEffectScale,
                false,
                "Cast effect");
        }

        public void SpawnHitEffect(
            string resourcePath,
            Vector2 position,
            float scale = 1f,
            bool randomizeRotation = true)
        {
            SpawnSpineEffect(resourcePath, position, scale, randomizeRotation, "Hit effect");
        }

        public void SpawnDeathEffect(UnitDefinition definition, Vector2 position)
        {
            if (definition == null)
                return;
            SpawnSpineEffect(
                definition.deathEffectResourcePath,
                position,
                Mathf.Max(0.1f, definition.deathEffectScale),
                false,
                "Death effect");
        }

        void SpawnSpineEffect(
            string resourcePath,
            Vector2 position,
            float scale,
            bool randomizeRotation,
            string effectLabel)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return;
            SkeletonDataAsset asset = LoadHitEffect(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"{effectLabel} not found at Resources/{resourcePath}.", this);
                return;
            }

            CombatHitEffectView view = AcquireHitEffect();
            if (!view.Play(this, asset, position, Mathf.Max(0.1f, scale), randomizeRotation))
                ReleaseHitEffect(view);
        }

        internal void ReleaseProjectile(CombatProjectile projectile)
        {
            if (projectile == null || !projectile.gameObject.activeSelf)
                return;
            projectileReturns++;
            projectile.gameObject.SetActive(false);
            projectile.transform.SetParent(projectileRoot, false);
        }

        internal void ReleaseHitEffect(CombatHitEffectView effect)
        {
            if (effect == null)
                return;
            effect.gameObject.SetActive(false);
            effect.transform.SetParent(hitEffectRoot, false);
        }

        CombatProjectile AcquireProjectile()
        {
            for (int i = 0; i < projectiles.Count; i++)
                if (projectiles[i] != null && !projectiles[i].gameObject.activeSelf)
                    return projectiles[i];
            return CreateProjectile();
        }

        CombatHitEffectView AcquireHitEffect()
        {
            for (int i = 0; i < hitEffects.Count; i++)
                if (hitEffects[i] != null && !hitEffects[i].gameObject.activeSelf)
                    return hitEffects[i];
            return CreateHitEffect();
        }

        CombatProjectile CreateProjectile()
        {
            var go = new GameObject($"Projectile_{projectiles.Count:00}");
            go.transform.SetParent(projectileRoot, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "battleCharacter";
            renderer.sortingOrder = 250000;
            CombatProjectile projectile = go.AddComponent<CombatProjectile>();
            projectile.SetRenderer(renderer);
            projectiles.Add(projectile);
            go.SetActive(false);
            return projectile;
        }

        CombatHitEffectView CreateHitEffect()
        {
            var go = new GameObject($"HitEffect_{hitEffects.Count:00}");
            go.transform.SetParent(hitEffectRoot, false);
            CombatHitEffectView effect = go.AddComponent<CombatHitEffectView>();
            hitEffects.Add(effect);
            go.SetActive(false);
            return effect;
        }

        Sprite LoadProjectileSprite(string path)
        {
            string key = path.Trim();
            if (spriteCache.TryGetValue(key, out Sprite cached))
                return cached;
            Sprite sprite = Resources.Load<Sprite>(BBadsResource.Key(key));
            if (sprite == null)
            {
                Sprite[] sprites = Resources.LoadAll<Sprite>(BBadsResource.Key(key));
                if (sprites != null && sprites.Length > 0)
                    sprite = sprites[0];
            }
            spriteCache[key] = sprite;
            return sprite;
        }

        SkeletonDataAsset LoadHitEffect(string path)
        {
            string key = path.Trim();
            if (hitEffectCache.TryGetValue(key, out SkeletonDataAsset cached))
                return cached;
            SkeletonDataAsset[] assets = Resources.LoadAll<SkeletonDataAsset>(BBadsResource.Key(key));
            SkeletonDataAsset asset = assets != null && assets.Length > 0 ? assets[0] : null;
            hitEffectCache[key] = asset;
            return asset;
        }

        void OnDestroy()
        {
            foreach (var projectile in projectiles)
                if (projectile != null) Destroy(projectile.gameObject);
            foreach (var hitEffect in hitEffects)
                if (hitEffect != null) Destroy(hitEffect.gameObject);
            projectiles.Clear();
            hitEffects.Clear();
            spriteCache.Clear();
            hitEffectCache.Clear();
            projectileReturns = projectileRents;
        }
    }

    public sealed class CombatProjectile : MonoBehaviour
    {
        readonly List<CombatActor> areaTargets = new List<CombatActor>();
        CombatPresentationPool owner;
        CombatWorld world;
        ICombatTarget target;
        CombatActor targetActor;
        uint targetSpawnVersion;
        UnitCamp sourceCamp;
        SpriteRenderer spriteRenderer;
        string hitEffectResourcePath;
        Vector2 hitEffectOffset;
        ProjectileTrajectory trajectory;
        Vector2 launchPosition;
        Vector2 lastTargetPosition;
        Vector2 travelDirection;
        float damage;
        float aoeRange;
        float speed;
        float visualScale;
        float arcHeight;
        float rotationOffset;
        float hitEffectScale;
        bool randomHitRotation;
        float flightDuration;
        float flightElapsed;

        public void SetRenderer(SpriteRenderer value) => spriteRenderer = value;

        public void Launch(
            CombatPresentationPool valueOwner,
            Sprite sprite,
            CombatWorld valueWorld,
            ICombatTarget valueTarget,
            UnitCamp valueSourceCamp,
            float valueDamage,
            float valueAoeRange,
            AttackProfileDefinition attackProfile,
            Vector2 startPosition,
            float valueSpeed)
        {
            owner = valueOwner;
            world = valueWorld;
            target = valueTarget;
            targetActor = valueTarget as CombatActor;
            targetSpawnVersion = targetActor != null ? targetActor.SpawnVersion : 0;
            sourceCamp = valueSourceCamp;
            damage = valueDamage;
            aoeRange = valueAoeRange;
            hitEffectResourcePath = attackProfile?.hitEffectResourcePath;
            hitEffectOffset = attackProfile?.hitEffectOffset ?? Vector2.zero;
            hitEffectScale = Mathf.Max(0.01f, attackProfile?.hitEffectScale ?? 1f);
            randomHitRotation = attackProfile?.randomHitRotation ?? true;
            trajectory = attackProfile?.projectileTrajectory ?? ProjectileTrajectory.Homing;
            speed = Mathf.Max(0.1f, valueSpeed);
            visualScale = Mathf.Max(0.01f, attackProfile?.projectileScale ?? 1f);
            arcHeight = Mathf.Max(0f, attackProfile?.projectileArcHeight ?? 0f);
            rotationOffset = attackProfile?.projectileRotationOffset ?? 0f;
            launchPosition = startPosition;
            lastTargetPosition = target.Position;
            travelDirection = (lastTargetPosition - startPosition).normalized;
            flightDuration = Mathf.Max(0.01f, Vector2.Distance(startPosition, lastTargetPosition) / speed);
            flightElapsed = 0f;
            transform.position = new Vector3(startPosition.x, startPosition.y, 0f);
            transform.localScale = Vector3.one * (0.55f * visualScale);
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = Color.white;
            gameObject.SetActive(true);
            RefreshRotation();
        }

        void Update()
        {
            if (owner == null || world == null)
            {
                owner?.ReleaseProjectile(this);
                return;
            }
            float deltaTime = owner.Delta;
            if (deltaTime <= 0f)
                return;

            if (trajectory == ProjectileTrajectory.Homing &&
                HasLiveTarget())
                lastTargetPosition = target.Position;

            if (trajectory == ProjectileTrajectory.Arc)
            {
                UpdateArcFlight(deltaTime);
                return;
            }

            Vector2 current = transform.position;
            Vector2 delta = lastTargetPosition - current;
            float step = speed * deltaTime;
            if (delta.sqrMagnitude <= step * step || delta.sqrMagnitude <= 0.0001f)
            {
                Impact(lastTargetPosition);
                return;
            }

            travelDirection = delta.normalized;
            Vector2 next = current + travelDirection * step;
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            RefreshRotation();
        }

        void UpdateArcFlight(float deltaTime)
        {
            flightElapsed += deltaTime;
            float normalized = Mathf.Clamp01(flightElapsed / flightDuration);
            Vector2 previous = transform.position;
            Vector2 next = Vector2.Lerp(launchPosition, lastTargetPosition, normalized);
            next.y += 4f * arcHeight * normalized * (1f - normalized);
            travelDirection = next - previous;
            if (travelDirection.sqrMagnitude > 0.0001f)
                travelDirection.Normalize();
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            RefreshRotation();
            if (normalized >= 1f)
                Impact(lastTargetPosition);
        }

        void Impact(Vector2 point)
        {
            bool baseTarget = target is PlayerBase && HasLiveTarget();
            if (baseTarget)
                ApplyDamage(target, damage, travelDirection);

            if (aoeRange > 0f)
            {
                world.CollectOpponentsInRange(sourceCamp, point, aoeRange, areaTargets);
                for (int i = 0; i < areaTargets.Count; i++)
                    ApplyDamage(areaTargets[i], damage, travelDirection);
                areaTargets.Clear();
            }
            else if (!baseTarget && HasLiveTarget())
            {
                ApplyDamage(target, damage, travelDirection);
            }

            float effectScale = (aoeRange > 0f ? Mathf.Max(0.75f, aoeRange) : 0.65f) * hitEffectScale;
            Vector2 orientedOffset = hitEffectOffset;
            if (travelDirection.x < -0.01f)
                orientedOffset.x = -orientedOffset.x;
            owner.SpawnHitEffect(
                hitEffectResourcePath,
                point + orientedOffset,
                effectScale,
                randomHitRotation);
            owner.ReleaseProjectile(this);
        }

        bool HasLiveTarget()
        {
            if (target is UnityEngine.Object nativeTarget && nativeTarget == null)
                return false;
            return target != null && target.IsAlive && target.Camp != sourceCamp &&
                (targetActor == null || targetActor.SpawnVersion == targetSpawnVersion);
        }

        void RefreshRotation()
        {
            if (travelDirection.sqrMagnitude <= 0.0001f)
                return;
            float angle = Mathf.Atan2(travelDirection.y, travelDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
        }

        static void ApplyDamage(ICombatTarget value, float amount, Vector2 direction)
        {
            if (value is CombatActor actor)
                actor.ReceiveDamage(amount, direction);
            else
                value?.ReceiveDamage(amount);
        }

        void OnDisable()
        {
            target = null;
            targetActor = null;
            world = null;
            owner = null;
            areaTargets.Clear();
            if (spriteRenderer != null)
                spriteRenderer.sprite = null;
        }
    }

    [DefaultExecutionOrder(-90)]
    public sealed class CombatHitEffectView : MonoBehaviour
    {
        CombatPresentationPool owner;
        SkeletonAnimation skeletonAnimation;
        float remaining;

        public bool Play(
            CombatPresentationPool valueOwner,
            SkeletonDataAsset asset,
            Vector2 position,
            float scale,
            bool randomizeRotation)
        {
            if (valueOwner == null || asset == null)
                return false;
            owner = valueOwner;
            if (skeletonAnimation == null)
                skeletonAnimation = SkeletonAnimation.AddToGameObject(gameObject, asset, true);
            else
                skeletonAnimation.skeletonDataAsset = asset;

            transform.position = new Vector3(position.x, position.y, 0f);
            transform.rotation = randomizeRotation
                ? Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f))
                : Quaternion.identity;
            transform.localScale = Vector3.one * scale;
            gameObject.SetActive(true);
            skeletonAnimation.Initialize(true, true);
            Renderer renderer = skeletonAnimation.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sortingLayerName = "battleCharacter";
                renderer.sortingOrder = 300000;
            }

            SkeletonData data = asset.GetSkeletonData(false);
            Spine.Animation animation = data != null ? data.FindAnimation("Start") : null;
            if (animation == null && data != null && data.Animations.Count > 0)
                animation = data.Animations.Items[0];
            if (animation == null)
                return false;
            skeletonAnimation.AnimationState.SetAnimation(0, animation, false);
            remaining = Mathf.Max(0.12f, animation.Duration);
            return true;
        }

        void Update()
        {
            if (owner == null)
                return;
            var clock = owner.Clock;
            if (skeletonAnimation != null && clock != null)
            {
                skeletonAnimation.UnscaledTime = true;
                skeletonAnimation.timeScale = clock.PlaybackSpeed;
            }
            remaining -= owner.Delta;
            if (remaining <= 0f)
                owner.ReleaseHitEffect(this);
        }
    }
}
