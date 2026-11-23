using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Input;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using Numerics = System.Numerics;

namespace Test.Client.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatShowcase : GameHostBehaviour
    {
        public const string ScenePath = "Assets/Scenes/CombatShowcase.unity";
        private const double StepSeconds = 1.0 / CombatRun.StepsPerSecond;
        [SerializeField] private Shader surfaceShader;
        [SerializeField] private Shader effectsShader;
        [SerializeField] private RainPassEnvironmentProfile environmentProfile;
        [SerializeField] private RainPassEnvironmentMode environmentMode = RainPassEnvironmentMode.RuntimeGenerated;
        [SerializeField] private int seed = 27041;
        [SerializeField, Range(0, 120)] private int detailDensity = 65;
        [SerializeField, Range(0, 1200)] private int rainDensity = 485;
        [SerializeField, Range(0, 1)] private float soundVolume = .62f;
        [SerializeField, Range(0, 1)] private float musicVolume = .65f;
        [SerializeField] private Color moonlight = new Color(.66f, .79f, .94f);
        [SerializeField] private Color fogColor = new Color(.115f, .15f, .187f);
        [SerializeField, Range(.7f, 1.3f)] private float characterScale = 1;
        private PassWorld world;
        private PassSpace space;
        private CombatRun run;
        private PassEffects effects;
        private PassAudio audio;
        private (PassWorld World, PassSpace Space, CombatRun Run, PassEffects Effects, PassAudio Audio) original;
        private PassHud hud;
        private IReadOnlyList<TrialOpponent> opponents;
        private IReadOnlyList<MapBossOption> mapBossOptions;
        private MapBossCatalogResult mapBossCatalog;
        private string mapBossError;
        private TrialSession trials;
        private string trialCatalogError;
        private bool expedition;
        private Vector2 opponentScroll;
        private Camera lens;
        private InputService input;
        private UnityInputBackend backend;
        private IDisposable inputEvents;
        private bool forwardHeld, backHeld, leftHeld, rightHeld;
        private bool guard, paused, debug, fixedCamera, previousFocus;
        private float yaw, pitch = 17, noticeUntil, shake;
        private string notice = "";
        private Color noticeColor;
        private double accumulator;
        private RenderState previousRender;

        public RainPassEnvironmentProfile EnvironmentProfile => environmentProfile;
        public RainPassEnvironmentMode EnvironmentMode => environmentMode;
        public RainPassEnvironmentParameters GetEnvironmentParameters()
        {
            return environmentProfile != null ? environmentProfile.Parameters.Clone() : new RainPassEnvironmentParameters
            {
                seed = seed,
                detailDensity = detailDensity,
                moonlight = moonlight,
                fogColor = fogColor
            };
        }

        public PassWorld GenerateEnvironmentForBake()
        {
            return BuildWorld(false, true);
        }

        public PassWorld GeneratePreview()
        {
            var preview = BuildWorld(true, false);
            try
            {
                var previewSpace = new PassSpace(preview, characterScale);
                var previewRun = new CombatRun(previewSpace);
                for (int actor = 0; actor < previewRun.Count; actor++) previewSpace.Fighter(actor).Pose(previewRun.View(actor), 0, 1);
                return preview;
            }
            catch { preview.Dispose(); throw; }
        }

        public string LayoutDigest => world?.LayoutDigest;
        public int ArchitectureCount => world?.ArchitectureCount ?? 0;
        public string CurrentArea => run?.Encounter.ToString();
        public int SimulationTick => run?.Tick ?? 0;
        public bool IsTrialSelection => run != null && !expedition && run.TrialOpponent == null;
        public bool TrialsReady => opponents != null;
        public int? ActiveTrialId => run?.TrialOpponent?.Id;
        public CombatMapId? ActiveMapId => run?.Map?.Id;
        public string BossConfigurationHash => mapBossCatalog?.ConfigurationHash;

        public void RestartRun()
        {
            if (run == null || trials?.IsSaving == true) return;
            expedition = true;
            run.Restart();
        }

        public void RetryEncounter()
        {
            if (trials?.IsSaving == true) return;
            if (run?.TrialOpponent != null) trials.Start(run.TrialOpponent);
            else run?.Retry();
        }

        public void ShowTrialSelection()
        {
            if (run == null || trials?.IsSaving == true) return;
            Debug.Log($"Rain Pass stage=return-selection map={run.Map?.Name ?? "Original"} objects={GetComponentsInChildren<Transform>(true).Length}.", this);
            expedition = false;
            if (original.World != null)
            {
                audio.Sleep();
                world.Root.SetActive(false);
                world.Dispose();
                (world, space, run, effects, audio) = original;
                original = default;
                world.Root.SetActive(true);
                ApplyEnvironment(null);
            }
            run.Restart();
        }

        public bool StartMap(CombatMapId id)
        {
            if (!IsTrialSelection || trials?.IsSaving == true) return false;
            foreach (var map in CombatMap.Routes)
            {
                if (map.Id == id) return StartRoute(map);
            }
            Debug.LogWarning($"Rain Pass could not open route '{id}'.", this);
            return false;
        }

        public bool StartMapBoss(int id)
        {
            if (!IsTrialSelection || trials?.IsSaving == true || mapBossOptions == null) return false;
            foreach (var option in mapBossOptions)
                if (option.Id == id) return StartRoute(option.Route);
            Debug.LogWarning($"Rain Pass could not open BossChallenge ID {id}.", this);
            return false;
        }

        private bool StartRoute(CombatMap map)
        {
            Debug.Log($"Rain Pass stage=route-building map={map.Name} boss={map.Boss.Name} config={BossConfigurationHash}.", this);
            var nextWorld = BuildWorld(false, false, map);
            try
            {
                var nextSpace = new PassSpace(nextWorld, characterScale);
                var nextRun = new CombatRun(nextSpace, map);
                var nextEffects = new PassEffects(nextWorld, effectsShader, map.Id == CombatMapId.CedarGrove ? rainDensity / 3 : 0);
                var nextAudio = new PassAudio(nextWorld, soundVolume, musicVolume);
                original = (world, space, run, effects, audio);
                audio.Sleep();
                world.Root.SetActive(false);
                (world, space, run, effects, audio) = (nextWorld, nextSpace, nextRun, nextEffects, nextAudio);
            }
            catch
            {
                nextWorld.Dispose();
                throw;
            }
            Physics.SyncTransforms();
            run.Cue += ReceiveCue;
            run.Rebuilt += ResetPresentation;
            expedition = true;
            ApplyEnvironment(map);
            ResetPresentation();
            Debug.Log($"Rain Pass stage=route-ready map={map.Name} boss={map.Boss.Name} objects={GetComponentsInChildren<Transform>(true).Length}.", this);
            return true;
        }

        public bool StartTrial(int opponentId)
        {
            if (opponents == null || !IsTrialSelection) return false;
            foreach (var opponent in opponents)
            {
                if (opponent.Id != opponentId) continue;
                trials.Start(opponent);
                return true;
            }
            Debug.LogWarning($"Rain Pass could not start trial for unknown opponent ID {opponentId}.", this);
            return false;
        }
        internal CombatRun Run => run;
        internal PassSpace Space => space;
        internal void SetEvidenceCamera(bool enabled) => fixedCamera = enabled;
        internal void RetryTrialSave() => _ = ObserveTrialSave(trials.SaveRecordAsync());

        protected override void ConfigureHost(GameHost host)
        {
            input = new InputService(host.Events);
            backend = new UnityInputBackend();
            backend.MapMouse("mouse/middle", 2);
            backend.MapKey("keyboard/r", KeyCode.R);
            backend.MapKey("keyboard/1", KeyCode.Alpha1);
            backend.MapKey("keyboard/2", KeyCode.Alpha2);
            backend.MapKey("keyboard/f2", KeyCode.F2);
            backend.MapKey("keyboard/f3", KeyCode.F3);
            var context = input.CreateContext("rain-pass");
            context.Bind("forward", "keyboard/w");
            context.Bind("back", "keyboard/s");
            context.Bind("left", "keyboard/a");
            context.Bind("right", "keyboard/d");
            context.Bind("attack", "mouse/left");
            context.Bind("guard", "mouse/right");
            context.Bind("sword", "keyboard/1");
            context.Bind("bow", "keyboard/2");
            context.Bind("dodge", "keyboard/space");
            context.Bind("lock", "mouse/middle");
            context.Bind("pause", "keyboard/escape");
            context.Bind("retry", "keyboard/r");
            context.Bind("restart", "keyboard/enter");
            context.Bind("debug", "keyboard/f2");
            context.Bind("fixed", "keyboard/f3");
            inputEvents = host.Events.Subscribe<InputActionEvent>(Receive);
            host.AddModule(new InputModule(input, backend));
        }

        protected override async Task OnStartedAsync()
        {
            previousRender = new RenderState();
            previousRender.Capture();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .018f;
            RenderSettings.fogColor = EffectiveEnvironmentParameters.fogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .65f, .77f);
            RenderSettings.ambientEquatorColor = new Color(.31f, .36f, .42f);
            RenderSettings.ambientGroundColor = new Color(.18f, .20f, .23f);
            world = BuildWorld(false, false);
            Debug.Log("Rain Pass stage=environment-built.", this);
            space = new PassSpace(world, characterScale);
            run = new CombatRun(space);
            var cameraNode = PassWorld.Group("Shoulder camera", transform);
            lens = cameraNode.gameObject.AddComponent<Camera>();
            lens.clearFlags = CameraClearFlags.SolidColor;
            ApplyEnvironment(null);
            lens.fieldOfView = 58;
            lens.nearClipPlane = .075f;
            lens.farClipPlane = 155;
            cameraNode.gameObject.AddComponent<AudioListener>();
            effects = new PassEffects(world, effectsShader, rainDensity);
            audio = new PassAudio(world, soundVolume, musicVolume);
            hud = new PassHud();
            run.Cue += ReceiveCue;
            run.Rebuilt += ResetPresentation;
            expedition = PassEvidence.Requested;
            ResetPresentation();
            try
            {
                mapBossCatalog = await MapBossCatalog.LoadAsync();
                mapBossOptions = mapBossCatalog;
                Debug.Log($"Rain Pass stage=configuration-loaded hash={mapBossCatalog.ConfigurationHash} options={mapBossCatalog.Count}.", this);
            }
            catch (Exception error)
            {
                mapBossError = "Boss challenges could not be loaded. See the log.";
                Debug.LogError($"Rain Pass BossChallenge or BossStrike configuration failed. {error}", this);
            }
            if (this == null) return;
            if (expedition)
            {
                gameObject.AddComponent<PassEvidence>().Initialize(this);
                return;
            }

            IReadOnlyList<TrialOpponent> catalog;
            try { catalog = await TrialCatalog.LoadAsync(); }
            catch (Exception error)
            {
                trialCatalogError = "Opponent configuration could not be loaded. See the log for details.";
                Debug.LogError($"Rain Pass configuration (rain-pass-opponents.json) failed. {error}", this);
                return;
            }
            var saveDirectory = Path.Combine(UnityEngine.Application.persistentDataPath, "RainPassTrials");
            trials = new TrialSession(run, saveDirectory);
            try { await trials.OpenAsync(); }
            catch (Exception error) { Debug.LogError($"Rain Pass could not read records from '{saveDirectory}'. {error}", this); }
            if (this != null) opponents = catalog;
        }

        private RainPassEnvironmentParameters EffectiveEnvironmentParameters
        {
            get
            {
                return environmentProfile != null ? environmentProfile.Parameters : new RainPassEnvironmentParameters
                {
                    seed = seed,
                    detailDensity = detailDensity,
                    moonlight = moonlight,
                    fogColor = fogColor
                };
            }
        }

        private void ApplyEnvironment(CombatMap map)
        {
            RenderSettings.fogDensity = map?.Id == CombatMapId.CedarGrove ? .031f : .018f;
            RenderSettings.fogColor = map != null ? PassWorld.RoutePalette.For(map.Id).Sky : EffectiveEnvironmentParameters.fogColor;
            lens.backgroundColor = RenderSettings.fogColor;
        }

        private PassWorld BuildWorld(bool preview, bool forceRuntime, CombatMap map = null)
        {
            var parameters = EffectiveEnvironmentParameters;
            if (map == null && !preview && !forceRuntime && environmentMode == RainPassEnvironmentMode.BakedAsset)
            {
                if (environmentProfile == null || !environmentProfile.HasCurrentBakedAsset)
                    throw new InvalidOperationException("Rain Pass BakedAsset mode requires a current baked environment profile.");
                return PassWorld.FromBakedPrefab(environmentProfile.BakedPrefab, transform, surfaceShader, parameters.seed);
            }

            var built = new PassWorld(transform, surfaceShader, parameters.seed, parameters.detailDensity, preview, map);
            var moon = PassWorld.Group("Moon key", built.Root.transform);
            moon.localRotation = Quaternion.Euler(43, -126, 0);
            var key = moon.gameObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = map != null ? PassWorld.RoutePalette.For(map.Id).Key : parameters.moonlight;
            key.shadows = LightShadows.Soft;
            var bounce = PassWorld.Group("Sky rim", built.Root.transform);
            bounce.localRotation = Quaternion.Euler(25, 92, 0);
            var fill = bounce.gameObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .32f;
            fill.color = new Color(.47f, .78f, 1);
            return built;
        }

        protected override void Update()
        {
            base.Update();
            if (run == null || Failure != null) return;
            bool automation = PassEvidence.Requested;
            bool focused = UnityEngine.Application.isFocused;
            if (!automation && !focused && previousFocus && Playing) run.TogglePause();
            previousFocus = focused;
            if (paused != (run.Status == RunStatus.Paused))
            {
                paused = run.Status == RunStatus.Paused;
                effects.Pause(paused);
                LockCursor(!paused && !automation);
                accumulator = 0;
            }
            if (!Playing && !automation) LockCursor(false);
            float delta = Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (!automation && Playing && focused)
            {
                if (Cursor.lockState == CursorLockMode.Locked && run.Locked == 0)
                {
                    yaw += Input.GetAxisRaw("Mouse X") * 2.1f;
                    pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 1.55f, 6, 43);
                }
                accumulator += delta;
                var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                var desired = Vector2.ClampMagnitude(new Vector2((rightHeld ? 1 : 0) - (leftHeld ? 1 : 0),
                    (forwardHeld ? 1 : 0) - (backHeld ? 1 : 0)), 1);
                var direction = forward * desired.y + right * desired.x;
                Numerics.Vector3? arrowAim = run.Weapon == PlayerWeapon.Bow && run.Locked == 0
                    ? space.AimPoint(lens.ViewportPointToRay(new Vector3(.5f, .5f))) : (Numerics.Vector3?)null;
                for (int steps = 0; accumulator >= StepSeconds && steps < 6; steps++)
                {
                    accumulator -= StepSeconds;
                    var movement = new Numerics.Vector3(direction.x, 0, direction.z);
                    var facing = new Numerics.Vector3(forward.x, 0, forward.z);
                    if (run.TrialOpponent == null) run.Step(movement, facing, guard, arrowAim);
                    else
                    {
                        Task save = trials.Step(movement, facing, guard, arrowAim);
                        if (save != null) _ = ObserveTrialSave(save);
                    }
                    audio.Step(run);
                    if (!Playing) break;
                }
            }
            audio.Advance(run, IsTrialSelection, delta);
            if (!paused)
            {
                for (int actor = 0; actor < run.Count; actor++)
                {
                    var view = run.View(actor);
                    var fighter = space.Fighter(actor);
                    fighter.Controller.enabled = view.Vitality.Alive;
                    fighter.Pose(view, (float)run.Tick / CombatRun.StepsPerSecond, 1 - Mathf.Exp(-22 * delta), actor == 0 && run.Weapon == PlayerWeapon.Bow);
                }
                space.DrawArrows(run.AirborneArrows);
                space.Gates(run.Status == RunStatus.Proceed, 1 - Mathf.Exp(-7 * delta));
                effects.Advance(run, space, delta);
                shake = Mathf.MoveTowards(shake, 0, delta * 1.9f);
                CameraPosition(delta);
            }
        }

        private bool Playing => !IsTrialSelection && (run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed);

        private async Task ObserveTrialSave(Task save)
        {
            try { await save; }
            catch (Exception error) { Debug.LogError($"Rain Pass could not save the trial record. {error}", this); }
        }

        private void CameraPosition(float delta)
        {
            var origin = space.Fighter(0).Root.position;
            var anchor = origin + Vector3.up * 1.58f;
            var focus = anchor + Quaternion.Euler(0, yaw, 0) * new Vector3(.79f, -.18f, 2f);
            if (run.Locked > 0)
            {
                var target = space.Fighter(run.Locked).Root.position;
                var toward = target - origin;
                yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, 1 - Mathf.Exp(-9 * delta));
                focus = Vector3.Lerp(anchor, target + Vector3.up * 1.24f, .56f) + Quaternion.Euler(0, yaw, 0) * Vector3.right * .37f;
            }
            var offset = Quaternion.Euler(pitch, yaw, 0) * new Vector3(1.69f, .69f, -4.65f);
            if (fixedCamera)
            {
                offset = new Vector3(-4.5f, 3.6f, -7.0f);
                focus = origin + new Vector3(0, 1.35f, 4.5f);
            }
            if (Physics.SphereCast(anchor, .19f, offset.normalized, out var hit, offset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) offset = offset.normalized * Mathf.Max(.28f, hit.distance - .16f);
            var desired = anchor + offset;
            desired += Quaternion.Euler(0, yaw, 0) * Vector3.right * Mathf.Sin(Time.unscaledTime * 142) * shake * .11f;
            lens.transform.position = delta <= 0 || fixedCamera ? desired : Vector3.Lerp(lens.transform.position, desired, 1 - Mathf.Exp(-18 * delta));
            lens.transform.rotation = Quaternion.LookRotation(focus - lens.transform.position);
        }

        private void ResetPresentation()
        {
            yaw = 0;
            pitch = 17;
            guard = false;
            forwardHeld = backHeld = leftHeld = rightHeld = false;
            accumulator = 0;
            shake = 0;
            noticeUntil = 0;
            effects.Reset();
            audio.Reset(run);
            CameraPosition(0);
            LockCursor(Playing && !PassEvidence.Requested);
        }

        private void ReceiveCue(CombatCue cue)
        {
            if (cue.Result == ContactResult.Fall || cue.Result == ContactResult.Finish)
                space.Fighter(cue.Target).Controller.enabled = false;
            effects.Cue(cue, space);
            audio.Cue(cue, run);
            bool deflect = cue.Result == ContactResult.Deflect;
            bool playerTarget = cue.Target == 0;
            notice = deflect ? (run.View(cue.Attacker).Broken ? "POSTURE BROKEN" : "DEFLECT")
                : cue.Result == ContactResult.Finish ? "FINISH"
                : cue.Result == ContactResult.Break ? (playerTarget ? "GUARD BROKEN" : "POSTURE BROKEN")
                : cue.Result == ContactResult.Guard && playerTarget ? "BLOCK" : "";
            if (notice.Length > 0)
            {
                noticeColor = deflect || cue.Result == ContactResult.Finish ? new Color(1f, .77f, .39f) : new Color(.95f, .56f, .43f);
                noticeUntil = Time.unscaledTime + .64f;
            }
            else noticeUntil = 0;
            if (deflect || playerTarget) shake = deflect ? .54f : .32f;
        }

        private void Receive(InputActionEvent message)
        {
            if (run == null || IsTrialSelection || PassEvidence.Requested) return;
            bool pressed = message.Phase == InputActionPhase.Pressed;
            bool held = message.Phase != InputActionPhase.Released;
            switch (message.Action)
            {
                case "forward": forwardHeld = held; break;
                case "back": backHeld = held; break;
                case "right": rightHeld = held; break;
                case "left": leftHeld = held; break;
                case "guard": guard = held; if (pressed) run.Queue(CombatButtons.Guard); break;
                case "attack":
                    if (pressed) run.Queue(CombatButtons.AttackPressed);
                    else if (!held) run.Queue(CombatButtons.AttackReleased);
                    break;
                case "sword": if (pressed) run.Queue(CombatButtons.EquipSword); break;
                case "bow": if (pressed) run.Queue(CombatButtons.EquipBow); break;
                case "lock": if (pressed) run.Queue(CombatButtons.Lock); break;
                case "dodge": if (pressed) run.Queue(CombatButtons.Dodge); break;
                case "pause": if (pressed) run.TogglePause(); break;
                case "retry": if (pressed && !Playing && (run.TrialOpponent != null || run.Status != RunStatus.Victory)) RetryEncounter(); break;
                case "restart":
                    if (pressed && !Playing)
                    {
                        if (run.TrialOpponent != null) ShowTrialSelection();
                        else RestartRun();
                    }
                    break;
                case "debug": if (pressed) debug = !debug; break;
                case "fixed": if (pressed) fixedCamera = !fixedCamera; break;
            }
        }

        private void OnGUI()
        {
            if (hud == null || run == null) return;
            if (IsTrialSelection)
            {
                hud.DrawTrialSelection(this, opponents, trials, mapBossOptions, trialCatalogError, mapBossError, ref opponentScroll);
                return;
            }
            hud.Draw(run, space, lens, debug, EffectiveEnvironmentParameters.seed, world.LayoutDigest, Time.unscaledTime < noticeUntil ? notice : "", noticeColor);
            if (run.TrialOpponent != null && !Playing) hud.DrawTrialResult(this, trials);
            else if (!PassEvidence.Requested && !Playing && GUI.Button(new Rect(Screen.width / 2f - 90f, Screen.height * .67f, 180f, 30f), "Route and trial selection"))
                ShowTrialSelection();
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        protected override void OnDestroy()
        {
            inputEvents?.Dispose();
            world?.Dispose();
            original.World?.Dispose();
            if (previousRender != null && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                previousRender.Restore();
            if (run != null) LockCursor(false);
            base.OnDestroy();
        }

        private sealed class RenderState
        {
            private bool fog;
            private FogMode mode;
            private float density;
            private Color color, sky, equator, ground;
            private UnityEngine.Rendering.AmbientMode ambientMode;

            public void Capture()
            {
                fog = RenderSettings.fog;
                mode = RenderSettings.fogMode;
                density = RenderSettings.fogDensity;
                color = RenderSettings.fogColor;
                sky = RenderSettings.ambientSkyColor;
                equator = RenderSettings.ambientEquatorColor;
                ground = RenderSettings.ambientGroundColor;
                ambientMode = RenderSettings.ambientMode;
            }

            public void Restore()
            {
                RenderSettings.fog = fog;
                RenderSettings.fogMode = mode;
                RenderSettings.fogDensity = density;
                RenderSettings.fogColor = color;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
            }
        }
    }
}
