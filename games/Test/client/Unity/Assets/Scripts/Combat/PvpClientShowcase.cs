using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Gameplay.Input;
using Test.Application.Combat;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat;
using Test.Domain.Combat.Pvp;
using UnityEngine;
using WireCue = Test.Application.Protocol.PvpCombatCue;
using PlayerSnapshot = Test.Application.Protocol.PvpPlayerSnapshot;
using InputFrame = Test.Application.Protocol.PvpInputFrame;
using Numerics = System.Numerics;

namespace Test.Client.Combat
{
    [DisallowMultipleComponent]
    public sealed partial class PvpClientShowcase : MonoBehaviour
    {
        internal const string RainPassMode = "rain-pass";
        internal const string BBadsMode = "bbads";
        private const string Running = "Running";
        private const string Finished = "Finished";
        private const int InputLead = 4;
        private static readonly Vector3 CameraOffset = new Vector3(0, 5, -8);

        [SerializeField] private string serverUrl = "http://127.0.0.1:17840";
        [SerializeField] private string roomId = "rain-pass-test";
        [SerializeField] private string playerId = "alpha";
        [SerializeField] private string mode = RainPassMode;
        [SerializeField] private bool connectOnEnable = true;

        private readonly PassFighter[] fighters = new PassFighter[2];
        private readonly PlayerSnapshot[] players = new PlayerSnapshot[2];
        private readonly PlayerSnapshot[] pendingPlayers = new PlayerSnapshot[2];
        private readonly List<Transform> arrows = new List<Transform>();
        private CombatShowcase owner;
        private PvpPlayerSmoke smoke;
        private PassWorld world;
        private Transform arena;
        private Material arrowShaft, arrowHead;
        private Camera lens;
        private Connection connection;
        private Task disconnect = Task.CompletedTask;
        private PvpJoinResponse joined;
        private PvpSnapshotMessage snapshot, pendingSnapshot;
        private int pendingSimulationTick;
        private Task sending;
        private long sentTick, cueSequence;
        private int rematch;
        private string activeRoom, roomState, error, cue;
        private int winner = -1, occupancy;
        private float cueUntil, disconnectedAt = -1;
        private bool menu;
        private float forward, back, left, right;
        private bool attack, guard;
        private PvpButtons edges;
        private PvpButtons backpackItem = PvpButtons.EquipSword;
        private PvpButtons backpackCommand;
        private Vector2Int backpackCell;

        public bool IsConnected => connection?.Session.IsConnected == true;
        public string ServerUrl => serverUrl;
        public string RoomId => activeRoom ?? roomId;
        public string PlayerId => playerId;
        public string Mode => mode;
        internal bool IsFinished => roomState == Finished || roomState == "Closed";
        internal PassFighter Fighter(int slot) => fighters[slot];
        internal Vector2 Movement => Vector2.ClampMagnitude(new Vector2(right - left, forward - back), 1);
        internal string ConnectionError => error;

        internal Task StartSmokeAsync(PvpPlayerSmoke driver, string server, string room, string player, string selectedMode)
        {
            SelectMode(selectedMode);
            smoke = driver;
            serverUrl = server;
            roomId = room;
            playerId = player;
            if (IsTournament)
            {
                UpdateTournamentLobby();
                tournamentLobby.FillForm(server, room, player);
                tournamentLobby.Join.onClick.Invoke();
                return tournamentLobby.Joining;
            }
            return ConnectAsync();
        }

