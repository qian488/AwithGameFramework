using System.Collections.Generic;
using AwithGameFrame.Gameplay.Input;
using BBads.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BBads.Backpack
{
    [DisallowMultipleComponent]
    public sealed class BackpackPresenter : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] BackpackController controller;
        [SerializeField] RectTransform gridRoot;
        [SerializeField] RectTransform gridItemRoot;
        [SerializeField] RectTransform trayRoot;
        [SerializeField] RectTransform itemPoolRoot;
        [SerializeField] CardDragInfoView dragInfoView;
        [SerializeField] Image[] gridCells = new Image[0];
        [SerializeField] Sprite gridCellSprite;
        [SerializeField] ItemView[] itemPool = new ItemView[0];

        [Header("Designer Layout")]
        [SerializeField, Min(16f)] float cellSize = 82f;
        [SerializeField, Min(0f)] float cellGap = 6f;
        [SerializeField, Min(0f)] float traySpacing = 12f;
        [SerializeField, Min(0.1f)] float autoFlightDuration = 0.45f;
        [SerializeField] Color validTargetColor = new Color32(0x63, 0xE6, 0x78, 255);

        readonly Dictionary<int, ItemView> gridItemViews = new Dictionary<int, ItemView>();
        readonly Dictionary<int, ItemView> trayItemViews = new Dictionary<int, ItemView>();
        readonly List<int> removeIds = new List<int>();
        readonly HashSet<Vector2Int> highlightedCells = new HashSet<Vector2Int>();
        const string DefaultGridCellSpriteResourcePath = "BBads/Items/Bag_Slot";
        Color[] gridCellBaseColors;
        AutoPlacementAnimation autoPlacement;
        UnitCatalog unitCatalog;
        ItemView draggedView;
        BackpackDragInput dragInput;
        bool applicationFocused = true;
        bool bound;

        internal InputService DragInputService => dragInput?.Service;

        sealed class AutoPlacementAnimation
        {
            public BackpackPlacementRequest Request;
            public ItemView View;
            public Vector3 StartWorld;
            public Vector3 TargetWorld;
            public float Elapsed;
        }

        public void Configure(BackpackController value)
        {
            CancelActiveDrag();
            Unbind();
            controller = value;
            ResetPoolForRuntime();
            Bind();
            SyncAll();
        }

        public void SetUnitCatalog(UnitCatalog value)
        {
            unitCatalog = value;
            ResolveDragInfoView();
            dragInfoView?.Configure(unitCatalog);
        }

        void Awake()
        {
            ResolveDragInfoView();
            if (validTargetColor.a <= 0f)
                validTargetColor = new Color32(0x63, 0xE6, 0x78, 255);
            ResolveGridCellSprite();
            ApplyGridCellSprites();
            CacheGridCellColors();
            ResetPoolForRuntime();
        }

        void OnEnable()
        {
            Bind();
            SyncAll();
        }

        void OnDisable()
        {
            CancelActiveDrag();
            if (autoPlacement != null && controller != null && autoPlacement.Request != null)
                controller.CancelAutoPlacement(autoPlacement.Request.TrayId);
            if (autoPlacement != null && autoPlacement.View != null)
                autoPlacement.View.SetPresentationLocked(false);
            autoPlacement = null;
            highlightedCells.Clear();
            Unbind();
        }

        void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            if (!focused)
                CancelActiveDrag();
        }

        void OnValidate()
        {
            ResolveGridCellSprite();
            ApplyGridCellSprites();
            ApplyLayoutPreview();
        }

        void Update()
        {
            if (controller == null)
                return;
            CheckActiveDrag();
            SyncGridItems();
            SyncTray();
            float delta = controller.Clock != null ? controller.Clock.Delta : Time.deltaTime;
            if (delta <= 0f)
                return;
            foreach (ItemView view in gridItemViews.Values)
                if (view != null)
                    view.TickVisual(delta);
            foreach (ItemView view in trayItemViews.Values)
                if (view != null)
                    view.TickVisual(delta);
            TickAutoPlacement(delta);
        }

        public void ApplyLayoutPreview()
        {
            float pitch = cellSize + cellGap;
            if (gridRoot != null)
                gridRoot.sizeDelta = new Vector2(7f * cellSize + 6f * cellGap, 6f * cellSize + 5f * cellGap);
            for (int i = 0; i < gridCells.Length; i++)
            {
                Image image = gridCells[i];
                if (image == null)
                    continue;
                ApplyGridCellSprite(image);
                RectTransform rect = image.rectTransform;
                rect.sizeDelta = Vector2.one * cellSize;
                int x = i % 7;
                int y = i / 7;
                rect.anchoredPosition = new Vector2((x - 3f) * pitch, (y - 2.5f) * pitch);
            }
        }

        void Bind()
        {
            if (bound || !isActiveAndEnabled || controller == null)
                return;
            dragInput = new BackpackDragInput();
            controller.GridChanged += HandleGridChanged;
            controller.GridLayoutChanged += HandleGridLayoutChanged;
            controller.TrayChanged += HandleTrayChanged;
            controller.AutoPlacementRequested += HandleAutoPlacementRequested;
            controller.PlacementCompleted += HandlePlacementCompleted;
            controller.FishCycleCompleted += HandleFishCycleCompleted;
            controller.CatChargeChanged += HandleCatChargeChanged;
            if (controller.Clock != null)
                controller.Clock.PlaybackChanged += HandlePlaybackChanged;
            bound = true;
        }

        void Unbind()
        {
            if (!bound)
                return;
            bound = false;
            dragInput.Dispose();
            dragInput = null;
            if (controller == null)
                return;
            controller.GridChanged -= HandleGridChanged;
            controller.GridLayoutChanged -= HandleGridLayoutChanged;
            controller.TrayChanged -= HandleTrayChanged;
            controller.AutoPlacementRequested -= HandleAutoPlacementRequested;
            controller.PlacementCompleted -= HandlePlacementCompleted;
            controller.FishCycleCompleted -= HandleFishCycleCompleted;
            controller.CatChargeChanged -= HandleCatChargeChanged;
            if (controller.Clock != null)
                controller.Clock.PlaybackChanged -= HandlePlaybackChanged;
        }

        void HandlePlaybackChanged(bool paused)
        {
            if (paused)
                CheckActiveDrag();
        }

        void CheckActiveDrag()
        {
            if (draggedView != null && !AllowsDragInput())
                CancelActiveDrag();
        }

        void HandleGridChanged(BackpackItemInstance _) => SyncGridItems();
        void HandleGridLayoutChanged() => SyncGridCells();
        void HandleTrayChanged() => SyncTray();

        void HandleAutoPlacementRequested(BackpackPlacementRequest request)
        {
            if (request == null)
                return;
            if (controller == null || gridRoot == null)
            {
                controller?.CancelAutoPlacement(request.TrayId);
                return;
            }
            if (autoPlacement != null)
            {
                controller.CancelAutoPlacement(request.TrayId);
                return;
            }

            SyncTray();
            if (!trayItemViews.TryGetValue(request.TrayId, out ItemView view) || view == null)
            {
                controller.CancelAutoPlacement(request.TrayId);
                Debug.LogWarning($"No pooled ItemView is available for auto placement '{request.Definition?.id}'.", this);
                return;
            }

            view.SetPresentationLocked(true);
            Vector2 targetLocal = DefinitionToLocal(request.Definition, request.TargetAnchor, controller.Model);
            autoPlacement = new AutoPlacementAnimation
            {
                Request = request,
                View = view,
                StartWorld = view.RectTransform.position,
                TargetWorld = gridRoot.TransformPoint(targetLocal),
                Elapsed = 0f
            };
            SetTargetHighlight(request.Definition, request.TargetAnchor);
        }

        void HandlePlacementCompleted(BackpackPlacementResult result)
        {
            if (result == null)
                return;

            if (result.TrayItem != null)
            {
                SyncTray();
                if (!trayItemViews.TryGetValue(result.TrayItem.TrayId, out ItemView trayView) || trayView == null)
                    return;
                if (result.Kind == BackpackPlacementKind.TrayMerge)
                    trayView.PlayMergeFeedback(result.TrayItem.Definition != null
                        ? result.TrayItem.Definition.mergeLevel
                        : 2);
                else
                    trayView.PlayPlacementFeedback();
                return;
            }

            if (result.Item == null)
                return;
            SyncGridItems();
            if (!gridItemViews.TryGetValue(result.Item.InstanceId, out ItemView view) || view == null)
                return;
            if (result.Kind == BackpackPlacementKind.Merge)
                view.PlayMergeFeedback(result.Item.Definition != null ? result.Item.Definition.mergeLevel : 2);
            else
                view.PlayPlacementFeedback();
        }

        void HandleFishCycleCompleted(BackpackItemInstance item)
        {
            if (item != null && gridItemViews.TryGetValue(item.InstanceId, out ItemView view) && view != null)
                view.PlayFishCycleFeedback();
        }

        void HandleCatChargeChanged(BackpackItemInstance item, bool summoned)
        {
            if (item != null && gridItemViews.TryGetValue(item.InstanceId, out ItemView view) && view != null)
                view.PlayCatChargeFeedback(summoned);
        }

        void SyncAll()
        {
            if (controller == null)
                return;
            SyncGridCells();
            SyncGridItems();
            SyncTray();
        }

        void SyncGridCells()
        {
            if (controller == null)
                return;
            BackpackGridModel model = controller.Model;
            for (int i = 0; i < gridCells.Length; i++)
            {
                Image image = gridCells[i];
                if (image == null)
                    continue;
                ApplyGridCellSprite(image);
                Vector2Int cell = new Vector2Int(i % model.Columns, i / model.Columns);
                image.gameObject.SetActive(model.IsCellUnlocked(cell));
                image.color = highlightedCells.Contains(cell)
                    ? validTargetColor
                    : GetGridCellBaseColor(i, image);
            }
        }

        Sprite ResolveGridCellSprite()
        {
            if (gridCellSprite != null)
                return gridCellSprite;
            gridCellSprite = Resources.Load<Sprite>(DefaultGridCellSpriteResourcePath);
            if (gridCellSprite != null)
                return gridCellSprite;
            Sprite[] sprites = Resources.LoadAll<Sprite>(DefaultGridCellSpriteResourcePath);
            gridCellSprite = sprites != null && sprites.Length > 0 ? sprites[0] : null;
            return gridCellSprite;
        }

        void ApplyGridCellSprites()
        {
            for (int i = 0; i < gridCells.Length; i++)
                ApplyGridCellSprite(gridCells[i]);
        }

        void ApplyGridCellSprite(Image image)
        {
            if (image == null)
                return;
            Sprite sprite = ResolveGridCellSprite();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.enabled = sprite != null;
        }

        void SyncGridItems()
        {
            if (controller == null || gridItemRoot == null)
                return;
            BackpackGridModel model = controller.Model;
            removeIds.Clear();
            foreach (int id in gridItemViews.Keys)
                if (!model.Items.ContainsKey(id))
                    removeIds.Add(id);
            for (int i = 0; i < removeIds.Count; i++)
                Release(gridItemViews, removeIds[i]);

            foreach (KeyValuePair<int, BackpackItemInstance> pair in model.Items)
            {
                BackpackItemInstance item = pair.Value;
                if (!gridItemViews.TryGetValue(pair.Key, out ItemView view) || view == null)
                {
                    view = Acquire(item.Definition, gridItemRoot);
                    if (view == null)
                        continue;
                    view.SetTargetPosition(Vector2.zero, true);
                    gridItemViews[pair.Key] = view;
                }
                view.BindInteraction(this, ItemViewOrigin.Grid, pair.Key);
                view.SetTargetPosition(ItemToLocal(item, model));
                view.SetCooldown(item.CooldownRemaining, item.Definition != null ? item.Definition.cooldown : 0f);
                view.SetActivation(item.ActivationProgress, item.Definition != null ? item.Definition.fishCost : 0f);
            }
        }

        void SyncTray()
        {
            if (controller == null || trayRoot == null)
                return;
            IReadOnlyList<BackpackTrayItem> items = controller.TrayItems;
            removeIds.Clear();
            foreach (int id in trayItemViews.Keys)
            {
                bool found = false;
                for (int i = 0; i < items.Count; i++)
                    found |= items[i] != null && items[i].TrayId == id;
                if (!found)
                    removeIds.Add(id);
            }
            for (int i = 0; i < removeIds.Count; i++)
                Release(trayItemViews, removeIds[i]);

            int validCount = 0;
            float totalWidth = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                BackpackTrayItem item = items[i];
                if (item == null || item.Definition == null)
                    continue;
                totalWidth += ItemView.CalculateLayoutSize(
                    item.Definition,
                    cellSize,
                    cellGap).x;
                validCount++;
            }
            totalWidth += Mathf.Max(0, validCount - 1) * traySpacing;
            float cursorX = -totalWidth * 0.5f;
            for (int i = 0; i < items.Count; i++)
            {
                BackpackTrayItem item = items[i];
                if (item == null || item.Definition == null)
                    continue;
                float itemWidth = ItemView.CalculateLayoutSize(
                    item.Definition,
                    cellSize,
                    cellGap).x;
                if (!trayItemViews.TryGetValue(item.TrayId, out ItemView view) || view == null)
                {
                    view = Acquire(item.Definition, trayRoot);
                    if (view == null)
                        continue;
                    view.SetTargetPosition(new Vector2(0f, -80f), true);
                    trayItemViews[item.TrayId] = view;
                }
                else if (view.Definition != item.Definition)
                {
                    view.Configure(item.Definition, cellSize, cellGap);
                }
                view.BindInteraction(this, ItemViewOrigin.Tray, item.TrayId);
                float cooldown = item.HasRuntimeState
                    ? item.CooldownRemaining
                    : Mathf.Max(0f, item.Definition.cooldown);
                float activation = item.HasRuntimeState ? item.ActivationProgress : 0f;
                view.SetCooldown(cooldown, item.Definition.cooldown);
                view.SetActivation(activation, item.Definition.fishCost);
                if (!item.IsAutoPlacing)
                    view.SetTargetPosition(new Vector2(cursorX + itemWidth * 0.5f, 0f));
                cursorX += itemWidth + traySpacing;
            }
        }

        ItemView Acquire(ItemDefinition definition, RectTransform parent)
        {
            for (int i = 0; i < itemPool.Length; i++)
            {
                ItemView view = itemPool[i];
                if (view == null || view.gameObject.activeSelf)
                    continue;
                RectTransform rect = view.transform as RectTransform;
                rect.SetParent(parent, false);
                view.gameObject.SetActive(true);
                view.Configure(definition, cellSize, cellGap);
                return view;
            }
            Debug.LogWarning("Backpack ItemPool exhausted. Increase the prebuilt pool in SampleScene.", this);
            return null;
        }

        public bool BeginItemDrag(ItemView view)
        {
            if (DragBlocked || autoPlacement != null || draggedView != null || view == null || view.Definition == null)
                return false;
            if (!AllowsDragInput())
            {
                dragInput.Release();
                return false;
            }

            draggedView = view;
            ResolveDragInfoView();
            dragInfoView?.Configure(unitCatalog);
            dragInfoView?.Show(view.Definition);
            return true;
        }

        bool DragBlocked => !isActiveAndEnabled || !applicationFocused || controller == null || dragInput == null;

        bool AllowsDragInput()
        {
            dragInput.SetPaused(controller.Clock != null && controller.Clock.IsPaused);
            return dragInput.AllowsDrag();
        }

        internal void CancelItemDrag(ItemView view)
        {
            if (draggedView == view)
                CancelActiveDrag();
        }

        void CancelActiveDrag()
        {
            if (draggedView == null)
                return;
            ItemView view = draggedView;
            draggedView = null;
            dragInput?.Release();
            view.CancelDrag();
            dragInfoView?.Hide();
        }

        public void EndItemDrag(ItemView view, PointerEventData eventData)
        {
            if (view == null || draggedView != view)
                return;
            if (DragBlocked || eventData == null || !AllowsDragInput())
            {
                CancelActiveDrag();
                return;
            }

            draggedView = null;
            dragInput.Release();
            dragInfoView?.Hide();

            bool success = false;
            if (IsScreenPointInTray(eventData.position, eventData.pressEventCamera))
            {
                TryGetTrayTarget(view, eventData.position, eventData.pressEventCamera, out int targetTrayId);
                if (view.Origin == ItemViewOrigin.Grid)
                {
                    if (targetTrayId != 0)
                        success = controller.TryMergeGridIntoTray(view.SourceId, targetTrayId, out _);
                    if (!success)
                        success = controller.TryReturnToTray(view.SourceId, out _);
                }
                else if (view.Origin == ItemViewOrigin.Tray && targetTrayId != 0)
                {
                    success = controller.TryMergeTray(view.SourceId, targetTrayId, out _);
                }
            }
            else if (TryScreenToGrid(
                    eventData.position,
                    eventData.pressEventCamera,
                    view.Definition,
                    out Vector2Int pointerCell,
                    out Vector2Int anchor))
            {
                BackpackGridModel model = controller.Model;
                bool hasTarget = model.TryGetItemAt(pointerCell, out BackpackItemInstance target) && target != null;
                if (view.Origin == ItemViewOrigin.Grid)
                {
                    if (hasTarget && target.InstanceId != view.SourceId)
                        success = controller.TryMerge(view.SourceId, target.InstanceId, out _);
                    if (!success)
                        success = controller.TryMove(view.SourceId, anchor);
                }
                else if (view.Origin == ItemViewOrigin.Tray)
                {
                    if (hasTarget)
                        success = controller.TryMergeFromTray(view.SourceId, target.InstanceId, out _);
                    if (!success)
                        success = controller.TryPlaceFromTray(view.SourceId, anchor);
                }
            }

            if (!success)
                SyncAll();
        }

        void ResolveDragInfoView()
        {
            var scene = gameObject.scene;
            if (dragInfoView != null && dragInfoView.gameObject.scene == scene)
                return;

            dragInfoView = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                dragInfoView = root.GetComponentInChildren<CardDragInfoView>(true);
                if (dragInfoView != null)
                    return;
            }
        }

        bool IsScreenPointInTray(Vector2 screenPosition, Camera eventCamera) =>
            trayRoot != null &&
            RectTransformUtility.RectangleContainsScreenPoint(trayRoot, screenPosition, eventCamera);

        bool TryGetTrayTarget(
            ItemView source,
            Vector2 screenPosition,
            Camera eventCamera,
            out int trayId)
        {
            trayId = 0;
            float bestDistance = float.MaxValue;
            foreach (KeyValuePair<int, ItemView> pair in trayItemViews)
            {
                ItemView candidate = pair.Value;
                if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                    (source.Origin == ItemViewOrigin.Tray && pair.Key == source.SourceId) ||
                    !RectTransformUtility.RectangleContainsScreenPoint(
                        candidate.RectTransform,
                        screenPosition,
                        eventCamera))
                    continue;

                Vector2 candidateScreen = RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    candidate.RectTransform.position);
                float distance = (candidateScreen - screenPosition).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                trayId = pair.Key;
            }
            return trayId != 0;
        }

        bool TryScreenToGrid(
            Vector2 screenPosition,
            Camera eventCamera,
            ItemDefinition definition,
            out Vector2Int pointerCell,
            out Vector2Int anchor)
        {
            pointerCell = default;
            anchor = default;
            if (controller == null || gridRoot == null || definition == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(gridRoot, screenPosition, eventCamera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    gridRoot,
                    screenPosition,
                    eventCamera,
                    out Vector2 localPoint))
                return false;

            BackpackGridModel model = controller.Model;
            float pitch = cellSize + cellGap;
            Vector2 gridPoint = new Vector2(
                localPoint.x / pitch + (model.Columns - 1) * 0.5f,
                localPoint.y / pitch + (model.Rows - 1) * 0.5f);
            pointerCell = new Vector2Int(
                Mathf.RoundToInt(gridPoint.x),
                Mathf.RoundToInt(gridPoint.y));

            Vector2 shapeCenter = GetShapeBoundsCenter(definition.EffectiveCells);
            Vector2 anchorPoint = gridPoint - shapeCenter;
            anchor = new Vector2Int(
                Mathf.RoundToInt(anchorPoint.x),
                Mathf.RoundToInt(anchorPoint.y));

            return pointerCell.x >= 0 && pointerCell.x < model.Columns &&
                   pointerCell.y >= 0 && pointerCell.y < model.Rows;
        }

        void Release(Dictionary<int, ItemView> views, int id)
        {
            if (views.TryGetValue(id, out ItemView view) && view != null)
                view.ResetForPool(itemPoolRoot);
            views.Remove(id);
        }

        void ResetPoolForRuntime()
        {
            if (autoPlacement != null && controller != null && autoPlacement.Request != null)
                controller.CancelAutoPlacement(autoPlacement.Request.TrayId);
            autoPlacement = null;
            highlightedCells.Clear();
            gridItemViews.Clear();
            trayItemViews.Clear();
            for (int i = 0; i < itemPool.Length; i++)
                if (itemPool[i] != null)
                    itemPool[i].ResetForPool(itemPoolRoot);
        }

        Vector2 ItemToLocal(BackpackItemInstance item, BackpackGridModel model)
        {
            return DefinitionToLocal(item.Definition, item.Anchor, model);
        }

        Vector2 DefinitionToLocal(ItemDefinition definition, Vector2Int anchor, BackpackGridModel model)
        {
            Vector2 center = (Vector2)anchor + GetShapeBoundsCenter(definition.EffectiveCells);
            float pitch = cellSize + cellGap;
            return new Vector2((center.x - (model.Columns - 1) * 0.5f) * pitch,
                (center.y - (model.Rows - 1) * 0.5f) * pitch);
        }

        static Vector2 GetShapeBoundsCenter(IReadOnlyList<Vector2Int> cells)
        {
            if (cells == null || cells.Count == 0)
                return Vector2.zero;

            int minX = cells[0].x;
            int maxX = cells[0].x;
            int minY = cells[0].y;
            int maxY = cells[0].y;
            for (int i = 1; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                minX = Mathf.Min(minX, cell.x);
                maxX = Mathf.Max(maxX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxY = Mathf.Max(maxY, cell.y);
            }

            return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        }

        void TickAutoPlacement(float deltaTime)
        {
            if (autoPlacement == null)
                return;

            AutoPlacementAnimation animation = autoPlacement;
            if (animation.View == null || animation.Request == null || controller == null)
            {
                CancelAutoPlacementAnimation();
                return;
            }

            animation.Elapsed += Mathf.Max(0f, deltaTime);
            float normalized = Mathf.Clamp01(animation.Elapsed / Mathf.Max(0.1f, autoFlightDuration));
            float eased = normalized * normalized * (3f - 2f * normalized);
            animation.View.SetPresentationWorldPosition(Vector3.Lerp(
                animation.StartWorld,
                animation.TargetWorld,
                eased));
            if (normalized < 1f)
                return;

            int trayId = animation.Request.TrayId;
            autoPlacement = null;
            ClearTargetHighlight();
            if (!controller.TryCommitAutoPlacement(trayId, out _))
            {
                animation.View.SetPresentationLocked(false);
                SyncAll();
            }
        }

        void CancelAutoPlacementAnimation()
        {
            if (autoPlacement != null && controller != null && autoPlacement.Request != null)
                controller.CancelAutoPlacement(autoPlacement.Request.TrayId);
            if (autoPlacement != null && autoPlacement.View != null)
                autoPlacement.View.SetPresentationLocked(false);
            autoPlacement = null;
            ClearTargetHighlight();
            SyncAll();
        }

        void SetTargetHighlight(ItemDefinition definition, Vector2Int anchor)
        {
            highlightedCells.Clear();
            if (definition != null)
                foreach (Vector2Int offset in definition.EffectiveCells)
                    highlightedCells.Add(anchor + offset);
            RefreshGridCellVisuals();
        }

        void ClearTargetHighlight()
        {
            highlightedCells.Clear();
            RefreshGridCellVisuals();
        }

        void CacheGridCellColors()
        {
            gridCellBaseColors = new Color[gridCells != null ? gridCells.Length : 0];
            for (int i = 0; i < gridCellBaseColors.Length; i++)
                gridCellBaseColors[i] = gridCells[i] != null ? gridCells[i].color : Color.white;
        }

        Color GetGridCellBaseColor(int index, Image image)
        {
            if (gridCellBaseColors == null || gridCellBaseColors.Length != gridCells.Length)
                CacheGridCellColors();
            return index >= 0 && index < gridCellBaseColors.Length
                ? gridCellBaseColors[index]
                : image.color;
        }

        void RefreshGridCellVisuals()
        {
            if (controller == null)
                return;
            BackpackGridModel model = controller.Model;
            for (int i = 0; i < gridCells.Length; i++)
            {
                Image image = gridCells[i];
                if (image == null)
                    continue;
                Vector2Int cell = new Vector2Int(i % model.Columns, i / model.Columns);
                bool highlighted = highlightedCells.Contains(cell);
                image.gameObject.SetActive(highlighted || model.IsCellUnlocked(cell));
                image.color = highlighted ? validTargetColor : GetGridCellBaseColor(i, image);
            }
        }
    }
}
