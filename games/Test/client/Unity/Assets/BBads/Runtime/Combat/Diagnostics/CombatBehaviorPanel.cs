#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using BBads.Combat.Behavior;
using BBads.Data;
using UnityEngine;

namespace BBads.Combat.Diagnostics
{
    // 运行时行为模式切换面板（IMGUI）。只在编辑器和 Development Build 里编译。
    // 控件文字用英文，因为内置 GUI 字体在 Player 里不一定有中文字形；
    // 说明文字用中文，编辑器下能正常显示。
    public sealed class CombatBehaviorPanel : MonoBehaviour
    {
        [SerializeField] CombatWorld world;
        [SerializeField] KeyCode toggleKey = KeyCode.F1;
        [SerializeField] bool visible = true;
        [SerializeField] Rect windowRect = new Rect(12f, 12f, 330f, 0f);

        static readonly float[] TimeScales = { 0f, 0.25f, 0.5f, 1f, 2f };
        static readonly string[] TimeScaleLabels = { "Pause", "0.25x", "0.5x", "1x", "2x" };

        string[] modeLabels;
        Vector2 actorScroll;
        bool showActors;
        BBadsBattleClock clock;

        public void Configure(CombatWorld value, BBadsBattleClock valueClock)
        {
            world = value;
            clock = valueClock;
        }

        void Awake()
        {
            world ??= GetComponent<CombatWorld>();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || world == null)
                return;
            EnsureLabels();
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Combat Behavior");
        }

        void EnsureLabels()
        {
            if (modeLabels != null)
                return;
            IReadOnlyList<CombatBehaviorProfile> profiles = CombatBehaviorLibrary.Profiles;
            modeLabels = new string[profiles.Count];
            for (int i = 0; i < profiles.Count; i++)
                modeLabels[i] = profiles[i].DisplayName;
        }

        void DrawWindow(int id)
        {
            IReadOnlyList<CombatBehaviorProfile> profiles = CombatBehaviorLibrary.Profiles;

            GUILayout.Label("— 战术行为 / Tactics —");
            bool perCamp = GUILayout.Toggle(world.OverridePerCamp, "Split by camp 分阵营");
            if (perCamp != world.OverridePerCamp)
                world.SetOverridePerCamp(perCamp);

            if (!perCamp)
            {
                DrawModeSelector("All units", profiles, world.GlobalMode, world.SetGlobalMode);
                GUILayout.Label(CombatBehaviorLibrary.DescribeMode(world.GlobalMode), DescriptionStyle);
            }
            else
            {
                DrawModeSelector(
                    "Player camp",
                    profiles,
                    world.PlayerMode,
                    mode => world.SetCampMode(UnitCamp.Player, mode));
                DrawModeSelector(
                    "Enemy camp",
                    profiles,
                    world.EnemyMode,
                    mode => world.SetCampMode(UnitCamp.Enemy, mode));
            }

            GUILayout.Space(6f);
            GUILayout.Label("— 站位子系统 / Spacing —");
            // 这是独立于战术模式的另一套开关：它同时影响 BodyRadius 与出生避让半径，
            // 所以不并入模式枚举，单独摆在这里对比。
            bool improved = GUILayout.Toggle(
                world.UseImprovedEngagementAvoidance,
                "Reserved engagement slots 槽位预约");
            if (improved != world.UseImprovedEngagementAvoidance)
                world.SetImprovedEngagementAvoidance(improved);

            GUILayout.Space(6f);
            GUILayout.Label("— 时间 / Time —");
            if (clock != null)
            {
                int currentScale = IndexOfTimeScale(clock.PlaybackSpeed);
                int nextScale = GUILayout.SelectionGrid(currentScale, TimeScaleLabels, 5);
                if (nextScale != currentScale && nextScale >= 0)
                    clock.SetPlaybackSpeed(TimeScales[nextScale]);
            }

            GUILayout.Space(6f);
            showActors = GUILayout.Toggle(showActors, $"Actors ({world.Actors.Count})");
            if (showActors)
                DrawActorList();

            GUILayout.Label($"[{toggleKey}] hide/show", DescriptionStyle);
            GUI.DragWindow();
        }

        void DrawModeSelector(
            string header,
            IReadOnlyList<CombatBehaviorProfile> profiles,
            CombatBehaviorMode current,
            System.Action<CombatBehaviorMode> apply)
        {
            GUILayout.Label(header);
            int currentIndex = IndexOfMode(profiles, current);
            int nextIndex = GUILayout.SelectionGrid(currentIndex, modeLabels, 2);
            if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < profiles.Count)
                apply(profiles[nextIndex].Mode);
        }

        void DrawActorList()
        {
            actorScroll = GUILayout.BeginScrollView(actorScroll, GUILayout.Height(120f));
            IReadOnlyList<CombatActor> actors = world.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                CombatActor actor = actors[i];
                if (actor == null || !actor.IsAlive)
                    continue;
                string targetName = DescribeTarget(actor.CurrentTarget);
                string state = actor.AttackLocked
                    ? "attack"
                    : actor.CurrentIntent.allowAttack ? "move" : "hold-fire";
                GUILayout.Label(
                    $"{actor.Definition.id} [{actor.Camp}] {Mathf.RoundToInt(actor.HealthRatio * 100f)}% " +
                    $"{state} → {targetName}",
                    DescriptionStyle);
            }
            GUILayout.EndScrollView();
        }

        static string DescribeTarget(ICombatTarget target)
        {
            if (target == null)
                return "none";
            if (target is PlayerBase)
                return "base";
            return target is CombatActor actor ? actor.Definition.id : "?";
        }

        static int IndexOfMode(IReadOnlyList<CombatBehaviorProfile> profiles, CombatBehaviorMode mode)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].Mode == mode)
                    return i;
            }
            return -1;
        }

        static int IndexOfTimeScale(float value)
        {
            for (int i = 0; i < TimeScales.Length; i++)
            {
                if (Mathf.Abs(TimeScales[i] - value) < 0.001f)
                    return i;
            }
            return -1;
        }

        static GUIStyle descriptionStyle;

        static GUIStyle DescriptionStyle
        {
            get
            {
                if (descriptionStyle == null)
                {
                    descriptionStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 10 };
                }
                return descriptionStyle;
            }
        }
    }
}
#endif
