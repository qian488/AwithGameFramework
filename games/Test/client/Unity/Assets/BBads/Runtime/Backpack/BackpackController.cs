using System;
using System.Collections.Generic;
using BBads.Combat;
using BBads.Data;
using BBads.Timeline;
using Test.Application.BBads.Session;
using UnityEngine;

namespace BBads.Backpack
{
    public sealed class BackpackTrayItem
    {
        public int TrayId { get; internal set; }
        public ItemDefinition Definition { get; internal set; }
        public bool HasScheduledPlacement { get; internal set; }
        public float PlaceAt { get; internal set; }
        public Vector2Int TargetAnchor { get; internal set; }
        public bool IsAutoPlacing { get; internal set; }
        public bool HasRuntimeState { get; internal set; }
        public float CooldownRemaining { get; internal set; }
        public float ActivationProgress { get; internal set; }
    }

    public enum BackpackPlacementKind
    {
        Place,
        Merge,
        UnlockSlot,
        ReturnToTray,
        TrayMerge
    }

    public sealed class BackpackPlacementRequest
    {
        public int TrayId { get; internal set; }
        public ItemDefinition Definition { get; internal set; }
        public Vector2Int TargetAnchor { get; internal set; }
        public BackpackPlacementKind Kind { get; internal set; }
        public int TargetInstanceId { get; internal set; }
    }

    public sealed class BackpackPlacementResult
    {
        public int TrayId { get; internal set; }
        public BackpackPlacementKind Kind { get; internal set; }
        public Vector2Int TargetAnchor { get; internal set; }
        public int SourceInstanceId { get; internal set; }
        public int TargetInstanceId { get; internal set; }
        public BackpackItemInstance Item { get; internal set; }
        public BackpackTrayItem TrayItem { get; internal set; }
    }

    public sealed class BackpackController : MonoBehaviour
    {
        [SerializeField, Min(1)] int columns = 7;
        [SerializeField, Min(1)] int rows = 6;
        [SerializeField, Min(1)] int initialUnlockedColumns = 3;
        [SerializeField, Min(1)] int initialUnlockedRows = 3;
        [SerializeField] bool autoPlaceItems = true;
        [SerializeField] bool autoMergeItems = true;
        [SerializeField] ItemCatalog itemCatalog;
        [SerializeField] ScenarioRunner scenarioRunner;

        readonly List<BackpackItemInstance> readyBuffer = new List<BackpackItemInstance>();
        readonly List<BackpackTrayItem> trayItems = new List<BackpackTrayItem>();
        BackpackGridModel model;
        SummonPositionSequence summonPositions;
        ScenarioDefinition boundScenario;
        int nextTrayId = 1;
        int activeAutoPlacementTrayId;

        public event Action<BackpackItemInstance> GridChanged;
        public event Action TrayChanged;
        public event Action GridLayoutChanged;
        public event Action<string, Vector2> SummonRequested;
        public event Action<BackpackPlacementRequest> AutoPlacementRequested;
        public event Action<BackpackPlacementResult> PlacementCompleted;
        public event Action<BackpackItemInstance> FishCycleCompleted;
        public event Action<BackpackItemInstance, bool> CatChargeChanged;

        public BackpackGridModel Model => EnsureModel();
        public IReadOnlyList<BackpackTrayItem> TrayItems => trayItems;
        public bool AutoPlaceItems => autoPlaceItems;
        public bool AutoMergeItems => autoMergeItems;
        public BBadsBattleClock Clock { get; private set; }
        internal void SetClock(BBadsBattleClock value) => Clock = value;

        public void SetItemCatalog(ItemCatalog value) => itemCatalog = value;
        public void SetAutoPlaceItems(bool value) => autoPlaceItems = value;
        public void SetAutoMergeItems(bool value) => autoMergeItems = value;

        public void ConfigureScenarioRunner(ScenarioRunner value)
        {
            if (scenarioRunner != null)
                scenarioRunner.ItemSpawnRequested -= HandleItemSpawn;
            scenarioRunner = value;
            if (isActiveAndEnabled && scenarioRunner != null)
                scenarioRunner.ItemSpawnRequested += HandleItemSpawn;
            ResetSummonPositions();
        }

        void OnEnable()
        {
            if (scenarioRunner != null)
                scenarioRunner.ItemSpawnRequested += HandleItemSpawn;
            ResetSummonPositions();
        }

