using BBads.Quality;
using System.Collections.Generic;
using BBads.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BBads.Backpack
{
    public enum ItemViewOrigin
    {
        None,
        Grid,
        Tray
    }

    [DisallowMultipleComponent]
    public sealed class ItemView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        const string DefaultSlotCellSpriteResourcePath = "BBads/Items/Bag_Slot";

        [SerializeField] RectTransform rectTransform;
        [SerializeField] Image frameImage;
        [SerializeField] Image progressFillImage;
        [SerializeField] Image iconImage;
        [SerializeField] RectTransform slotCellRoot;
        [SerializeField] Image[] slotCellImages = new Image[0];

        ItemDefinition definition;
        BackpackPresenter interactionOwner;
        ItemViewOrigin origin;
        int sourceId;
        Vector2 targetPosition;
        bool hasTarget;
        int? dragPointerId;
        bool presentationLocked;
        float displayedActivation;
        float targetActivation;
        bool activationWrapping;
        bool activationResetPending;
        float feedbackRemaining;
        float feedbackDuration;
        int feedbackPulseCount;
        float feedbackScale;
        Color feedbackColor = Color.white;
        bool feedbackShrinkFirst;
        int mergeSparkleCount;
        Color frameBaseColor = Color.white;
        Color iconBaseColor = Color.white;
        Color progressBaseColor = Color.white;

        public ItemDefinition Definition => definition;
        public ItemViewOrigin Origin => origin;
        public int SourceId => sourceId;
        public RectTransform RectTransform => rectTransform != null
            ? rectTransform
            : rectTransform = transform as RectTransform;

        public void Configure(ItemDefinition value, float cellSize, float cellGap)
        {
            definition = value;
            if (rectTransform == null)
                rectTransform = transform as RectTransform;

            Vector2 layoutSize = CalculateLayoutSize(value, cellSize, cellGap);
            rectTransform.sizeDelta = layoutSize;
            bool isSlot = value != null && value.type == ItemType.Slot;

            if (frameImage != null)
            {
                frameImage.gameObject.SetActive(!isSlot);
                frameImage.sprite = LoadSprite(value != null ? value.frameResourcePath : null);
                frameImage.enabled = frameImage.sprite != null;
                frameImage.color = Color.white;
                frameBaseColor = frameImage.color;
            }

            if (progressFillImage != null)
            {
                progressFillImage.sprite = frameImage != null ? frameImage.sprite : null;
                progressFillImage.type = Image.Type.Filled;
                progressFillImage.fillMethod = Image.FillMethod.Vertical;
                progressFillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                progressFillImage.color = TypeColor(value != null ? value.type : ItemType.None);
                progressBaseColor = progressFillImage.color;
                progressFillImage.fillAmount = 0f;
                progressFillImage.gameObject.SetActive(
                    value != null && (value.type == ItemType.Fish || value.type == ItemType.Cat));
            }

            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(!isSlot);
                iconImage.sprite = LoadSprite(value != null ? value.pictureResourcePath : null);
                iconImage.enabled = iconImage.sprite != null;
                iconImage.preserveAspect = true;
                iconImage.color = Color.white;
                iconBaseColor = iconImage.color;
            }

            ConfigureSlotCells(value, cellSize, cellGap, isSlot);

            SetCooldown(0f, 0f);
            displayedActivation = 0f;
            targetActivation = 0f;
            activationWrapping = false;
            activationResetPending = false;
            SetActivation(0f, value != null ? value.fishCost : 0f);
            displayedActivation = targetActivation;
            feedbackRemaining = 0f;
            presentationLocked = false;
            if (rectTransform != null)
                rectTransform.localScale = Vector3.one;
        }

        public void BindInteraction(
            BackpackPresenter owner,
            ItemViewOrigin valueOrigin,
            int valueSourceId)
        {
            interactionOwner = owner;
            origin = valueOrigin;
            sourceId = valueSourceId;
        }

        void ConfigureSlotCells(
            ItemDefinition value,
            float cellSize,
            float cellGap,
            bool isSlot)
        {
            if (slotCellRoot != null)
                slotCellRoot.gameObject.SetActive(isSlot);
            for (int i = 0; i < slotCellImages.Length; i++)
                if (slotCellImages[i] != null)
                {
                    slotCellImages[i].raycastTarget = false;
                    slotCellImages[i].gameObject.SetActive(false);
                }
            if (!isSlot || value == null)
                return;

            IReadOnlyList<Vector2Int> cells = value.EffectiveCells;
            GetBounds(cells, out int minX, out int minY, out int maxX, out int maxY);
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            float pitch = cellSize + cellGap;
            Sprite cellSprite = LoadSprite(value.frameResourcePath);
            if (cellSprite == null)
                cellSprite = LoadSprite(DefaultSlotCellSpriteResourcePath);
            int visibleCount = Mathf.Min(cells.Count, slotCellImages.Length);
            for (int i = 0; i < visibleCount; i++)
            {
                Image image = slotCellImages[i];
                if (image == null)
                    continue;
                Vector2Int cell = cells[i];
                RectTransform cellRect = image.rectTransform;
                cellRect.sizeDelta = Vector2.one * cellSize;
                cellRect.anchoredPosition = new Vector2(
                    (cell.x - centerX) * pitch,
                    (cell.y - centerY) * pitch);
                image.sprite = cellSprite;
                image.color = Color.white;
                image.preserveAspect = true;
                image.enabled = cellSprite != null;
                image.raycastTarget = true;
                image.gameObject.SetActive(true);
            }
            if (cells.Count > slotCellImages.Length)
                Debug.LogError(
                    $"Slot shape '{value.shapeResourcePath}' needs {cells.Count} prebuilt cells, " +
                    $"but ItemView only has {slotCellImages.Length}.",
                    this);
        }

        public static Vector2 CalculateLayoutSize(
            ItemDefinition value,
            float cellSize,
            float cellGap)
        {
            GetBounds(value != null ? value.EffectiveCells : null,
                out int minX, out int minY, out int maxX, out int maxY);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            return new Vector2(
                width * cellSize + Mathf.Max(0, width - 1) * cellGap,
                height * cellSize + Mathf.Max(0, height - 1) * cellGap);
        }

        public void SetTargetPosition(Vector2 value, bool immediate = false)
        {
            targetPosition = value;
            hasTarget = true;
            if (immediate && rectTransform != null)
                rectTransform.anchoredPosition = value;
        }

        public void TickVisual(float deltaTime)
        {
            if (rectTransform == null)
                return;
            float safeDelta = Mathf.Max(0f, deltaTime);
            if (!dragPointerId.HasValue && !presentationLocked && hasTarget)
            {
                float t = 1f - Mathf.Exp(-10f * safeDelta);
                rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, targetPosition, t);
            }

            if (definition != null && definition.type == ItemType.Cat && progressFillImage != null)
            {
                if (activationWrapping)
                {
                    displayedActivation = Mathf.MoveTowards(displayedActivation, 1f, safeDelta * 4f);
                    if (displayedActivation >= 0.999f)
                    {
                        displayedActivation = 1f;
                        activationWrapping = false;
                        activationResetPending = true;
                    }
                }
                else if (activationResetPending)
                {
                    displayedActivation = 0f;
                    activationResetPending = false;
                }
                else
                {
                    float t = 1f - Mathf.Exp(-8f * safeDelta);
                    displayedActivation = Mathf.Lerp(displayedActivation, targetActivation, t);
                    if (Mathf.Abs(displayedActivation - targetActivation) < 0.001f)
                        displayedActivation = targetActivation;
                }
                progressFillImage.fillAmount = displayedActivation;
            }

            TickFeedback(safeDelta);
        }

        public void SetPresentationLocked(bool value)
        {
            presentationLocked = value;
            if (value)
            {
                hasTarget = false;
                rectTransform?.SetAsLastSibling();
            }
        }

        public void SetPresentationWorldPosition(Vector3 worldPosition)
        {
            if (rectTransform != null)
                rectTransform.position = worldPosition;
        }

        public void PlayPlacementFeedback() =>
            BeginFeedback(new Color32(0x74, 0xF2, 0x87, 255), 1, 0.28f, 0.12f, false);

        public void PlayMergeFeedback(int mergeLevel)
        {
            int level = Mathf.Clamp(mergeLevel, 2, 4);
            ConfigureMergeSparkles(level);
            BeginFeedback(Color.white, level, 0.14f * level, 0.18f, true);
        }

        public void PlayFishCycleFeedback() =>
            BeginFeedback(new Color32(0x46, 0xCA, 0xFF, 255), 1, 0.24f, 0.10f, false);

        public void PlayCatChargeFeedback(bool summoned) =>
            BeginFeedback(new Color32(0xFF, 0xF2, 0x7C, 255), summoned ? 2 : 1, summoned ? 0.42f : 0.24f, summoned ? 0.18f : 0.10f, false);

        void BeginFeedback(Color color, int pulseCount, float duration, float scale, bool shrinkFirst)
        {
            feedbackColor = color;
            feedbackPulseCount = Mathf.Max(1, pulseCount);
            feedbackDuration = Mathf.Max(0.01f, duration);
            feedbackRemaining = feedbackDuration;
            feedbackScale = Mathf.Max(0f, scale);
            feedbackShrinkFirst = shrinkFirst;
        }

        void TickFeedback(float deltaTime)
        {
            if (feedbackRemaining <= 0f)
                return;

            feedbackRemaining = Mathf.Max(0f, feedbackRemaining - deltaTime);
            float normalized = 1f - feedbackRemaining / feedbackDuration;
            float pulse = Mathf.Sin(normalized * Mathf.PI * feedbackPulseCount);
            float visiblePulse = Mathf.Abs(pulse);
            float scaleOffset = visiblePulse * feedbackScale;
            if (feedbackShrinkFirst && normalized < 0.22f)
                scaleOffset = -Mathf.Sin(normalized / 0.22f * Mathf.PI) * 0.12f;
            rectTransform.localScale = Vector3.one * (1f + scaleOffset);

            float tint = visiblePulse * 0.65f;
            if (frameImage != null)
                frameImage.color = Color.Lerp(frameBaseColor, feedbackColor, tint);
            if (iconImage != null)
                iconImage.color = Color.Lerp(iconBaseColor, feedbackColor, tint);
            if (progressFillImage != null)
                progressFillImage.color = Color.Lerp(progressBaseColor, Color.white, tint);
            TickMergeSparkles(normalized, visiblePulse);

            if (feedbackRemaining > 0f)
                return;
            rectTransform.localScale = Vector3.one;
            if (frameImage != null)
                frameImage.color = frameBaseColor;
            if (iconImage != null)
                iconImage.color = iconBaseColor;
            if (progressFillImage != null)
                progressFillImage.color = progressBaseColor;
            HideMergeSparkles();
        }

        void ConfigureMergeSparkles(int count)
        {
            mergeSparkleCount = Mathf.Min(count, slotCellImages.Length);
            if (slotCellRoot == null || mergeSparkleCount <= 0)
                return;
            slotCellRoot.gameObject.SetActive(true);
            float radiusX = Mathf.Max(24f, rectTransform != null ? rectTransform.rect.width * 0.42f : 32f);
            float radiusY = Mathf.Max(24f, rectTransform != null ? rectTransform.rect.height * 0.42f : 32f);
            for (int i = 0; i < slotCellImages.Length; i++)
            {
                Image image = slotCellImages[i];
                if (image == null)
                    continue;
                bool visible = i < mergeSparkleCount;
                image.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                float angle = (Mathf.PI * 2f * i / mergeSparkleCount) + Mathf.PI * 0.25f;
                RectTransform sparkleRect = image.rectTransform;
                sparkleRect.sizeDelta = Vector2.one * 14f;
                sparkleRect.anchoredPosition = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                sparkleRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.enabled = true;
                image.raycastTarget = false;
                image.color = Color.white;
            }
        }

        void TickMergeSparkles(float normalized, float pulse)
        {
            if (mergeSparkleCount <= 0)
                return;
            float alpha = Mathf.Sin(Mathf.Clamp01(normalized) * Mathf.PI);
            for (int i = 0; i < mergeSparkleCount; i++)
            {
                Image image = slotCellImages[i];
                if (image == null)
                    continue;
                image.color = new Color(1f, 1f, 1f, alpha);
                image.rectTransform.localScale = Vector3.one * (0.65f + pulse * 0.75f);
            }
        }

        void HideMergeSparkles()
        {
            if (mergeSparkleCount <= 0)
                return;
            for (int i = 0; i < slotCellImages.Length; i++)
                if (slotCellImages[i] != null)
                {
                    slotCellImages[i].rectTransform.localScale = Vector3.one;
                    slotCellImages[i].gameObject.SetActive(false);
                }
            mergeSparkleCount = 0;
            if (slotCellRoot != null && (definition == null || definition.type != ItemType.Slot))
                slotCellRoot.gameObject.SetActive(false);
        }

        public void SetCooldown(float remaining, float duration)
        {
            if (progressFillImage == null || definition == null || definition.type != ItemType.Fish)
                return;
            progressFillImage.fillAmount = duration > 0f
                ? 1f - Mathf.Clamp01(remaining / duration)
                : 0f;
        }

        public void SetActivation(float current, float required)
        {
            if (progressFillImage == null || definition == null || definition.type != ItemType.Cat)
                return;
            float nextActivation = required > 0f ? Mathf.Clamp01(current / required) : 0f;
            if (nextActivation < targetActivation - 0.001f && displayedActivation > 0.001f)
            {
                activationWrapping = true;
                activationResetPending = false;
            }
            targetActivation = nextActivation;
        }

        public void ResetForPool(RectTransform poolRoot)
        {
            interactionOwner?.CancelItemDrag(this);
            definition = null;
            interactionOwner = null;
            origin = ItemViewOrigin.None;
            sourceId = 0;
            dragPointerId = null;
            presentationLocked = false;
            hasTarget = false;
            displayedActivation = 0f;
            targetActivation = 0f;
            activationWrapping = false;
            activationResetPending = false;
            feedbackRemaining = 0f;
            HideMergeSparkles();
            if (rectTransform == null)
                rectTransform = transform as RectTransform;
            if (rectTransform != null)
                rectTransform.localScale = Vector3.one;
            if (rectTransform != null && poolRoot != null)
                rectTransform.SetParent(poolRoot, false);
            gameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (dragPointerId.HasValue || interactionOwner == null || presentationLocked ||
                origin == ItemViewOrigin.None || !interactionOwner.BeginItemDrag(this))
                return;
            dragPointerId = eventData.pointerId;
            rectTransform?.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragPointerId != eventData.pointerId || rectTransform == null)
                return;
            RectTransform dragPlane = rectTransform.parent as RectTransform;
            if (dragPlane != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    dragPlane,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector3 worldPoint))
                rectTransform.position = worldPoint;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragPointerId != eventData.pointerId)
                return;
            dragPointerId = null;
            interactionOwner?.EndItemDrag(this, eventData);
        }

        internal void CancelDrag()
        {
            dragPointerId = null;
            if (hasTarget && rectTransform != null)
                rectTransform.anchoredPosition = targetPosition;
        }

        void OnDisable() => interactionOwner?.CancelItemDrag(this);

        static void GetBounds(IReadOnlyList<Vector2Int> cells, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = minY = int.MaxValue;
            maxX = maxY = int.MinValue;
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    minX = Mathf.Min(minX, cells[i].x);
                    minY = Mathf.Min(minY, cells[i].y);
                    maxX = Mathf.Max(maxX, cells[i].x);
                    maxY = Mathf.Max(maxY, cells[i].y);
                }
            }
            if (cells == null || cells.Count == 0)
                minX = minY = maxX = maxY = 0;
        }

        static Color TypeColor(ItemType type)
        {
            switch (type)
            {
                case ItemType.Fish: return new Color32(0x46, 0xCA, 0xFF, 255);
                case ItemType.Cat: return new Color32(0xFF, 0xF2, 0x7C, 255);
                default: return new Color32(255, 255, 255, 0);
            }
        }

        static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;
            string key = BBadsResource.Key(resourcePath);
            Sprite sprite = key == BBadsArtQuality.BagSlotKey || key == BBadsArtQuality.GangsterIconKey
                ? BBadsArtQuality.LoadSprite(key)
                : Resources.Load<Sprite>(key);
            if (sprite != null)
                return sprite;
            Sprite[] sprites = Resources.LoadAll<Sprite>(key);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }
    }
}
