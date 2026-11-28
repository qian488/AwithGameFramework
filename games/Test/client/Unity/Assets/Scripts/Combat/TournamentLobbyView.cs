using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Unity.Presentation;
using Test.Application.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    public sealed class TournamentLobbyView : MonoBehaviour
    {
        private const string GeneratedResource = "Tournament/GeneratedView";
        private Canvas canvas;
        private UnityPrototypeView design;
        private InputField server, room, player;
        private Button join, ready, retry, back, details;
        private Text status, score, next, first, second, rounds;
        private ScrollRect report;
        private Image backpackIcon;
        private EventSystem createdEvents;
        private PvpTournamentRoundSummary[] previousRounds;
        private string currentPage;
        private bool expanded;
        private bool tabDown;
        private bool tabNavigation, tabBackwards;
        private Func<Task> joinAction, readyAction, retryAction;
        private Action backAction;

        public string Page => currentPage;
        public string Status => status.text;
        internal bool IsOpen => gameObject.activeSelf;
        internal Task Joining { get; private set; } = Task.CompletedTask;
        internal InputField Server => server;
        internal InputField Room => room;
        internal InputField Player => player;
        internal Button Join => join;
        internal Button Ready => ready;
        internal Button Details => details;

        public void Initialize(string address, string roomCode, string playerName,
            Func<Task> connect, Func<Task> confirm, Func<Task> reconnect, Action close)
        {
            if (design != null) return;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1;
            gameObject.AddComponent<GraphicRaycaster>();
            var asset = Resources.Load<GameObject>(GeneratedResource)?.GetComponent<UnityPrototypeView>();
            if (asset == null) throw new InvalidOperationException("Import the Tournament/GeneratedView prefab before opening the lobby.");
            design = Instantiate(asset, transform, false);
            server = design.Get<InputField>("server");
            room = design.Get<InputField>("room");
            player = design.Get<InputField>("player");
            join = design.Get<Button>("join");
            ready = design.Get<Button>("ready");
            retry = design.Get<Button>("retry");
            back = design.Get<Button>("back");
            details = design.Get<Button>("details");
            detailsCaption = details.GetComponentInChildren<Text>().text;
            status = design.Get<Text>("status");
            score = design.Get<Text>("score");
            next = design.Get<Text>("next");
            first = design.Get<Text>("first");
            second = design.Get<Text>("second");
            rounds = design.Get<Text>("rounds");
            report = design.Get<ScrollRect>("report");
            backpackIcon = design.Get<Image>("backpack-icon");
            server.SetTextWithoutNotify(address);
            room.SetTextWithoutNotify(roomCode);
            player.SetTextWithoutNotify(playerName);
            joinAction = connect;
            readyAction = confirm;
            retryAction = reconnect;
            backAction = close;
            join.onClick.AddListener(() => Joining = joinAction?.Invoke() ?? Task.CompletedTask);
            ready.onClick.AddListener(() => { if (readyAction != null) _ = readyAction(); });
            retry.onClick.AddListener(() => { if (retryAction != null) _ = retryAction(); });
            back.onClick.AddListener(() => backAction?.Invoke());
            details.onClick.AddListener(() =>
            {
                expanded = !expanded;
                UpdateRounds(previousRounds);
            });
        }

        public void FillForm(string address, string roomCode, string playerName)
        {
            server.SetTextWithoutNotify(address);
            room.SetTextWithoutNotify(roomCode);
            player.SetTextWithoutNotify(playerName);
        }

        public void Present(string page, string message, PvpTournamentState state, int localSlot,
            bool canJoin, bool canReady, bool canReconnect)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            bool changed = page != currentPage;
            var events = EnsureEventSystem();
            currentPage = page;
            status.text = message;
            join.interactable = canJoin;
            ready.interactable = canReady;
            retry.interactable = canReconnect;
            server.interactable = room.interactable = player.interactable = canJoin;
            if (state != null)
            {
                score.text = $"Score   {state.FirstScore} – {state.SecondScore}";
                next.text = $"Round {state.RoundNumber}   {(state.Mode == "bbads" ? "BBads" : "Rain Pass")}";
                first.text = DescribePlayer(state, 0, localSlot, state.FirstPlayerId);
                second.text = DescribePlayer(state, 1, localSlot, state.SecondPlayerId);
            }
            if (changed) design.ShowPage(page);
            if (page == "reconnect" && state == null)
                score.gameObject.SetActive(false);
            else if (page == "reconnect")
                score.gameObject.SetActive(true);

            if (page == "lobby" || page == "reconnect") ApplyVisibility(ready, page == "lobby" && canReady);
            if (page == "results")
            {
                bool hasBoards = state?.Rounds?.Any(round => round.FirstBackpack != null) == true;
                ApplyVisibility(details, hasBoards);
                if (backpackIcon.gameObject.activeSelf != hasBoards) backpackIcon.gameObject.SetActive(hasBoards);
                if (!ReferenceEquals(previousRounds, state?.Rounds))
                {
                    previousRounds = state?.Rounds;
                    expanded = false;
                    UpdateRounds(previousRounds);
                }
            }
            var selected = events.currentSelectedGameObject;
            if (changed || selected == null || !selected.activeInHierarchy ||
                !selected.transform.IsChildOf(transform) || selected.GetComponent<Selectable>()?.IsInteractable() != true)
            {
                Selectable focus = page == "join" && canJoin ? player : page == "lobby" && canReady ? ready :
                    page == "reconnect" && canReconnect ? retry : page == "results" && details.gameObject.activeSelf ? details : back;
                if (selected != focus.gameObject) events.SetSelectedGameObject(focus.gameObject);
            }
        }

        private static void ApplyVisibility(Selectable selectable, bool visible)
        {
            if (selectable.gameObject.activeSelf != visible) selectable.gameObject.SetActive(visible);
        }

        public void RenderForCamera(Camera camera)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
        }

        public void Hide()
        {
            var events = EventSystem.current;
            if (events?.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(transform))
                events.SetSelectedGameObject(null);
            if (gameObject.activeSelf) gameObject.SetActive(false);
            currentPage = null;
        }

        private EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null) return EventSystem.current;
            if (createdEvents != null) return createdEvents;
            var host = new GameObject("Tournament EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            host.transform.SetParent(transform, false);
            createdEvents = host.GetComponent<EventSystem>();
            return createdEvents;
        }

        private void OnEnable()
        {
            if (UnityEngine.Application.isPlaying) InputSystem.onEvent += ReadKeyboard;
        }

        private void OnDisable()
        {
            InputSystem.onEvent -= ReadKeyboard;
            tabDown = tabNavigation = false;
        }

        private void ReadKeyboard(InputEventPtr input, InputDevice device)
        {
            if (!(device is Keyboard keyboard) || !keyboard.tabKey.ReadValueFromEvent(input, out float value)) return;
            bool pressed = value > .5f;
            if (pressed && !tabDown)
            {
                tabNavigation = true;
                tabBackwards = keyboard.leftShiftKey.ReadValueFromEvent(input, out float leftShift) && leftShift > .5f ||
                    keyboard.rightShiftKey.ReadValueFromEvent(input, out float rightShift) && rightShift > .5f;
            }
            tabDown = pressed;
        }

        private void Update()
        {
            RainPassGamepad.Navigate(this, backAction);
            var keyboard = Keyboard.current;
            if (tabNavigation)
            {
                tabNavigation = false;
                NavigateTab(tabBackwards);
            }
            if (currentPage != "results" || !report.gameObject.activeInHierarchy) return;
            float step = keyboard?.pageDownKey.wasPressedThisFrame == true || Gamepad.current?.rightShoulder.wasPressedThisFrame == true ? -.2f :
                keyboard?.pageUpKey.wasPressedThisFrame == true || Gamepad.current?.leftShoulder.wasPressedThisFrame == true ? .2f : 0;
            if (step != 0) report.verticalNormalizedPosition = Mathf.Clamp01(report.verticalNormalizedPosition + step);
        }

        private void NavigateTab(bool backwards)
        {
            var events = EventSystem.current;
            var controls = GetComponentsInChildren<Selectable>().Where(item => item.IsInteractable()).ToArray();
            if (events == null || controls.Length == 0) return;
            int index = Array.FindIndex(controls, item => item.gameObject == events.currentSelectedGameObject);
            int direction = backwards ? -1 : 1;
            events.SetSelectedGameObject(controls[(index + direction + controls.Length) % controls.Length].gameObject);
        }

        private static string DescribePlayer(PvpTournamentState state, int slot, int localSlot, string name)
        {
            int mask = 1 << slot;
            string flag = (state.ConnectedMask & mask) == 0 ? "Waiting to connect" :
                (state.ReadyMask & mask) == 0 ? "Preparing" : "Ready";
            if (string.IsNullOrEmpty(name)) name = "Player " + (slot + 1);
            if (name.Length > 32) name = name.Substring(0, 29) + "…";
            return name + (slot == localSlot ? " (you)" : "") + "   ·   " + flag;
        }

        private void UpdateRounds(PvpTournamentRoundSummary[] results)
        {
            var lines = new StringBuilder();
            if (results != null)
            {
                for (int index = 0; index < results.Length; index++)
                {
                    var round = results[index];
                    string result = round.Winner < 0 ? "Draw" : $"Player {round.Winner + 1} won";
                    lines.AppendLine($"{index + 1}. {(round.Mode == "bbads" ? "BBads" : "Rain Pass")}   ·   {result}");
                    if (expanded && round.FirstBackpack != null && round.SecondBackpack != null)
                    {
                        AppendBackpack(lines, "Player 1", round.FirstBackpack);
                        AppendBackpack(lines, "Player 2", round.SecondBackpack);
                    }
                }
            }
            rounds.text = lines.Length == 0 ? "No completed rounds" : lines.ToString().TrimEnd();
            if (details.gameObject.activeInHierarchy)
            {
                var caption = details.GetComponentInChildren<Text>();
                caption.text = expanded ? "Hide BBads backpacks" : detailsCaption;
            }
            report.verticalNormalizedPosition = 1;
        }

        private string detailsCaption;

        private static void AppendBackpack(StringBuilder lines, string name, string[] items)
        {
            var cells = new Dictionary<(int, int), string>();
            foreach (var placement in items)
            {
                var fields = placement.Split(':');
                if (fields.Length == 3 && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y))
                    cells[(x, y)] = fields[0];
            }
            lines.AppendLine("    " + name + " backpack");
            for (int row = 0; row < 2; row++)
                lines.AppendLine("    " + string.Join("   /   ", Enumerable.Range(0, 3)
                    .Select(column => cells.TryGetValue((column, row), out var item) ? item : "Empty")));
        }
    }
}