        void OnDisable()
        {
            if (scenarioRunner != null)
                scenarioRunner.ItemSpawnRequested -= HandleItemSpawn;
        }

        void Update()
        {
            float delta = Clock != null ? Clock.Delta : Time.deltaTime;
            if (delta <= 0f)
                return;
            if (scenarioRunner != null && scenarioRunner.Scenario != boundScenario)
                ResetSummonPositions();
            ProcessTray();
            readyBuffer.Clear();
            EnsureModel().Tick(delta, readyBuffer);
            for (int i = 0; i < readyBuffer.Count; i++)
            {
                ItemDefinition definition = readyBuffer[i].Definition;
                if (definition == null || definition.type != ItemType.Fish) continue;
                FishCycleCompleted?.Invoke(readyBuffer[i]);
                ActivateAdjacentCats(readyBuffer[i], definition.activationAmount);
            }
        }

        public bool TryPlaceNew(string itemId, Vector2Int anchor, out BackpackItemInstance instance)
        {
            instance = null;
            if (itemCatalog == null || !itemCatalog.TryGet(itemId, out ItemDefinition definition))
                return false;
            bool success = EnsureModel().TryPlaceNew(definition, anchor, out instance);
            if (success)
                GridChanged?.Invoke(instance);
            return success;
        }

        public bool TryMove(int instanceId, Vector2Int anchor)
        {
            bool success = EnsureModel().TryMove(instanceId, anchor);
            if (success && model.TryGetItem(instanceId, out BackpackItemInstance item))
            {
                GridChanged?.Invoke(item);
                PlacementCompleted?.Invoke(new BackpackPlacementResult
                {
                    Kind = BackpackPlacementKind.Place,
                    TargetAnchor = anchor,
                    SourceInstanceId = instanceId,
                    Item = item
                });
            }
            return success;
        }

        public bool TryMerge(int sourceInstanceId, int targetInstanceId, out BackpackItemInstance upgraded)
        {
            Vector2Int targetAnchor = default;
            if (EnsureModel().TryGetItem(targetInstanceId, out BackpackItemInstance target))
                targetAnchor = target.Anchor;
            bool success = EnsureModel().TryMerge(sourceInstanceId, targetInstanceId, itemCatalog, out upgraded);
            if (success)
            {
                GridChanged?.Invoke(upgraded);
                PlacementCompleted?.Invoke(new BackpackPlacementResult
                {
                    Kind = BackpackPlacementKind.Merge,
                    TargetAnchor = targetAnchor,
                    SourceInstanceId = sourceInstanceId,
                    TargetInstanceId = targetInstanceId,
                    Item = upgraded
                });
            }
            return success;
        }

        public bool TryPlaceFromTray(int trayId, Vector2Int anchor)
        {
            int trayIndex = FindTrayIndex(trayId);
            if (trayIndex < 0)
                return false;

            BackpackTrayItem trayItem = trayItems[trayIndex];
            ItemDefinition definition = trayItem.Definition;
            if (definition == null)
                return false;

            BackpackItemInstance placed = null;
            bool success;
            if (definition.type == ItemType.Slot)
            {
                success = EnsureModel().TryUnlockSlotAt(definition, anchor, out _);
                if (success)
                    GridLayoutChanged?.Invoke();
            }
            else
            {
                success = EnsureModel().TryPlaceNew(definition, anchor, out placed);
                if (success)
                    RestoreRuntimeState(trayItem, placed);
            }

            if (!success)
                return false;
            trayItems.RemoveAt(trayIndex);
            TrayChanged?.Invoke();
            if (placed != null)
                GridChanged?.Invoke(placed);
            PlacementCompleted?.Invoke(new BackpackPlacementResult
            {
                TrayId = trayId,
                Kind = definition.type == ItemType.Slot
                    ? BackpackPlacementKind.UnlockSlot
                    : BackpackPlacementKind.Place,
                TargetAnchor = anchor,
                Item = placed
            });
            return true;
        }

        public bool TryReturnToTray(int instanceId, out BackpackTrayItem returned)
        {
            returned = null;
            if (!EnsureModel().TryGetItem(instanceId, out BackpackItemInstance item) ||
                item == null || item.Definition == null ||
                !model.Remove(instanceId))
                return false;

            returned = new BackpackTrayItem
            {
                TrayId = nextTrayId++,
                Definition = item.Definition,
                HasScheduledPlacement = false,
                IsAutoPlacing = false,
                HasRuntimeState = true,
                CooldownRemaining = item.CooldownRemaining,
                ActivationProgress = item.ActivationProgress
            };
            trayItems.Add(returned);
            GridChanged?.Invoke(null);
            TrayChanged?.Invoke();
            PlacementCompleted?.Invoke(new BackpackPlacementResult
            {
                TrayId = returned.TrayId,
                Kind = BackpackPlacementKind.ReturnToTray,
                SourceInstanceId = instanceId,
                TrayItem = returned
            });
            return true;
        }

