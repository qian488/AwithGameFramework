using System.Collections.Generic;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    internal sealed class PassHud
    {
        private static readonly Color Ivory = new Color(.93f, .91f, .80f);
        private static readonly Color Amber = new Color(.92f, .66f, .31f);
        private static readonly Color Slate = new Color(.72f, .8f, .84f);
        private static readonly Color Panel = new Color(.027f, .048f, .06f, .87f);
        private static readonly Color Teal = new Color(.28f, .75f, .76f);
        private static readonly Color Red = new Color(.96f, .38f, .27f);
        private GUIStyle title, small, wrappedSmall, middle, centered, headline;
        private float smoothed;

        public void Draw(CombatRun run, PassSpace space, Camera lens, bool diagnostics, int seed, string digest, string cue, Color cueColor)
        {
            PrepareStyles();
            var matrix = GUI.matrix;
            float scale = Mathf.Clamp(Mathf.Min(Screen.height / 810f, Screen.width / 870f), .4f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            Fill(new Rect(29, 28, 329, 110), Panel);
            Fill(new Rect(29, 28, 3, 110), Amber);
            Label(new Rect(47, 34, 294, 49), run.Map?.Name ?? "RAIN PASS", title, Ivory);
            string area = run.Encounter == Encounter.Practice ? "PRACTICE TERRACE" : run.Encounter == Encounter.Courtyard ? "LANTERN COURTYARD" : "WARDEN'S GATE";
            if (run.TrialOpponent != null) area = run.TrialOpponent.Name;
            if (run.Map != null) area = run.Encounter == Encounter.Courtyard ? run.Map.Patrol.Name : run.Map.Boss.Name;
            Label(new Rect(48, 83, 290, 22), area, middle, Amber);
            string objective = run.TrialOpponent == null ? Objective(run) : $"TRIAL TIME   {FormatTime(run.Tick)}";
            Label(new Rect(48, 106, 295, 29), objective, wrappedSmall, Slate);

            var player = run.View(0);
            float bottom = height - 103;
            Fill(new Rect(29, bottom, 278, 72), Panel);
            Label(new Rect(44, bottom + 6, 70, 20), "VITALITY", small, Slate);
            Bar(new Rect(112, bottom + 11, 176, 11), (float)player.Vitality.Health / player.HealthLimit, Teal);
            Label(new Rect(44, bottom + 31, 74, 20), "POSTURE", small, Slate);
            Bar(new Rect(120, bottom + 36, 168, 10), (float)player.Vitality.Posture / player.PostureLimit, player.Broken ? Red : Amber);
            Label(new Rect(44, bottom + 51, 242, 17), player.Broken ? "Guard broken" : run.Locked > 0 ? "MMB   Unlock target" : "MMB   Lock target", small, player.Broken ? Red : Slate);

            if (run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed)
            {
                bool bow = run.Weapon == PlayerWeapon.Bow;
                Fill(new Rect(29, bottom - 36, 278, 30), Panel);
                string weapon = bow ? $"BOW   {run.ArrowsRemaining}/{BowRules.QuiverCapacity} arrows" : "SWORD";
                if (bow && run.ArrowsRemaining == 0 && player.Phase == AttackPhase.Ready) weapon = "QUIVER EMPTY   1  Equip sword";
                Label(new Rect(44, bottom - 34, 242, 26), weapon, small, bow && run.ArrowsRemaining == 0 ? Amber : Ivory);
                if (bow && run.Locked == 0)
                {
                    Fill(new Rect(width / 2 - 6, height / 2 - 1, 12, 2), Amber);
                    Fill(new Rect(width / 2 - 1, height / 2 - 6, 2, 12), Amber);
                }
                for (int actor = 1; actor < run.Count; actor++)
                {
                    var enemy = run.View(actor);
                    if (!enemy.Vitality.Alive) continue;
                    var point = lens.WorldToScreenPoint(space.Fighter(actor).Root.position + Vector3.up * (run.Encounter == Encounter.Gate ? 3.07f : 2.52f));
                    if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                    if (Vector3.Distance(space.Fighter(0).Root.position, space.Fighter(actor).Root.position) > 11) continue;
                    float center = point.x / scale;
                    float top = height - point.y / scale;
                    float extent = run.Locked == actor ? 88 : 64;
                    Bar(new Rect(center - extent / 2, top, extent, 5), (float)enemy.Vitality.Health / enemy.HealthLimit, Red);
                    Bar(new Rect(center - extent / 2, top + 7, extent, 4), (float)enemy.Vitality.Posture / enemy.PostureLimit, Amber);
                    if (run.Locked == actor) Label(new Rect(center - 20, top - 22, 40, 21), "<> ", centered, Ivory);
                }
                if (!string.IsNullOrEmpty(cue)) Label(new Rect(width / 2 - 182, height * .30f, 364, 33), cue, centered, cueColor);
                if (run.CanFinish)
                {
                    float promptTop = height * .71f;
                    Label(new Rect(width / 2 - 182, promptTop, 364, 38), "HOLD LMB   FINISH", centered, Amber);
                    Bar(new Rect(width / 2 - 78, promptTop + 34, 156, 4), run.FinishHoldProgress, Amber);
                }
                float left = Mathf.Max(29, width - 335);
                if (left > 320)
                {
                    Fill(new Rect(left, bottom, 304, 72), Panel);
                    Label(new Rect(left + 14, bottom + 4, 286, 24), bow ? "LMB   Draw and shoot   MMB   Lock target" : "LMB   Strike       RMB   Deflect / Guard", small, Ivory);
                    Label(new Rect(left + 14, bottom + 29, 286, 19), "WASD  Move   SPACE  Dodge   ESC  Pause", small, Slate);
                    Label(new Rect(left + 14, bottom + 48, 286, 18), "1   Sword       2   Bow", small, Slate);
                }
            }
            else if (run.TrialOpponent == null)
            {
                Fill(new Rect(0, 0, width, height), new Color(.012f, .029f, .038f, .68f));
                float left = width / 2 - 248;
                float top = height / 2 - 93;
                Fill(new Rect(left, top, 496, 186), Panel);
                Fill(new Rect(left, top, 496, 2), Amber);
                string heading = run.Status == RunStatus.Victory ? "PASS SECURED" : run.Status == RunStatus.Defeated ? "FALLEN" : "PAUSED";
                Label(new Rect(left, top + 24, 496, 68), heading, headline, Ivory);
                string options = run.Status == RunStatus.Paused ? "ESC  Resume     R  Retry checkpoint     ENTER  Restart"
                    : run.Status == RunStatus.Defeated ? "R  Retry checkpoint     ENTER  Restart" : "ENTER  Begin a new run";
                Label(new Rect(left, top + 108, 496, 40), options, centered, Amber);
            }
            if (diagnostics)
            {
                if (Event.current.type == EventType.Repaint) smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime * 1000, .05f);
                float left = width - 294;
                Fill(new Rect(left, 28, 266, 108), Panel);
                Label(new Rect(left + 12, 32, 244, 24), $"Seed {seed}   {digest.Substring(0, 10)}", small, Ivory);
                Label(new Rect(left + 12, 57, 244, 24), $"{smoothed:0.0} ms   Step {run.Tick}   Owner {run.AttackOwner}", small, Ivory);
                string phase = run.AttackOwner == 0 ? "Ready" : run.View(run.AttackOwner).Phase.ToString();
                Label(new Rect(left + 12, 82, 244, 24), $"{phase}   Deflect {run.DeflectRemaining}/8", small, Ivory);
                Label(new Rect(left + 12, 105, 244, 24), "F2 Hide   F3 Fixed camera", small, Slate);
            }
            GUI.matrix = matrix;
        }

        public void DrawTrialSelection(CombatShowcase showcase, IReadOnlyList<TrialOpponent> opponents, TrialSession trials,
            IReadOnlyList<MapBossOption> bosses, string configurationError, string bossError, ref Vector2 scroll)
        {
            PrepareStyles();
            var matrix = BeginTrialOverlay(out float width, out float height);
            const float panelWidth = 620;
            float panelHeight = Mathf.Min(height - 64, 580);
            float left = (width - panelWidth) / 2;
            float top = (height - panelHeight) / 2;
            Fill(new Rect(left, top, panelWidth, panelHeight), Panel);
            Fill(new Rect(left, top, panelWidth, 2), Amber);
            Label(new Rect(left + 24, top + 12, 560, 56), "RAIN PASS", title, Ivory);
            bool expedition = GUI.Button(new Rect(left + 406, top + 25, 190, 32), "Play original Rain Pass");
            CombatMapId? route = null;
            for (int index = 0; index < CombatMap.Routes.Count; index++)
            {
                var map = CombatMap.Routes[index];
                if (GUI.Button(new Rect(left + 24 + index * 194, top + 72, 184, 41), map.Name)) route = map.Id;
            }
            const float rowHeight = 86;
            const float rowWidth = 552;
            const float headingHeight = 28;
            int bossCount = bosses?.Count ?? 0;
            float trialTop = headingHeight + rowHeight * bossCount;
            var viewport = new Rect(left + 24, top + 122, panelWidth - 48, panelHeight - 146);
            scroll = GUI.BeginScrollView(viewport, scroll,
                new Rect(0, 0, rowWidth, trialTop + headingHeight + rowHeight * (opponents?.Count ?? 0)));
            Label(new Rect(0, 0, rowWidth, headingHeight), bossError ?? "Route boss challenges", small, bossError == null ? Slate : Red);
            int? bossId = null;
            for (int index = 0; index < bossCount; index++)
            {
                var option = bosses[index];
                var map = option.Route;
                float rowTop = headingHeight + index * rowHeight;
                Fill(new Rect(0, rowTop, rowWidth, rowHeight - 8), new Color(.065f, .10f, .13f));
                Label(new Rect(12, rowTop + 7, 395, 25), map.Boss.Name, middle, Ivory);
                Label(new Rect(432, rowTop + 7, 106, 25), map.Name, small, Amber);
                GUI.Label(new Rect(12, rowTop + 34, 410, 39), map.BossAdvice, wrappedSmall);
                if (GUI.Button(new Rect(432, rowTop + 37, 106, 30), "Enter route")) bossId = option.Id;
            }
            string prompt = configurationError ?? (opponents == null ? "Loading opponents and records..."
                : trials.RecordsUnavailable ? "Duel trials   /   Records unavailable" : "Duel trials   /   Personal bests");
            Label(new Rect(0, trialTop, rowWidth, headingHeight), prompt, small, configurationError == null ? Slate : Red);
            int? selected = null;
            if (opponents != null)
            {
                for (int index = 0; index < opponents.Count; index++)
                {
                    var opponent = opponents[index];
                    float rowTop = trialTop + headingHeight + index * rowHeight;
                    Fill(new Rect(0, rowTop, rowWidth, rowHeight - 8), new Color(.065f, .10f, .13f));
                    Label(new Rect(12, rowTop + 7, 325, 25), opponent.Name, middle, Ivory);
                    Label(new Rect(354, rowTop + 7, 184, 25), $"Best   {FormatTime(trials.BestTicks(opponent.Id))}", small, Amber);
                    GUI.Label(new Rect(12, rowTop + 34, 410, 39), opponent.Description, wrappedSmall);
                    if (GUI.Button(new Rect(432, rowTop + 37, 106, 30), "Challenge")) selected = opponent.Id;
                }
            }
            GUI.EndScrollView();
            GUI.matrix = matrix;
            if (selected.HasValue) showcase.StartTrial(selected.Value);
            if (expedition) showcase.RestartRun();
            if (route.HasValue) showcase.StartMap(route.Value);
            if (bossId.HasValue) showcase.StartMapBoss(bossId.Value);
        }

        public void DrawTrialResult(CombatShowcase showcase, TrialSession trials)
        {
            var matrix = BeginTrialOverlay(out float width, out float height);
            const float panelWidth = 528;
            const float panelHeight = 244;
            float left = (width - panelWidth) / 2;
            float top = (height - panelHeight) / 2;
            Fill(new Rect(left, top, panelWidth, panelHeight), Panel);
            Fill(new Rect(left, top, panelWidth, 2), Amber);
            bool paused = trials.Status == RunStatus.Paused;
            bool victory = trials.Status == RunStatus.Victory;
            string heading = paused ? "TRIAL PAUSED" : victory ? "TRIAL CLEARED" : "FALLEN";
            Label(new Rect(left, top + 12, panelWidth, 55), heading, headline, Ivory);
            Label(new Rect(left, top + 66, panelWidth, 29), trials.Opponent.Name, centered, Slate);
            string best = FormatTime(trials.BestTicks(trials.Opponent.Id));
            Label(new Rect(left, top + 100, panelWidth, 30), $"Time   {FormatTime(trials.ElapsedTicks)}      Best   {best}", centered, Ivory);
            string status = trials.IsSaving ? "Saving record..." : trials.SaveFailed ? "Record could not be saved."
                : trials.RecordsUnavailable ? "Records unavailable for this session." : trials.NewRecord ? "NEW BEST" : "";
            Label(new Rect(left, top + 135, panelWidth, 28), status, centered, trials.SaveFailed ? Red : Amber);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !trials.IsSaving;
            bool retry = GUI.Button(new Rect(left + 24, top + 187, 142, 34), "Retry  [R]");
            bool choose = GUI.Button(new Rect(left + 175, top + 187, 174, 34), "Choose opponent");
            bool resume = paused && GUI.Button(new Rect(left + 358, top + 187, 146, 34), "Resume  [Esc]");
            bool save = trials.SaveFailed && GUI.Button(new Rect(left + 358, top + 187, 146, 34), "Retry saving");
            GUI.enabled = enabled;
            GUI.matrix = matrix;
            if (retry) showcase.RetryEncounter();
            if (choose) showcase.ShowTrialSelection();
            if (resume) showcase.Run.TogglePause();
            if (save) showcase.RetryTrialSave();
        }

        private void PrepareStyles()
        {
            if (title != null) return;
            title = Style(37, FontStyle.Bold, TextAnchor.MiddleLeft);
            small = Style(14, FontStyle.Normal, TextAnchor.MiddleLeft);
            wrappedSmall = new GUIStyle(small) { wordWrap = true };
            middle = Style(19, FontStyle.Bold, TextAnchor.MiddleLeft);
            centered = Style(19, FontStyle.Bold, TextAnchor.MiddleCenter);
            headline = Style(43, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private static Matrix4x4 BeginTrialOverlay(out float width, out float height)
        {
            var previous = GUI.matrix;
            float scale = Mathf.Min(1.4f, Mathf.Min(Screen.height / 690f, Screen.width / 730f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            width = Screen.width / scale;
            height = Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(.012f, .029f, .038f, .70f));
            return previous;
        }

        private static string FormatTime(int? ticks)
        {
            if (!ticks.HasValue) return "--:--.--";
            long hundredths = (long)ticks.Value * 100 / CombatRun.StepsPerSecond;
            return $"{hundredths / 6000:00}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
        }

        private static string Objective(CombatRun run)
        {
            if (run.Status == RunStatus.Proceed) return run.Map == null ? "Gate open. Follow the lantern path." : "Gate open. Enter the boss arena.";
            if (run.Map != null) return run.Encounter == Encounter.Courtyard ? run.Map.Advice : run.Map.BossAdvice;
            if (run.Encounter == Encounter.Practice) return "Meet the blade as it comes down.";
            if (run.Encounter == Encounter.Courtyard) return "Read the fighter who steps forward.";
            return "Watch for the delay and second strike.";
        }

        private static GUIStyle Style(int size, FontStyle weight, TextAnchor align) => new GUIStyle(GUI.skin.label)
        {
fontSize = size, fontStyle = weight, alignment = align, clipping = TextClipping.Clip, wordWrap = false
        };

        private static void Label(Rect rectangle, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            GUI.Label(rectangle, text, style);
        }

        private static void Bar(Rect rectangle, float ratio, Color color)
        {
            Fill(rectangle, new Color(.085f, .11f, .13f, .94f));
            rectangle.width *= Mathf.Clamp01(ratio);
            Fill(rectangle, color);
        }

        private static void Fill(Rect rectangle, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rectangle, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
