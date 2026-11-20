using System;
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
        [SerializeField] private int seed = 27041;
        [SerializeField, Range(0, 120)] private int detailDensity = 65;
        [SerializeField, Range(0, 1200)] private int rainDensity = 485;
        [SerializeField, Range(0, 1)] private float soundVolume = .62f;
        [SerializeField] private Color moonlight = new Color(.66f, .79f, .94f);
        [SerializeField] private Color fogColor = new Color(.115f, .15f, .187f);
        [SerializeField, Range(.7f, 1.3f)] private float characterScale = 1;
        private PassWorld world;
        private PassSpace space;
        private CombatRun run;
        private PassEffects effects;
        private PassHud hud;
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

        public PassWorld GeneratePreview()
        {
            var preview = BuildWorld(true);
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
        public void RestartRun() => run?.Restart();
        public void RetryEncounter() => run?.Retry();
        internal CombatRun Run => run;
        internal PassSpace Space => space;
        internal void SetEvidenceCamera(bool enabled) => fixedCamera = enabled;

        protected override void ConfigureHost(GameHost host)
        {
            input = new InputService(host.Events);
            backend = new UnityInputBackend();
            backend.MapKey("keyboard/tab", KeyCode.Tab);
            backend.MapKey("keyboard/e", KeyCode.E);
            backend.MapKey("keyboard/r", KeyCode.R);
            backend.MapKey("keyboard/f2", KeyCode.F2);
            backend.MapKey("keyboard/f3", KeyCode.F3);
            var context = input.CreateContext("rain-pass");
            context.Bind("forward", "keyboard/w");
            context.Bind("back", "keyboard/s");
            context.Bind("left", "keyboard/a");
            context.Bind("right", "keyboard/d");
            context.Bind("attack", "mouse/left");
            context.Bind("guard", "mouse/right");
            context.Bind("dodge", "keyboard/space");
            context.Bind("lock", "keyboard/tab");
            context.Bind("finish", "keyboard/e");
            context.Bind("pause", "keyboard/escape");
            context.Bind("retry", "keyboard/r");
            context.Bind("restart", "keyboard/enter");
            context.Bind("debug", "keyboard/f2");
            context.Bind("fixed", "keyboard/f3");
            inputEvents = host.Events.Subscribe<InputActionEvent>(Receive);
            host.AddModule(new InputModule(input, backend));
        }

        protected override Task OnStartedAsync()
        {
            previousRender = new RenderState();
            previousRender.Capture();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .018f;
            RenderSettings.fogColor = fogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .65f, .77f);
            RenderSettings.ambientEquatorColor = new Color(.31f, .36f, .42f);
            RenderSettings.ambientGroundColor = new Color(.18f, .20f, .23f);
            world = BuildWorld(false);
            space = new PassSpace(world, characterScale);
            run = new CombatRun(space);
            var cameraNode = PassWorld.Group("Shoulder camera", world.Root.transform);
            lens = cameraNode.gameObject.AddComponent<Camera>();
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = fogColor;
            lens.fieldOfView = 58;
            lens.nearClipPlane = .075f;
            lens.farClipPlane = 155;
            cameraNode.gameObject.AddComponent<AudioListener>();
            effects = new PassEffects(world, effectsShader, rainDensity, soundVolume);
            hud = new PassHud();
            run.Cue += ReceiveCue;
            run.Rebuilt += ResetPresentation;
            ResetPresentation();
            if (PassEvidence.Requested) gameObject.AddComponent<PassEvidence>().Initialize(this);
            else LockCursor(true);
            return Task.CompletedTask;
        }

        private PassWorld BuildWorld(bool preview)
        {
            var built = new PassWorld(transform, surfaceShader, seed, detailDensity, preview);
            var moon = PassWorld.Group("Moon key", built.Root.transform);
            moon.localRotation = Quaternion.Euler(43, -126, 0);
            var key = moon.gameObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = moonlight;
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
                for (int steps = 0; accumulator >= StepSeconds && steps < 6; steps++)
                {
                    accumulator -= StepSeconds;
                    run.Step(new Numerics.Vector3(direction.x, 0, direction.z), new Numerics.Vector3(forward.x, 0, forward.z), guard);
                }
            }
            if (!paused)
            {
                for (int actor = 0; actor < run.Count; actor++)
                {
                    var view = run.View(actor);
                    var fighter = space.Fighter(actor);
                    fighter.Controller.enabled = view.Vitality.Alive;
                    fighter.Pose(view, (float)run.Tick / CombatRun.StepsPerSecond, 1 - Mathf.Exp(-22 * delta));
                }
                space.Gates(run.Status == RunStatus.Proceed, 1 - Mathf.Exp(-7 * delta));
                effects.Advance(run, space, delta);
                shake = Mathf.MoveTowards(shake, 0, delta * 1.9f);
                CameraPosition(delta);
            }
        }

        private bool Playing => run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed;

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
            shake = 0;
            noticeUntil = 0;
            effects.Reset();
            CameraPosition(0);
            LockCursor(!PassEvidence.Requested);
        }

        private void ReceiveCue(CombatCue cue)
        {
            if (cue.Result == ContactResult.Fall || cue.Result == ContactResult.Finish)
                space.Fighter(cue.Target).Controller.enabled = false;
            effects.Cue(cue, space);
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
            if (run == null || PassEvidence.Requested) return;
            bool pressed = message.Phase == InputActionPhase.Pressed;
            bool held = message.Phase != InputActionPhase.Released;
            switch (message.Action)
            {
                case "forward": forwardHeld = held; break;
                case "back": backHeld = held; break;
                case "right": rightHeld = held; break;
                case "left": leftHeld = held; break;
                case "guard": guard = held; if (pressed) run.Queue(CombatButtons.Guard); break;
                case "attack": if (pressed) run.Queue(CombatButtons.Attack); break;
                case "lock": if (pressed) run.Queue(CombatButtons.Lock); break;
                case "dodge": if (pressed) run.Queue(CombatButtons.Dodge); break;
                case "finish": if (pressed) run.Queue(CombatButtons.Finish); break;
                case "pause": if (pressed) run.TogglePause(); break;
                case "retry": if (pressed && (run.Status == RunStatus.Paused || run.Status == RunStatus.Defeated)) run.Retry(); break;
                case "restart": if (pressed && !Playing) run.Restart(); break;
                case "debug": if (pressed) debug = !debug; break;
                case "fixed": if (pressed) fixedCamera = !fixedCamera; break;
            }
        }

        private void OnGUI()
        {
            if (hud == null || run == null) return;
            hud.Draw(run, space, lens, debug, seed, world.LayoutDigest, Time.unscaledTime < noticeUntil ? notice : "", noticeColor);
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