        public bool TryMergeTray(int sourceTrayId, int targetTrayId, out BackpackTrayItem upgraded)
        {
            upgraded = null;
            if (sourceTrayId == targetTrayId)
                return false;

            int sourceIndex = FindTrayIndex(sourceTrayId);
            int targetIndex = FindTrayIndex(targetTrayId);
            if (sourceIndex < 0 || targetIndex < 0)
                return false;

            BackpackTrayItem source = trayItems[sourceIndex];
            BackpackTrayItem target = trayItems[targetIndex];
            if (source.IsAutoPlacing || target.IsAutoPlacing ||
                !TryResolveMerge(source.Definition, target.Definition, out ItemDefinition next))
                return false;

            ResetTrayItemAsUpgrade(target, next);
            trayItems.RemoveAt(sourceIndex);
            upgraded = target;
            TrayChanged?.Invoke();
            PlacementCompleted?.Invoke(new BackpackPlacementResult
            {
                TrayId = target.TrayId,
                Kind = BackpackPlacementKind.TrayMerge,
                TrayItem = target
            });
            return true;
        }

        public bool TryMergeGridIntoTray(
            int sourceInstanceId,
            int targetTrayId,
            out BackpackTrayItem upgraded)
        {
            upgraded = null;
            int targetIndex = FindTrayIndex(targetTrayId);
            if (targetIndex < 0 ||
                !EnsureModel().TryGetItem(sourceInstanceId, out BackpackItemInstance source) ||
                source == null || source.Definition == null)
                return false;

            BackpackTrayItem target = trayItems[targetIndex];
            if (target.IsAutoPlacing ||
                !TryResolveMerge(source.Definition, target.Definition, out ItemDefinition next) ||
                !model.Remove(sourceInstanceId))
                return false;

            ResetTrayItemAsUpgrade(target, next);
            upgraded = target;
            GridChanged?.Invoke(null);
            TrayChanged?.Invoke();
            PlacementCompleted?.Invoke(new BackpackPlacementResult
            {
                TrayId = target.TrayId,
                Kind = BackpackPlacementKind.TrayMerge,
                SourceInstanceId = sourceInstanceId,
                TrayItem = target
            });
            return true;
        }

        public bool TryMergeFromTray(
            int trayId,
            int targetInstanceId,
            out BackpackItemInstance upgraded)
        {
            upgraded = null;
            int trayIndex = FindTrayIndex(trayId);
            if (trayIndex < 0)
                return false;

            BackpackTrayItem trayItem = trayItems[trayIndex];
            if (trayItem.Definition == null || trayItem.Definition.type == ItemType.Slot ||
                !EnsureModel().TryMergeNew(trayItem.Definition, targetInstanceId, itemCatalog, out upgraded))
                return false;

            trayItems.RemoveAt(trayIndex);
            TrayChanged?.Invoke();
            GridChanged?.Invoke(upgraded);
            PlacementCompleted?.Invoke(new BackpackPlacementResult
            {
                TrayId = trayId,
                Kind = BackpackPlacementKind.Merge,
                TargetAnchor = upgraded.Anchor,
                TargetInstanceId = targetInstanceId,
                Item = upgraded
            });
            return true;
        }

