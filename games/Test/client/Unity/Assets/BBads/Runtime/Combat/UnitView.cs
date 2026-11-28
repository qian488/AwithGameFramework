using System;
using BBads.Data;
using Spine.Unity;
using UnityEngine;

namespace BBads.Combat
{
    [DefaultExecutionOrder(-90)]
    public sealed class UnitView : MonoBehaviour
    {
        [SerializeField] SpriteRenderer fallbackRenderer;
        [SerializeField, Min(0.01f)] float spineScale = 0.5f;
        [SerializeField] SkeletonAnimation spineAnimation;
        [SerializeField, Min(0.01f)] float hitFlashDuration = 0.1f;
        [SerializeField, Min(0.01f)] float spawnDuration = 0.22f;
        [SerializeField, Min(0.05f)] float fallbackDeathDuration = 0.42f;
        string currentAnimation;
        string idleAnimation;
        string walkAnimation;
        string attackAnimation;
        string deathAnimation;
        Renderer spineRenderer;
        int sortingTieBreaker;
        MaterialPropertyBlock materialPropertyBlock;
        Color fallbackBaseColor = Color.white;
        float configuredVisualScale = 1f;
        bool reverseFacing;
        bool configuredFlipX;
        float hitFlashRemaining;
        float spawnRemaining;
        float deathRemaining;
        float deathDuration;
        Vector2 deathVelocity;
        Action deathCompleted;
        bool dying;
        BBadsBattleClock clock;

        const int SortingBandSize = 2048;
        const int SortingBaseOrder = 100000;
        static readonly int BlackProperty = Shader.PropertyToID("_Black");

        void Awake()
        {
            sortingTieBreaker = GetInstanceID() & (SortingBandSize - 1);
            materialPropertyBlock = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            deathCompleted = null;
            dying = false;
            hitFlashRemaining = 0f;
            spawnRemaining = 0f;
            ResetHitFlash();
        }

        internal void SetClock(BBadsBattleClock value) => clock = value;

        public void Configure(UnitDefinition definition)
        {
            if (definition == null)
                return;

            configuredVisualScale = Mathf.Max(0.01f, definition.visualScale);
            Sprite icon = string.IsNullOrWhiteSpace(definition.pictureResourcePath)
                ? null
                : Resources.Load<Sprite>(BBadsResource.Key(definition.pictureResourcePath));
            SetFallback(icon);
            fallbackBaseColor = definition.camp == UnitCamp.Enemy
                ? new Color(0.85f, 0.25f, 0.25f, 1f)
                : new Color(0.25f, 0.55f, 1f, 1f);
            fallbackRenderer.color = fallbackBaseColor;
            dying = false;
            deathCompleted = null;
            deathRemaining = 0f;
            deathVelocity = Vector2.zero;
            transform.localScale = Vector3.one * 0.72f;
            spawnRemaining = Mathf.Max(0.01f, spawnDuration);
            hitFlashRemaining = 0f;
            ResetHitFlash();
            configuredFlipX = definition.flipX;
            reverseFacing = configuredFlipX != (definition.camp == UnitCamp.Player);
            SetSpine(definition.spineResourcePath, configuredFlipX);
            SetVisualAlpha(0f);
        }

        public void SetFallback(Sprite sprite)
        {
            if (fallbackRenderer == null)
                fallbackRenderer = GetComponentInChildren<SpriteRenderer>();
            if (fallbackRenderer == null)
            {
                GameObject fallback = new GameObject("Fallback");
                fallback.transform.SetParent(transform, false);
                fallbackRenderer = fallback.AddComponent<SpriteRenderer>();
                fallbackRenderer.transform.localScale = Vector3.one * 0.65f;
                fallbackRenderer.sortingOrder = 5;
            }
            fallbackRenderer.transform.localScale = Vector3.one * (0.65f * configuredVisualScale);
            fallbackRenderer.sprite = sprite != null ? sprite : Pixel;
            fallbackRenderer.gameObject.SetActive(true);
        }