        internal void Initialize(CombatShowcase showcase, PassWorld world, Camera camera, float scale)
        {
            owner = showcase;
            this.world = world;
            lens = camera;
            connectOnEnable = false;
            arena = PassWorld.Group("Rain Pass PvP courtyard", transform);
            arena.localPosition = new Vector3(0, .03f, 23);
            var plate = world.Material("PvP lacquer", new Color(.16f, .22f, .24f), 3, .46f);
            var steel = world.Material("PvP folded steel", new Color(.66f, .81f, .87f), 0, .65f);
            var brass = world.Material("PvP brass", new Color(.75f, .37f, .14f), 5, .5f);
            arrowShaft = brass;
            arrowHead = steel;
            var flash = world.Material("PvP blade", Color.white, 0, .7f, new Color(.49f, 1.67f, 2.3f));
            for (int slot = 0; slot < fighters.Length; slot++)
            {
                var cloth = world.Material("PvP player " + (slot + 1),
                    slot == 0 ? new Color(.11f, .32f, .37f) : new Color(.50f, .125f, .09f), 4, .29f);
                var fighter = new PassFighter(world, arena, 0, scale, cloth, plate, steel, brass, flash);
                fighter.Root.name = "PvP Wayfarer " + (slot + 1);
                fighter.Controller.enabled = false;
                fighters[slot] = fighter;
            }
            ResetMatchView();
        }

        private void OnEnable()
        {
            tournament = null;
            ResetMatchView();
            if (connectOnEnable) _ = ConnectAsync();
        }

        private void OnDisable()
        {
            if (tournamentLobby != null) tournamentLobby.Hide();
            _ = DisconnectAsync(leave: true);
            ResetMatchView();
            SetCursor(false);
        }

        internal void SelectMode(string value)
        {
            if (connection != null || value == mode) return;
            if (roomId == "rain-pass-test" || roomId == "bbads-test" || roomId == "tournament-test")
                roomId = value + "-test";
            mode = value;
            rematch = 0;
            activeRoom = null;
            error = null;
            ResetMatchView();
        }

