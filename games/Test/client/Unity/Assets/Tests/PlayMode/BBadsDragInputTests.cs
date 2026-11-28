using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwithGameFrame.Gameplay.Input;
using BBads.Backpack;
using BBads.Timeline;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    public sealed class BBadsDragInputTests
    {
        static readonly Vector2Int FishAnchor = new Vector2Int(3, 2);
        static readonly Vector2Int CatAnchor = new Vector2Int(4, 1);

        readonly List<BackpackPlacementKind> placements = new List<BackpackPlacementKind>();
        readonly List<string> summons = new List<string>();

        BackpackController backpack;
        BackpackPresenter presenter;
        CardDragInfoView card;
        EventSystem eventSystem;
        StandaloneInputModule inputModule;
        MouseInput mouse;
        GraphicRaycaster raycaster;
        RectTransform grid;
        RectTransform tray;
        Scene overlappingBattle;

        [UnitySetUp]
        public IEnumerator OpenBattle()
        {
            placements.Clear();
            summons.Clear();
            yield return SceneManager.LoadSceneAsync("BBadsBattle");
            backpack = Object.FindFirstObjectByType<BackpackController>();
            presenter = Object.FindFirstObjectByType<BackpackPresenter>();
            card = Object.FindFirstObjectByType<CardDragInfoView>(FindObjectsInactive.Include);
            var runner = Object.FindFirstObjectByType<ScenarioRunner>();
            backpack.SetAutoPlaceItems(false);
            backpack.PlacementCompleted += result => placements.Add(result.Kind);
            backpack.SummonRequested += (id, _) => summons.Add(id);

            eventSystem = EventSystem.current;
            Assert.That(eventSystem.gameObject.scene.name, Is.EqualTo("BBadsBattle"));
            inputModule = eventSystem.GetComponent<StandaloneInputModule>();
            Assert.That(inputModule, Is.Not.Null);
            mouse = eventSystem.gameObject.AddComponent<MouseInput>();
            inputModule.inputOverride = mouse;
            SetFocus(true);

            var rects = presenter.GetComponentsInChildren<RectTransform>(true);
            grid = rects.Single(rect => rect.name == "GridCells");
            tray = rects.Single(rect => rect.name == "Tray");
            raycaster = grid.GetComponentInParent<GraphicRaycaster>();
            Assert.That(raycaster, Is.Not.Null);
            Assert.That(raycaster.eventCamera, Is.Not.Null);
            yield return null;
            runner.Tick(9f);
            runner.Pause();
            Assert.That(backpack.TrayItems.Select(item => item.Definition.id),
                Is.EqualTo(new[] { "Fish1", "CatRocket1" }));
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            yield return Await(() => Mathf.Abs(fish.RectTransform.anchoredPosition.y) < 1f,
                "spawned items settle into the tray");
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator ReleaseInput()
        {
            if (inputModule != null)
                inputModule.inputOverride = null;
            if (mouse != null)
                Object.Destroy(mouse);
            if (overlappingBattle.IsValid() && overlappingBattle.isLoaded)
                yield return SceneManager.UnloadSceneAsync(overlappingBattle);
            overlappingBattle = default;
            yield return null;
        }

        [UnityTest]
        public IEnumerator LegalDropAndOccupiedDropPreserveItemsAndCells()
        {
            var fishTarget = ItemCenter(FishAnchor);
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            var cat = FindItem("CatRocket1", ItemViewOrigin.Tray);
            int catTrayId = cat.SourceId;

            Assert.That(Raycast(fishTarget).gameObject.name, Is.EqualTo("BG"),
                "empty grid resolves to the scene background graphic");
            Drag(fish, fishTarget);
            AssertOccupancy("Fish1", FishAnchor);
            Assert.That(backpack.Model.Items.Count, Is.EqualTo(1));
            Assert.That(placements, Is.EqualTo(new[] { BackpackPlacementKind.Place }));

            Drag(cat, fishTarget);
            AssertOccupancy("Fish1", FishAnchor);
            Assert.That(backpack.Model.Items.Count, Is.EqualTo(1));
            Assert.That(backpack.TrayItems.Single().TrayId, Is.EqualTo(catTrayId));
            Assert.That(backpack.TrayItems.Single().Definition.id, Is.EqualTo("CatRocket1"));
            Assert.That(placements.Count, Is.EqualTo(1));
            Assert.That(summons, Is.Empty);

            yield return Await(() => Mathf.Abs(cat.RectTransform.anchoredPosition.y) < 1f,
                "rejected cat returns to its tray slot");
        }

        [UnityTest]
        public IEnumerator AdjacentDropsSummonOnceAndReturningFishClearsBothCells()
        {
            Drag(FindItem("Fish1", ItemViewOrigin.Tray), ItemCenter(FishAnchor));
            Drag(FindItem("CatRocket1", ItemViewOrigin.Tray), ItemCenter(CatAnchor));
            AssertOccupancy("Fish1", FishAnchor);
            AssertOccupancy("CatRocket1", CatAnchor);
            Assert.That(backpack.TrayItems, Is.Empty);

            yield return Await(() => summons.Count > 0, "adjacent fish charges the cat and summons", 8f);
            Assert.That(summons, Is.EqualTo(new[] { "CatRocket1" }));

            var fish = FindItem("Fish1", ItemViewOrigin.Grid);
            var original = backpack.Model.Items[fish.SourceId];
            Drag(fish, ScreenCenter(tray));
            var returned = backpack.TrayItems.Single();
            Assert.That(returned.Definition.id, Is.EqualTo("Fish1"));
            Assert.That(returned.HasRuntimeState, Is.True);
            Assert.That(returned.CooldownRemaining, Is.EqualTo(original.CooldownRemaining));
            Assert.That(backpack.Model.TryGetItemAt(FishAnchor, out _), Is.False);
            Assert.That(backpack.Model.TryGetItemAt(FishAnchor + Vector2Int.up, out _), Is.False);
            AssertOccupancy("CatRocket1", CatAnchor);
            Assert.That(backpack.Model.Items.Count, Is.EqualTo(1));
            Assert.That(placements, Is.EqualTo(new[]
            {
                BackpackPlacementKind.Place, BackpackPlacementKind.Place, BackpackPlacementKind.ReturnToTray
            }));
            yield return new WaitForSeconds(3f);
            Assert.That(summons, Is.EqualTo(new[] { "CatRocket1" }));
        }

        [UnityTest]
        public IEnumerator FocusLossCancelsThePendingDropAndAllowsAFreshDrag()
        {
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            var start = ScreenCenter(fish.RectTransform);
            var target = ItemCenter(FishAnchor);
            BeginDrag(fish, target);
            Assert.That(card.gameObject.activeInHierarchy, Is.True);

            SetFocus(false);
            inputModule.UpdateModule();
            AssertUnplaced(fish, start, "focus loss");
            SetFocus(true);
            SendMouse(target, up: true);
            AssertUnplaced(fish, start, "release after regaining focus");

            Drag(fish, target);
            AssertOccupancy("Fish1", FishAnchor);
            Assert.That(placements.Count, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BattlePauseCancelsActiveDragAndBlocksNewPressesUntilResume()
        {
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            var start = ScreenCenter(fish.RectTransform);
            var target = ItemCenter(FishAnchor);
            BeginDrag(fish, target);
            Assert.That(card.gameObject.activeInHierarchy, Is.True);

            var clock = backpack.Clock;
            float speed = clock.PlaybackSpeed;
            clock.SetPlaybackSpeed(0f);
            AssertUnplaced(fish, start, "battle paused during drag");
            SendMouse(target, up: true);
            SendMouse(start, down: true);
            SendMouse(target);
            AssertUnplaced(fish, start, "new drag while paused");
            clock.SetPlaybackSpeed(speed);
            SendMouse(target, up: true);
            AssertUnplaced(fish, start, "release after resume");

            Drag(fish, target);
            AssertOccupancy("Fish1", FishAnchor);
            Assert.That(placements.Count, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FrameworkModalReleasesTheDragAndRestoresOnlyAFreshPress()
        {
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            var start = ScreenCenter(fish.RectTransform);
            var target = ItemCenter(FishAnchor);
            var input = presenter.DragInputService;
            var routing = new DragActionObserver();
            input.SetRouter(routing);
            using var modal = input.CreateContext("test.modal", priority: 20, modal: true);
            modal.SetEnabled(false);

            BeginDrag(fish, target);
            Assert.That(card.gameObject.activeInHierarchy, Is.True);
            modal.SetEnabled(true);
            yield return Await(() => !card.gameObject.activeInHierarchy, "modal releases the active drag");
            AssertUnplaced(fish, start, "modal cancels the pending drop");
            Assert.That(routing.Edges, Is.EqualTo(new[] { InputActionPhase.Pressed, InputActionPhase.Released }));

            SendMouse(target, up: true);
            SendMouse(start, down: true);
            SendMouse(target);
            AssertUnplaced(fish, start, "modal blocks a new drag");
            modal.SetEnabled(false);
            SendMouse(target + Vector2.right);
            SendMouse(target, up: true);
            AssertUnplaced(fish, start, "closing the modal does not replay the blocked press");
            Assert.That(routing.Edges.Count, Is.EqualTo(2));

            Drag(fish, target);
            AssertOccupancy("Fish1", FishAnchor);
            Assert.That(placements, Is.EqualTo(new[] { BackpackPlacementKind.Place }));
            Assert.That(summons, Is.Empty);
            Assert.That(routing.Edges, Is.EqualTo(new[]
            {
                InputActionPhase.Pressed, InputActionPhase.Released,
                InputActionPhase.Pressed, InputActionPhase.Released
            }));
        }

        [UnityTest]
        public IEnumerator AutoPlacementFlightResumesFromItsPausedPosition()
        {
            var fish = FindItem("Fish1", ItemViewOrigin.Tray);
            backpack.SetAutoPlaceItems(true);
            yield return Await(() => backpack.TrayItems[0].IsAutoPlacing,
                "scheduled fish begins its flight");

            var clock = backpack.Clock;
            float speed = clock.PlaybackSpeed;
            clock.SetPlaybackSpeed(0f);
            var position = fish.RectTransform.position;
            yield return new WaitForSeconds(0.55f);
            Assert.That(fish.RectTransform.position, Is.EqualTo(position));
            Assert.That(backpack.TrayItems[0].IsAutoPlacing, Is.True);
            Assert.That(placements, Is.Empty);
            clock.SetPlaybackSpeed(speed);
            yield return Await(() => placements.Count > 0, "fish completes its flight after resume");
            Assert.That(placements, Is.EqualTo(new[] { BackpackPlacementKind.Place }));
            AssertOccupancy("Fish1", FishAnchor);
        }

        [UnityTest]
        public IEnumerator DragCardStaysWithItsBattleWhileAnotherBattleIsLoaded()
        {
            Scene localBattle = presenter.gameObject.scene;
            yield return SceneManager.LoadSceneAsync(localBattle.path, LoadSceneMode.Additive);
            overlappingBattle = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(overlappingBattle, Is.Not.EqualTo(localBattle));
            var overlapRoots = overlappingBattle.GetRootGameObjects();
            var foreignCard = overlapRoots.Select(root => root.GetComponentInChildren<CardDragInfoView>(true))
                .Single(candidate => candidate != null);
            foreach (var root in overlapRoots)
                foreach (var otherRaycaster in root.GetComponentsInChildren<GraphicRaycaster>())
                    otherRaycaster.enabled = false;

            // Simulate a card reference cached from the outgoing scene during a transition.
            typeof(BackpackPresenter).GetField("dragInfoView", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(presenter, foreignCard);
            var target = ItemCenter(FishAnchor);
            BeginDrag(FindItem("Fish1", ItemViewOrigin.Tray), target);
            Assert.That(card.gameObject.activeInHierarchy, Is.True, "the local battle shows its own drag card");
            Assert.That(foreignCard.gameObject.activeInHierarchy, Is.False);
            SendMouse(target, up: true);
            Assert.That(card.gameObject.activeInHierarchy, Is.False);
            AssertOccupancy("Fish1", FishAnchor);
        }

        void SetFocus(bool focused)
        {
            presenter.SendMessage("OnApplicationFocus", focused, SendMessageOptions.DontRequireReceiver);
            eventSystem.SendMessage("OnApplicationFocus", focused);
        }

        ItemView FindItem(string id, ItemViewOrigin origin) =>
            presenter.GetComponentsInChildren<ItemView>(true).Single(view =>
                view.gameObject.activeInHierarchy && view.Origin == origin && view.Definition.id == id);

        RectTransform Cell(Vector2Int address) => (RectTransform)grid.Find($"Cell_{address.x}_{address.y}");

        Vector2 ScreenCenter(RectTransform rect) =>
            RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center));

        Vector2 ItemCenter(Vector2Int anchor) =>
            (ScreenCenter(Cell(anchor)) + ScreenCenter(Cell(anchor + Vector2Int.up))) * 0.5f;

        void AssertOccupancy(string id, Vector2Int anchor)
        {
            Assert.That(backpack.Model.TryGetItemAt(anchor, out var item), Is.True, $"{id} at {anchor}");
            Assert.That(item.Anchor, Is.EqualTo(anchor));
            Assert.That(item.Definition.id, Is.EqualTo(id));
            Assert.That(backpack.Model.TryGetItemAt(anchor + Vector2Int.up, out var upper), Is.True);
            Assert.That(upper, Is.SameAs(item));
        }

        void AssertUnplaced(ItemView fish, Vector2 start, string stage)
        {
            Assert.That(backpack.Model.Items, Is.Empty, stage);
            Assert.That(backpack.TrayItems.Select(item => item.TrayId), Does.Contain(fish.SourceId), stage);
            Assert.That(summons, Is.Empty, stage);
            Assert.That(placements, Is.Empty, stage);
            Assert.That(card.gameObject.activeInHierarchy, Is.False, stage);
            Assert.That(Vector2.Distance(ScreenCenter(fish.RectTransform), start), Is.LessThan(1f), stage);
        }

        void Drag(ItemView item, Vector2 target)
        {
            BeginDrag(item, target);
            SendMouse(target, up: true);
            Canvas.ForceUpdateCanvases();
        }

        void BeginDrag(ItemView item, Vector2 target)
        {
            var source = ScreenCenter(item.RectTransform);
            var hit = Raycast(source);
            Assert.That(ExecuteEvents.GetEventHandler<IBeginDragHandler>(hit.gameObject),
                Is.EqualTo(item.gameObject), $"source for {item.Definition.id} at {source} hit {hit.gameObject}");
            SendMouse(source, down: true);
            SendMouse(target);
            Assert.That(Vector2.Distance(ScreenCenter(item.RectTransform), target), Is.LessThan(1f),
                "scene input module delivered drag movement");
        }

        RaycastResult Raycast(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = point }, hits);
            Assert.That(hits, Is.Not.Empty, $"battle canvas at {point}, screen {Screen.width}x{Screen.height}");
            Assert.That(hits[0].module, Is.SameAs(raycaster));
            return hits[0];
        }

        void SendMouse(Vector2 position, bool down = false, bool up = false)
        {
            mouse.Position = position;
            mouse.Down = down;
            mouse.Up = up;
            inputModule.Process();
            mouse.Down = false;
            mouse.Up = false;
        }

        static IEnumerator Await(Func<bool> condition, string description, float timeout = 3f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, description);
        }

        sealed class DragActionObserver : IInputRouter
        {
            public readonly List<InputActionPhase> Edges = new List<InputActionPhase>();

            public bool Route(InputActionEvent action)
            {
                if (action.ContextId == "bbads.backpack" && action.Action == "drag" &&
                    action.Phase != InputActionPhase.Held)
                    Edges.Add(action.Phase);
                return false;
            }
        }

        sealed class MouseInput : BaseInput
        {
            public Vector2 Position;
            public bool Down;
            public bool Up;

            public override bool mousePresent => true;
            public override Vector2 mousePosition => Position;
            public override Vector2 mouseScrollDelta => Vector2.zero;
            public override int touchCount => 0;
            public override bool GetMouseButtonDown(int button) => button == 0 && Down;
            public override bool GetMouseButtonUp(int button) => button == 0 && Up;
            public override bool GetButtonDown(string buttonName) => false;
            public override float GetAxisRaw(string axisName) => 0f;
        }
    }
}