        public void SetSpine(string resourcePath, bool flipX = false)
        {
            SkeletonDataAsset asset = LoadSkeletonData(resourcePath);
            if (asset == null)
            {
                if (spineAnimation != null)
                    spineAnimation.gameObject.SetActive(false);
                if (fallbackRenderer != null)
                    fallbackRenderer.gameObject.SetActive(true);
                return;
            }

            if (fallbackRenderer != null)
                fallbackRenderer.gameObject.SetActive(false);

            if (spineAnimation == null)
            {
                GameObject spineObject = new GameObject("Spine");
                spineObject.transform.SetParent(transform, false);
                spineObject.transform.localScale = Vector3.one * spineScale;
                spineAnimation = SkeletonAnimation.AddToGameObject(spineObject, asset, true);
            }
            else
            {
                spineAnimation.gameObject.SetActive(true);
                spineAnimation.skeletonDataAsset = asset;
            }

            spineAnimation.transform.localScale = Vector3.one * (spineScale * configuredVisualScale);
            spineAnimation.initialFlipX = flipX;
            spineAnimation.loop = true;
            spineAnimation.Initialize(true, true);
            spineRenderer = spineAnimation.GetComponent<Renderer>();
            if (spineRenderer != null)
            {
                spineRenderer.sortingLayerName = "battleCharacter";
                spineRenderer.sortingOrder = 10;
            }
            currentAnimation = null;

            Spine.SkeletonData data = asset.GetSkeletonData(false);
            idleAnimation = FindAnimation(data, "Idle", "idle", "Stand", "stand", "Walk", "walk");
            walkAnimation = FindAnimation(data, "Walk", "walk", "Move", "move", idleAnimation);
            attackAnimation = FindAnimation(data, "Attack", "attack", "Atk", "atk", idleAnimation);
            deathAnimation = FindOptionalAnimation(data, "Dead", "Death", "Die", "dead", "death", "die");
            PlayIdle();
        }

        public void PlayAnimation(string name, bool loop, bool forceRestart = false)
        {
            if (spineAnimation == null || string.IsNullOrWhiteSpace(name) || (!forceRestart && currentAnimation == name))
                return;
            Spine.SkeletonData data = spineAnimation.SkeletonDataAsset != null
                ? spineAnimation.SkeletonDataAsset.GetSkeletonData(false)
                : null;
            Spine.Animation animation = data != null ? data.FindAnimation(name) : null;
            if (animation == null)
                return;
            currentAnimation = name;
            spineAnimation.AnimationState.SetAnimation(0, animation, loop);
        }

        public void PlayIdle() => PlayAnimation(idleAnimation, true);
        public void PlayWalk() => PlayAnimation(walkAnimation, true);
        public void PlayAttack(string preferredAnimation = null)
        {
            string selected = HasAnimation(preferredAnimation) ? preferredAnimation : attackAnimation;
            PlayAnimation(selected, false, true);
        }

        bool HasAnimation(string animationName)
        {
            if (spineAnimation == null || spineAnimation.SkeletonDataAsset == null ||
                string.IsNullOrWhiteSpace(animationName))
                return false;
            Spine.SkeletonData data = spineAnimation.SkeletonDataAsset.GetSkeletonData(false);
            return data != null && data.FindAnimation(animationName) != null;
        }

        public void FlashHit()
        {
            if (dying)
                return;
            hitFlashRemaining = Mathf.Max(0.01f, hitFlashDuration);
            ApplyHitFlash(Color.white);
        }

        public void PlayDeath(Vector2 impactDirection, Action completed)
        {
            if (dying)
                return;
            dying = true;
            deathCompleted = completed;
            hitFlashRemaining = 0f;
            ResetHitFlash();
            deathVelocity = impactDirection.sqrMagnitude > 0.0001f
                ? impactDirection.normalized * 0.55f
                : Vector2.left * 0.35f;

            Spine.SkeletonData data = spineAnimation != null && spineAnimation.SkeletonDataAsset != null
                ? spineAnimation.SkeletonDataAsset.GetSkeletonData(false)
                : null;
            Spine.Animation animation = data != null && !string.IsNullOrEmpty(deathAnimation)
                ? data.FindAnimation(deathAnimation)
                : null;
            if (animation != null)
            {
                currentAnimation = deathAnimation;
                spineAnimation.AnimationState.SetAnimation(0, animation, false);
                deathDuration = Mathf.Max(0.2f, animation.Duration);
                deathVelocity = Vector2.zero;
            }
            else
            {
                deathDuration = Mathf.Max(0.05f, fallbackDeathDuration);
            }
            deathRemaining = deathDuration;
        }