        public bool TryCommitAutoPlacement(int trayId, out BackpackPlacementResult result)
        {
            result = null;
            int trayIndex = FindTrayIndex(trayId);
            if (trayIndex < 0 || !TryBuildPlacementRequest(
                    trayItems[trayIndex],
                    autoMergeItems,
                    out BackpackPlacementRequest request))
            {
                if (trayIndex >= 0)
                    trayItems[trayIndex].IsAutoPlacing = false;
                if (activeAutoPlacementTrayId == trayId)
                    activeAutoPlacementTrayId = 0;
                return false;
            }

            BackpackItemInstance placed = null;
            bool success;
            if (request.Kind == BackpackPlacementKind.UnlockSlot)
            {
                success = EnsureModel().TryUnlockSlotAt(request.Definition, request.TargetAnchor, out _);
                if (success)
                    GridLayoutChanged?.Invoke();
            }
            else if (request.Kind == BackpackPlacementKind.Merge)
            {
                success = EnsureModel().TryMergeNew(
                    request.Definition,
                    request.TargetInstanceId,
                    itemCatalog,
                    out placed);
            }
            else
            {
                success = EnsureModel().TryPlaceNew(request.Definition, request.TargetAnchor, out placed);
                if (success)
                    RestoreRuntimeState(trayItems[trayIndex], placed);
            }

            if (!success)
            {
                trayItems[trayIndex].IsAutoPlacing = false;
                if (activeAutoPlacementTrayId == trayId)
                    activeAutoPlacementTrayId = 0;
                return false;
            }

            trayItems.RemoveAt(trayIndex);
            activeAutoPlacementTrayId = 0;
            result = new BackpackPlacementResult
            {
                TrayId = trayId,
                Kind = request.Kind,
                TargetAnchor = request.TargetAnchor,
                TargetInstanceId = request.TargetInstanceId,
                Item = placed
            };
            TrayChanged?.Invoke();
            if (placed != null)
                GridChanged?.Invoke(placed);
            PlacementCompleted?.Invoke(result);
            return true;
        }

        public void CancelAutoPlacement(int trayId)
        {
            int trayIndex = FindTrayIndex(trayId);
            if (trayIndex >= 0)
                trayItems[trayIndex].IsAutoPlacing = false;
            if (activeAutoPlacementTrayId == trayId)
                activeAutoPlacementTrayId = 0;
        }

        public void ResetRuntime()
        {
            model = CreateModel();
            trayItems.Clear();
            nextTrayId = 1;
            activeAutoPlacementTrayId = 0;
            ResetSummonPositions();
            TrayChanged?.Invoke();
            GridLayoutChanged?.Invoke();
        }

        BackpackGridModel EnsureModel() => model ??= CreateModel();

        BackpackGridModel CreateModel()
        {
            int unlockedWidth = Mathf.Clamp(initialUnlockedColumns, 1, columns);
            int unlockedHeight = Mathf.Clamp(initialUnlockedRows, 1, rows);
            int startX = (columns - unlockedWidth) / 2;
            int startY = (rows - unlockedHeight) / 2;
            var initial = new List<Vector2Int>(unlockedWidth * unlockedHeight);
            for (int y = 0; y < unlockedHeight; y++)
                for (int x = 0; x < unlockedWidth; x++)
                    initial.Add(new Vector2Int(startX + x, startY + y));
            return new BackpackGridModel(columns, rows, initial);
        }

        void ResetSummonPositions()
        {
            ScenarioDefinition scenario = scenarioRunner != null ? scenarioRunner.Scenario : null;
            summonPositions = scenario != null
                ? new SummonPositionSequence(scenario.SummonPositions)
                : null;
            boundScenario = scenario;
        }

        void HandleItemSpawn(ItemSpawnEvent spawnEvent)
        {
            if (spawnEvent == null || string.IsNullOrWhiteSpace(spawnEvent.itemId) || itemCatalog == null) return;
            if (!itemCatalog.TryGet(spawnEvent.itemId, out ItemDefinition definition)) return;
            trayItems.Add(new BackpackTrayItem
            {
                TrayId = nextTrayId++,
                Definition = definition,
                HasScheduledPlacement = spawnEvent.HasScheduledPlacement,
                PlaceAt = spawnEvent.placeTime,
                TargetAnchor = new Vector2Int(spawnEvent.x, spawnEvent.y)
            });
            TrayChanged?.Invoke();
        }

        void ProcessTray()
        {
            if (!autoPlaceItems || trayItems.Count == 0 || itemCatalog == null || activeAutoPlacementTrayId != 0)
                return;
            for (int i = 0; i < trayItems.Count; i++)
            {
                BackpackTrayItem trayItem = trayItems[i];
                if (trayItem == null || trayItem.Definition == null || trayItem.IsAutoPlacing ||
                    !trayItem.HasScheduledPlacement || scenarioRunner == null ||
                    scenarioRunner.Elapsed < trayItem.PlaceAt)
                    continue;

                if (!TryBuildPlacementRequest(trayItem, autoMergeItems, out BackpackPlacementRequest request))
                    continue;
                trayItem.IsAutoPlacing = true;
                activeAutoPlacementTrayId = trayItem.TrayId;
                if (AutoPlacementRequested != null)
                    AutoPlacementRequested.Invoke(request);
                else
                    TryCommitAutoPlacement(trayItem.TrayId, out _);
                return;
            }
        }