        public async Task ConnectAsync()
        {
            if (connection != null || !isActiveAndEnabled) return;
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var httpUri) ||
                (httpUri.Scheme != "http" && httpUri.Scheme != "https"))
            {
                error = "Enter an HTTP or HTTPS server URL.";
                return;
            }
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId))
            {
                error = "Enter a room ID and player ID.";
                return;
            }

            ResetMatchView();
            tournament = null;
            error = null;
            activeRoom = RoomForRound(roomId.Trim(), rematch);
            var websocket = new UriBuilder(new Uri(httpUri, "/pvp/session"))
            {
                Scheme = httpUri.Scheme == "https" ? "wss" : "ws",
                Port = httpUri.Port
            }.Uri;
            var current = new Connection(websocket);
            connection = current;
            current.Session.RoomChanged += value => current.Post(() => ReceiveRoom(value));
            current.Session.SnapshotReceived += value =>
            {
                int simulationTick = current.Session.SimulationTick;
                current.Post(() => ReceiveSnapshot(value, simulationTick));
            };
            current.Session.PlayerReceived += value => current.Post(() => ReceivePlayer(value));
            current.Session.CueReceived += value => current.Post(() => ReceiveCue(value));
            current.Session.ArrowsReceived += value => current.Post(() => ReceiveArrows(value));
            current.Session.TournamentChanged += value => current.Post(() => ReceiveTournament(value));
            current.Session.RecoveryReceived += value => current.Post(() =>
            {
                error = value.Reason;
                Debug.LogWarning("PvP session ended: " + error, this);
                smoke?.RecordFailure(error);
            });
            current.Session.ResultReceived += value => current.Post(() =>
            {
                if (value != null && joined != null) EndMatch(value.Winner);
            });

            try
            {
                await disconnect;
                if (connection != current) return;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(current.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var response = await current.Session.JoinAsync(activeRoom, playerId.Trim(), mode, timeout.Token,
                    IsTournament && smoke == null ? PlayerPrefs.GetString(TournamentSeatKey) : null);
                if (connection != current) return;
                if (!AcceptJoin(response)) await DisconnectAsync();
                else if (IsTournament && smoke == null)
                {
                    PlayerPrefs.SetString(TournamentSeatKey, response.ResumeToken);
                    PlayerPrefs.Save();
                }
            }
            catch (Exception exception)
            {
                if (connection != current) return;
                error = exception is OperationCanceledException ? "The room did not reply. Try joining again." : exception.Message;
                await DisconnectAsync();
            }
        }

        private Task DisconnectAsync(bool leave = false)
        {
            var current = connection;
            connection = null;
            if (current != null) disconnect = DisposeConnectionAsync(current, disconnect, leave && joined != null);
            return disconnect;
        }

        private async Task DisposeConnectionAsync(Connection current, Task previous, bool leave)
        {
            current.Cancel();
            try
            {
                await previous;
                if (leave) await current.Session.LeaveAsync();
            }
            catch (Exception exception)
            {
                if (this != null && isActiveAndEnabled && connection == null) error = exception.Message;
            }
            finally
            {
                try { await current.Session.DisposeAsync(); }
                catch (Exception exception)
                {
                    if (this != null && isActiveAndEnabled && connection == null) error = exception.Message;
                }
                current.Dispose();
            }
        }

        internal async Task PlayAgainAsync()
        {
            if (!IsFinished) return;
            rematch++;
            await LeaveAsync();
            if (this != null && isActiveAndEnabled) await ConnectAsync();
        }

        private void Update()
        {
            var current = connection;
            if (current != null && joined != null)
            {
                while (current.Messages.TryDequeue(out var apply)) apply();
                if (IsConnected)
                {
                    disconnectedAt = -1;
                    if (roomState == Running && snapshot?.PeerConnected == true) SendNextInput(current);
                }
                else if (disconnectedAt < 0)
                {
                    disconnectedAt = Time.unscaledTime;
                    ResetInput();
                }
            }
            UpdateTournamentLobby();
            bool inCombat = InCombat && !TournamentLobbyVisible;
            if (owner != null) SetCursor(inCombat);
            if (arena != null && GameplayMode == RainPassMode)
            {
                PoseFighters();
                var center = (fighters[0].Root.position + fighters[1].Root.position) * .5f + Vector3.up * 1.1f;
                float spread = Vector3.Distance(fighters[0].Root.position, fighters[1].Root.position);
                lens.transform.position = center + CameraOffset * Mathf.Max(1, spread / 7f);
                lens.transform.rotation = Quaternion.LookRotation(center - lens.transform.position);
            }
        }

        private bool ControlsEnabled => joined != null && roomState == Running && !menu && GameplayMode == RainPassMode;
        private bool InCombat => ControlsEnabled && IsConnected;

        internal void ReceiveInput(InputActionEvent action)
        {
            bool pressed = action.Phase == InputActionPhase.Pressed;
            bool held = action.Phase != InputActionPhase.Released;
            if (action.Action == "pause" && pressed)
            {
                if (roomState == Running && GameplayMode == RainPassMode)
                {
                    menu = !menu;
                    ResetInput();
                }
                else owner?.ClosePvp();
                return;
            }
            if (!ControlsEnabled) return;
            switch (action.Action)
            {
                case "forward": forward = action.Value; break;
                case "back": back = action.Value; break;
                case "left": left = action.Value; break;
                case "right": right = action.Value; break;
                case "attack":
                    attack = held;
                    if (pressed) edges |= PvpButtons.AttackPressed;
                    if (!held) edges |= PvpButtons.AttackReleased;
                    break;
                case "guard":
                    guard = held;
                    if (pressed) edges |= PvpButtons.Guard;
                    break;
                case "dodge": if (pressed) edges |= PvpButtons.Dodge; break;
                case "lock": if (pressed) edges |= PvpButtons.Lock; break;
                case "sword": if (pressed) edges |= PvpButtons.EquipSword; break;
                case "bow": if (pressed) edges |= PvpButtons.EquipBow; break;
            }
        }

        internal InputFrame SampleInput(bool enabled)
        {
            if (GameplayMode == BBadsMode)
                return new InputFrame
                {
                    SimulationTick = snapshot.ServerTick + InputLead,
                    Buttons = (int)backpackCommand,
                    MoveX = backpackCell.x * 1000,
                    MoveY = backpackCell.y * 1000
                };
            var local = joined == null ? null : players[joined.PlayerSlot];
            var peer = joined == null ? null : players[1 - joined.PlayerSlot];
            var movement = enabled ? Movement : Vector2.zero;
            var toward = local != null && peer != null ? Position(peer) - Position(local) : Vector3.right;
            var aim = movement.sqrMagnitude > .01f ? movement : new Vector2(toward.x, toward.z).normalized;
            var buttons = enabled ? edges | (guard ? PvpButtons.Guard : PvpButtons.None) : PvpButtons.None;
            if (enabled && attack && local != null && local.Weapon == (int)PvpWeapon.Sword && peer?.Broken == true &&
                toward.sqrMagnitude <= PvpCombatConfig.FinishReach * PvpCombatConfig.FinishReach)
                buttons = (buttons & ~PvpButtons.AttackPressed) | PvpButtons.Finish;
            return new InputFrame
            {
                SimulationTick = snapshot.ServerTick + InputLead,
                Buttons = (int)buttons,
                MoveX = Mathf.RoundToInt(movement.x * 1000),
                MoveY = Mathf.RoundToInt(movement.y * 1000),
                AimX = Mathf.RoundToInt(aim.x * 1000),
                AimY = Mathf.RoundToInt(aim.y * 1000)
            };
        }

        private void SendNextInput(Connection current)
        {
            if ((sending != null && !sending.IsCompleted) || snapshot.ServerTick + InputLead <= sentTick) return;
            if (smoke != null)
            {
                var frame = smoke.BuildInput(snapshot, players, joined.PlayerSlot, snapshot.ServerTick + InputLead);
                if (frame == null) return;
                sentTick = frame.SimulationTick;
                sending = SendInputAsync(current, frame);
                return;
            }
            if (GameplayMode == BBadsMode && backpackCommand == PvpButtons.None) return;
            bool focused = owner != null && owner.BattleInput.IsFocused;
            if (!focused) ResetInput();
            var input = SampleInput(InCombat && focused);
            sentTick = input.SimulationTick;
            edges = PvpButtons.None;
            backpackCommand = PvpButtons.None;
            sending = SendInputAsync(current, input);
        }

        private async Task SendInputAsync(Connection current, InputFrame input)
        {
            try
            {
                await current.Session.SendInputAsync(input.SimulationTick, (PvpButtons)input.Buttons,
                    input.MoveX, input.MoveY, input.AimX, input.AimY, current.Token, snapshot?.MatchId);
                if (connection == current) smoke?.RecordSentInput();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (connection == current) error = exception.Message; }
        }

        internal bool AcceptJoin(PvpJoinResponse value)
        {
            if (value == null || !value.Accepted || (!IsTournament && string.IsNullOrEmpty(value.MatchId)) ||
                value.PlayerSlot < 0 || value.PlayerSlot >= players.Length)
            {
                error = value?.Reason ?? "The server did not assign a player slot.";
                return false;
            }
            joined = value;
            roomState = value.RoomState;
            smoke?.RecordJoin(value);
            return true;
        }

        private bool IsCurrent(string match) => joined != null && match == (IsTournament ? tournament?.MatchId : joined.MatchId);

        private void ReceiveRoom(PvpRoomMessage value)
        {
            if (value == null || !IsCurrent(value.MatchId)) return;
            occupancy = value.PlayerCount;
            smoke?.RecordRoom(value);
            if (!IsFinished) roomState = value.RoomState;
        }

        internal void ReceiveSnapshot(PvpSnapshotMessage value, int simulationTick = 0)
        {
            if (value == null || !IsCurrent(value.MatchId) ||
                value.SnapshotSequence <= (pendingSnapshot?.SnapshotSequence ?? snapshot?.SnapshotSequence ?? 0)) return;
            if (value.PlayerCount != players.Length) return;
            pendingSnapshot = value;
            pendingSimulationTick = simulationTick;
            Array.Clear(pendingPlayers, 0, pendingPlayers.Length);
        }

        internal void ReceivePlayer(PlayerSnapshot value)
        {
            if (value == null || !IsCurrent(value.MatchId) ||
                value.SnapshotSequence != pendingSnapshot?.SnapshotSequence || value.PlayerSlot < 0 ||
                value.PlayerSlot >= players.Length) return;
            pendingPlayers[value.PlayerSlot] = value;
            if (pendingPlayers[0] == null || pendingPlayers[1] == null) return;

            Array.Copy(pendingPlayers, players, players.Length);
            snapshot = pendingSnapshot;
            pendingSnapshot = null;
            if (snapshot.RoomState == Finished || snapshot.Phase == Finished) EndMatch(snapshot.Winner);
            else if (!IsFinished) roomState = string.IsNullOrEmpty(snapshot.RoomState) ? Running : snapshot.RoomState;
            smoke?.RecordSnapshot(snapshot, players, pendingSimulationTick);
        }

        private void ReceiveCue(WireCue value)
        {
            if (value == null || !IsCurrent(value.MatchId) ||
                value.CueSequence <= cueSequence) return;
            cueSequence = value.CueSequence;
            cue = value.CueType;
            cueUntil = Time.unscaledTime + .8f;
        }

        internal void ReceiveArrows(IReadOnlyList<Numerics.Vector3> positions)
        {
            if (arena == null || GameplayMode != RainPassMode) return;
            int count = positions?.Count ?? 0;
            for (int index = 0; index < count; index++)
            {
                if (index == arrows.Count)
                {
                    var model = PassWorld.Group("PvP flying arrow", arena);
                    world.Shape("Shaft", PrimitiveType.Cube, Vector3.back * .23f, new Vector3(.035f, .035f, .46f), arrowShaft, model);
                    world.Shape("Head", PrimitiveType.Cube, Vector3.zero, new Vector3(.065f, .055f, .13f), arrowHead, model);
                    model.gameObject.SetActive(false);
                    arrows.Add(model);
                }
                var arrow = arrows[index];
                var source = positions[index];
                var position = new Vector3(source.X, source.Y, source.Z);
                var direction = arrow.gameObject.activeSelf ? position - arrow.localPosition : Vector3.zero;
                if (direction.sqrMagnitude > .0001f && direction.sqrMagnitude < 4)
                    arrow.localRotation = Quaternion.LookRotation(direction);
                arrow.localPosition = position;
                arrow.gameObject.SetActive(true);
            }
            for (int index = count; index < arrows.Count; index++) arrows[index].gameObject.SetActive(false);
        }

        private void EndMatch(int slot)
        {
            roomState = Finished;
            winner = slot;
            ResetInput();
        }

        internal void PoseFighters()
        {
            if (arena == null) return;
            for (int slot = 0; slot < fighters.Length; slot++)
            {
                var player = players[slot];
                var position = player == null ? new Vector3(slot == 0 ? -1.1f : 1.1f, 0, 0) : Position(player);
                var facing = player == null ? Vector3.zero : new Vector3((float)player.FacingX, 0, (float)player.FacingY);
                if (facing.sqrMagnitude < .001f)
                {
                    var other = players[1 - slot];
                    facing = other == null ? Vector3.zero : Position(other) - position;
                    if (facing.sqrMagnitude < .001f) facing = slot == 0 ? Vector3.right : Vector3.left;
                }
                var actor = new ReplayActor
                {
                    Facing = new Numerics.Vector3(facing.x, 0, facing.z),
                    Health = player == null || player.Alive ? Mathf.Max(1, player?.Health ?? 100) : 0,
                    HealthLimit = 100,
                    Posture = player?.Posture ?? 0,
                    PostureLimit = 100,
                    Phase = (AttackPhase)(player?.ActionState ?? 0),
                    PhaseProgress = Mathf.Clamp01((float)(player?.PhaseProgress ?? 0)),
                    Moving = player?.Moving == true,
                    Guarding = player?.Guarding == true,
                    Dodging = player?.Dodging == true,
                    Broken = player?.Broken == true
                };
                fighters[slot].Root.localPosition = position;
                fighters[slot].Pose(actor.ToView(), (snapshot?.ServerTick ?? 0) / (float)CombatRun.StepsPerSecond,
                    1, player?.Weapon == (int)PvpWeapon.Bow);
            }
        }

        private static Vector3 Position(PlayerSnapshot player) => new Vector3((float)player.PositionX, 0, (float)player.PositionY);

        private void ResetMatchView()
        {
            joined = null;
            ResetRoundView();
        }

        private void ResetRoundView()
        {
            snapshot = pendingSnapshot = null;
            Array.Clear(players, 0, players.Length);
            Array.Clear(pendingPlayers, 0, pendingPlayers.Length);
            sending = null;
            sentTick = cueSequence = 0;
            roomState = cue = null;
            occupancy = 0;
            winner = -1;
            disconnectedAt = -1;
            menu = false;
            backpackItem = PvpButtons.EquipSword;
            ResetInput();
            if (arena != null)
            {
                foreach (var arrow in arrows) arrow.gameObject.SetActive(false);
                arena.gameObject.SetActive(GameplayMode == RainPassMode);
                PoseFighters();
            }
        }

        private void ResetInput()
        {
            forward = back = left = right = 0;
            attack = guard = false;
            edges = PvpButtons.None;
            backpackCommand = PvpButtons.None;
        }

        private static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnGUI()
        {
            if (TournamentLobbyVisible) return;
            var matrix = GUI.matrix;
            float scale = Mathf.Min(1.4f, Screen.width / 730f, Screen.height / 690f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            DrawPlayer(0, new Rect(20, 20, 270, 140));
            DrawPlayer(1, new Rect(width - 290, 20, 270, 140));
            if (InCombat)
            {
                var label = snapshot?.PeerConnected == false ? "Opponent reconnecting" :
                    Time.unscaledTime < cueUntil ? cue : "Esc  Menu";
                GUI.Box(new Rect(width * .5f - 90, 20, 180, 30), label);
            }
            else
            {
                float panelHeight = IsTournament ? 468 : 340;
                GUILayout.BeginArea(new Rect(width * .5f - 205, height * .5f - panelHeight / 2, 410, panelHeight), GUI.skin.box);
                GUILayout.Label(IsTournament ? "Dual-Mode Tournament" : mode == RainPassMode ? "Rain Pass PvP" : "BBads PvP");
                if (connection == null && joined == null)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Rain Pass")) SelectMode(RainPassMode);
                    if (GUILayout.Button("BBads")) SelectMode(BBadsMode);
                    if (GUILayout.Button("Tournament")) SelectMode(PvpTournamentRules.Tournament);
                    GUILayout.EndHorizontal();
                    serverUrl = Field("Server", serverUrl);
                    var editedRoom = Field("Room", roomId);
                    if (editedRoom != roomId) { roomId = editedRoom; rematch = 0; activeRoom = null; }
                    playerId = Field("Player", playerId);
                    if (GUILayout.Button("Join room")) _ = ConnectAsync();
                }
                else
                {
                    GUILayout.Label(RoomId + "   /   " + (joined == null ? "Connecting…" :
                        !IsConnected ? ConnectionStatus : roomState == "WaitingForPlayers" ? $"Waiting for players ({Mathf.Max(1, occupancy)}/2)" : roomState));
                    if (snapshot != null)
                    {
                        if (roomState == Running && !snapshot.PeerConnected) GUILayout.Label("Opponent reconnecting");
                        if (GameplayMode == BBadsMode) DrawBBadsPhase();
                    }
                    if (!IsTournament && IsFinished)
                    {
                        GUILayout.Label(winner < 0 ? "Match ended" : winner == joined?.PlayerSlot ? "Victory" : "Defeat");
                        if (GUILayout.Button("Play again")) _ = PlayAgainAsync();
                    }
                    else if (menu && IsConnected && GUILayout.Button("Resume controls"))
                    {
                        menu = false;
                    }
                    if (joined != null && !IsConnected && ConnectionStatus == "Disconnected" &&
                        GUILayout.Button("Join again")) _ = ReconnectAsync();
                }
                if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);
                if (owner != null && GUILayout.Button("Return to selection")) owner.ClosePvp();
                else if (owner == null && connection != null && GUILayout.Button("Leave room")) _ = LeaveAsync();
                GUILayout.EndArea();
            }
            GUI.matrix = matrix;
        }

        private void DrawPlayer(int slot, Rect area)
        {
            var player = players[slot];
            if (player == null || string.IsNullOrEmpty(player.PlayerId)) return;
            GUILayout.BeginArea(area, GUI.skin.box);
            var heading = player.PlayerId + (slot == joined?.PlayerSlot ? " (you)" : "");
            GUILayout.Label(heading);
            if (GameplayMode == RainPassMode)
            {
                GUILayout.Label($"HP {player.Health}   Posture {player.Posture}   Arrows {player.Arrows}");
                if (player.FacingX != 0 || player.FacingY != 0)
                    if (player.LockedTarget == 1 - slot) GUILayout.Label("Locked on");
            }
            else
            {
                GUILayout.Label($"HP {player.Health}   {(player.Ready ? "Ready" : "Preparing")}");
                DrawBackpack(player, editable: false);
            }
            GUILayout.EndArea();
        }

        private string ConnectionStatus => disconnectedAt >= 0 && Time.unscaledTime - disconnectedAt >= 12
            ? "Disconnected" : "Reconnecting…";

        private void DrawBBadsPhase()
        {
            switch (snapshot.Phase)
            {
                case "Preparing":
                    GUILayout.Label("Prepare your backpack");
                    var local = players[joined.PlayerSlot];
                    if (local != null && !local.Ready && IsConnected && snapshot.PeerConnected)
                    {
                        backpackItem = GUILayout.Toolbar(backpackItem == PvpButtons.EquipSword ? 0 : 1,
                            new[] { "Weapon", "Fish" }) == 0 ? PvpButtons.EquipSword : PvpButtons.EquipBow;
                        bool wasEnabled = GUI.enabled;
                        GUI.enabled = wasEnabled && backpackCommand == PvpButtons.None;
                        DrawBackpack(local, editable: true);
                        if (GUILayout.Button("Ready")) backpackCommand = PvpButtons.Finish;
                        GUI.enabled = wasEnabled;
                    }
                    break;
                case "Countdown":
                    GUILayout.Label($"Battle starts in {Mathf.CeilToInt(snapshot.RemainingTicks / (float)CombatRun.StepsPerSecond)}");
                    break;
                case "Fighting":
                    GUILayout.Label("Auto battle");
                    break;
            }
        }

        private void DrawBackpack(PlayerSnapshot player, bool editable)
        {
            var slots = new string[3, 2];
            foreach (var placement in player.Backpack ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(placement)) continue;
                var parts = placement.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int x) &&
                    int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int z) && x < 3 && z < 2)
                    slots[x, z] = parts[0];
            }
            for (int z = 0; z < 2; z++)
            {
                GUILayout.BeginHorizontal();
                for (int x = 0; x < 3; x++)
                {
                    var label = slots[x, z] ?? "Empty";
                    if (!editable) GUILayout.Box(label, GUILayout.Width(78));
                    else if (GUILayout.Button(label))
                    {
                        backpackCell = new Vector2Int(x, z);
                        backpackCommand = backpackItem | PvpButtons.AttackPressed;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(65));
            var edited = GUILayout.TextField(value, 128);
            GUILayout.EndHorizontal();
            return edited;
        }

        internal static string RoomForRound(string room, int round)
        {
            if (round == 0) return room;
            string suffix = "-r" + round.ToString(CultureInfo.InvariantCulture);
            return room.Substring(0, Math.Min(room.Length, 48 - suffix.Length)) + suffix;
        }

        private async Task ReconnectAsync()
        {
            if (IsTournament) await DisconnectAsync();
            else await LeaveAsync();
            if (this != null && isActiveAndEnabled) await ConnectAsync();
        }

        internal Task LeaveAsync()
        {
            var closing = DisconnectAsync(leave: true);
            tournament = null;
            ResetMatchView();
            return closing;
        }

        private sealed class Connection : IDisposable
        {
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
            public Connection(Uri websocket)
            {
                Session = new PvpClientSession(websocket);
                Token = lifetime.Token;
            }

            public PvpClientSession Session { get; }
            public CancellationToken Token { get; }
            public ConcurrentQueue<Action> Messages { get; } = new ConcurrentQueue<Action>();
            public void Post(Action apply)
            {
                if (!Token.IsCancellationRequested) Messages.Enqueue(apply);
            }

            public void Cancel() => lifetime.Cancel();

            public void Dispose() => lifetime.Dispose();
        }
    }
}