        void Update()
        {
            if (spineAnimation != null && clock != null)
            {
                spineAnimation.UnscaledTime = true;
                spineAnimation.timeScale = clock.PlaybackSpeed;
            }
            float deltaTime = clock != null ? clock.Delta : Time.deltaTime;
            if (hitFlashRemaining > 0f)
            {
                hitFlashRemaining -= deltaTime;
                if (hitFlashRemaining <= 0f)
                    ResetHitFlash();
            }

            if (dying)
            {
                deathRemaining -= deltaTime;
                if (deathVelocity.sqrMagnitude > 0f)
                    transform.position += (Vector3)(deathVelocity * deltaTime);
                float fadeWindow = Mathf.Min(0.2f, deathDuration);
                float alpha = fadeWindow > 0f
                    ? Mathf.Clamp01(deathRemaining / fadeWindow)
                    : 0f;
                SetVisualAlpha(alpha);
                if (deathRemaining <= 0f)
                {
                    Action callback = deathCompleted;
                    deathCompleted = null;
                    callback?.Invoke();
                }
                return;
            }

            if (spawnRemaining > 0f)
            {
                spawnRemaining = Mathf.Max(0f, spawnRemaining - deltaTime);
                float normalized = 1f - spawnRemaining / Mathf.Max(0.01f, spawnDuration);
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1f, eased);
                SetVisualAlpha(eased);
                if (spawnRemaining <= 0f)
                {
                    transform.localScale = Vector3.one;
                    SetVisualAlpha(1f);
                }
            }
        }

        void ApplyHitFlash(Color color)
        {
            if (spineRenderer != null)
            {
                materialPropertyBlock ??= new MaterialPropertyBlock();
                spineRenderer.GetPropertyBlock(materialPropertyBlock);
                materialPropertyBlock.SetColor(BlackProperty, color);
                spineRenderer.SetPropertyBlock(materialPropertyBlock);
            }
            if (fallbackRenderer != null && fallbackRenderer.gameObject.activeSelf)
                fallbackRenderer.color = Color.white;
        }

        void ResetHitFlash()
        {
            if (spineRenderer != null)
            {
                materialPropertyBlock ??= new MaterialPropertyBlock();
                spineRenderer.GetPropertyBlock(materialPropertyBlock);
                materialPropertyBlock.SetColor(BlackProperty, Color.black);
                spineRenderer.SetPropertyBlock(materialPropertyBlock);
            }
            if (fallbackRenderer != null)
                fallbackRenderer.color = fallbackBaseColor;
        }

        void SetVisualAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (spineAnimation != null && spineAnimation.Skeleton != null)
                spineAnimation.Skeleton.A = alpha;
            if (fallbackRenderer != null)
            {
                Color color = fallbackBaseColor;
                color.a = alpha;
                fallbackRenderer.color = color;
            }
        }

        public void SetFacingByDirection(Vector2 direction)
        {
            if (spineAnimation == null || spineAnimation.Skeleton == null || Mathf.Abs(direction.x) < 0.01f)
                return;

            float magnitude = Mathf.Max(0.01f, Mathf.Abs(spineAnimation.Skeleton.ScaleX));
            bool flipX = direction.x > 0f;
            if (reverseFacing)
                flipX = !flipX;
            spineAnimation.Skeleton.ScaleX = flipX ? -magnitude : magnitude;
        }

        public void RefreshSortingOrder(float worldY)
        {
            int yBand = Mathf.RoundToInt(-worldY * 50f);
            int order = SortingBaseOrder + yBand * SortingBandSize + sortingTieBreaker;
            if (spineRenderer != null)
            {
                spineRenderer.sortingLayerName = "battleCharacter";
                spineRenderer.sortingOrder = order;
            }
            if (fallbackRenderer != null)
            {
                fallbackRenderer.sortingLayerName = "battleCharacter";
                fallbackRenderer.sortingOrder = order;
            }
        }

        static SkeletonDataAsset LoadSkeletonData(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;
            SkeletonDataAsset[] assets = Resources.LoadAll<SkeletonDataAsset>(BBadsResource.Key(resourcePath.Trim()));
            return assets != null && assets.Length > 0 ? assets[0] : null;
        }

        static string FindAnimation(Spine.SkeletonData data, params string[] preferredNames)
        {
            if (data == null)
                return null;
            foreach (string name in preferredNames)
                if (data.FindAnimation(name) != null)
                    return name;
            return data.Animations.Count > 0 ? data.Animations.Items[0].Name : null;
        }

        static string FindOptionalAnimation(Spine.SkeletonData data, params string[] preferredNames)
        {
            if (data == null)
                return null;
            foreach (string name in preferredNames)
                if (!string.IsNullOrEmpty(name) && data.FindAnimation(name) != null)
                    return name;
            return null;
        }

        static Sprite pixel;

        static Sprite Pixel
        {
            get
            {
                if (pixel == null)
                    pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return pixel;
            }
        }
    }
}