        bool TryBuildPlacementRequest(
            BackpackTrayItem trayItem,
            bool allowMerge,
            out BackpackPlacementRequest request)
        {
            request = null;
            if (trayItem == null || trayItem.Definition == null)
                return false;

            BackpackGridModel grid = EnsureModel();
            BackpackPlacementKind kind;
            int targetInstanceId = 0;
            Vector2Int targetAnchor = trayItem.TargetAnchor;
            if (trayItem.Definition.type == ItemType.Slot)
            {
                if (!grid.CanUnlockSlotAt(trayItem.Definition, targetAnchor))
                    return false;
                kind = BackpackPlacementKind.UnlockSlot;
            }
            else if (grid.TryGetItemAt(targetAnchor, out BackpackItemInstance target) && target != null)
            {
                if (!allowMerge ||
                    !grid.CanMergeNew(trayItem.Definition, target.InstanceId, itemCatalog, out _))
                    return false;
                kind = BackpackPlacementKind.Merge;
                targetInstanceId = target.InstanceId;
                targetAnchor = target.Anchor;
            }
            else
            {
                if (!grid.CanPlace(trayItem.Definition, targetAnchor))
                    return false;
                kind = BackpackPlacementKind.Place;
            }

            request = new BackpackPlacementRequest
            {
                TrayId = trayItem.TrayId,
                Definition = trayItem.Definition,
                TargetAnchor = targetAnchor,
                Kind = kind,
                TargetInstanceId = targetInstanceId
            };
            return true;
        }

        bool TryResolveMerge(
            ItemDefinition source,
            ItemDefinition target,
            out ItemDefinition next)
        {
            next = null;
            return source != null && target != null && itemCatalog != null &&
                   source.type != ItemType.Slot && target.type != ItemType.Slot &&
                   source.mergeLevel == target.mergeLevel &&
                   !string.IsNullOrWhiteSpace(source.mergeGroup) &&
                   string.Equals(source.mergeGroup, target.mergeGroup, StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(target.nextItemId) &&
                   itemCatalog.TryGet(target.nextItemId, out next);
        }

        static void ResetTrayItemAsUpgrade(BackpackTrayItem trayItem, ItemDefinition definition)
        {
            trayItem.Definition = definition;
            trayItem.HasScheduledPlacement = false;
            trayItem.IsAutoPlacing = false;
            trayItem.HasRuntimeState = false;
            trayItem.CooldownRemaining = 0f;
            trayItem.ActivationProgress = 0f;
        }

        static void RestoreRuntimeState(BackpackTrayItem trayItem, BackpackItemInstance placed)
        {
            if (trayItem == null || placed == null || !trayItem.HasRuntimeState)
                return;

            float cycle = placed.Definition != null ? Mathf.Max(0f, placed.Definition.cooldown) : 0f;
            placed.CooldownRemaining = cycle > 0f
                ? Mathf.Clamp(trayItem.CooldownRemaining, 0f, cycle)
                : 0f;
            placed.ActivationProgress = Mathf.Max(0f, trayItem.ActivationProgress);
        }

        int FindTrayIndex(int trayId)
        {
            for (int i = 0; i < trayItems.Count; i++)
                if (trayItems[i] != null && trayItems[i].TrayId == trayId)
                    return i;
            return -1;
        }

        void ActivateAdjacentCats(BackpackItemInstance fish, float amount)
        {
            if (fish == null || amount <= 0f) return;
            foreach (BackpackItemInstance target in EnsureModel().GetAdjacentItems(fish))
            {
                if (target == null || target.Definition == null || target.Definition.type != ItemType.Cat) continue;
                var charge = BBadsSummonCharge.Apply(target.ActivationProgress, amount, target.Definition.fishCost);
                target.ActivationProgress = charge.Remaining;
                bool summoned = false;
                for (int i = 0; i < charge.Summons; i++)
                {
                    if (summonPositions != null &&
                        !string.IsNullOrWhiteSpace(target.Definition.summonUnitId) &&
                        summonPositions.TryTakeNext(out Vector2 position))
                    {
                        SummonRequested?.Invoke(target.Definition.summonUnitId, position);
                        summoned = true;
                    }
                }
                GridChanged?.Invoke(target);
                CatChargeChanged?.Invoke(target, summoned);
            }
        }
    }
}
