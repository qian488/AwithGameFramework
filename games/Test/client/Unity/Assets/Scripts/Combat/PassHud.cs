using Test.Application.Combat;
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
        private GUIStyle title, small, middle, centered, headline;
        private float smoothed;

        public void Draw(CombatRun run, PassSpace space, Camera lens, bool diagnostics, int seed, string digest, string cue, Color cueColor)
        {
            if (title == null)
            {
                title = Style(37, FontStyle.Bold, TextAnchor.MiddleLeft);
                small = Style(14, FontStyle.Normal, TextAnchor.MiddleLeft);
                middle = Style(19, FontStyle.Bold, TextAnchor.MiddleLeft);
                centered = Style(19, FontStyle.Bold, TextAnchor.MiddleCenter);
                headline = Style(43, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            var matrix = GUI.matrix;
            float scale = Mathf.Clamp(Mathf.Min(Screen.height / 810f, Screen.width / 870f), .4f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            Fill(new Rect(29, 28, 329, 110), Panel);
            Fill(new Rect(29, 28, 3, 110), Amber);
            Label(new Rect(47, 34, 280, 49), "RAIN PASS", title, Ivory);
            string area = run.Encounter == Encounter.Practice ? "PRACTICE TERRACE" : run.Encounter == Encounter.Courtyard ? "LANTERN COURTYARD" : "WARDEN'S GATE";
            Label(new Rect(48, 83, 290, 22), area, middle, Amber);
            Label(new Rect(48, 109, 290, 20), Objective(run), small, Slate);

            var player = run.View(0);
            float bottom = height - 103;
            Fill(new Rect(29, bottom, 278, 72), Panel);
            Label(new Rect(44, bottom + 6, 70, 20), "VITALITY", small, Slate);
            Bar(new Rect(112, bottom + 11, 176, 11), (float)player.Vitality.Health / player.HealthLimit, Teal);
            Label(new Rect(44, bottom + 31, 74, 20), "POSTURE", small, Slate);
            Bar(new Rect(120, bottom + 36, 168, 10), (float)player.Vitality.Posture / player.PostureLimit, player.Broken ? Red : Amber);
            Label(new Rect(44, bottom + 51, 242, 17), player.Broken ? "Guard broken" : run.Locked > 0 ? "Target locked" : "TAB   Lock target", small, player.Broken ? Red : Slate);

            if (run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed)
            {
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
                if (run.CanFinish) Label(new Rect(width / 2 - 182, height * .71f, 364, 38), "E   FINISH", centered, Amber);
                float left = Mathf.Max(29, width - 335);
                if (left > 320)
                {
                    Fill(new Rect(left, bottom, 304, 72), Panel);
                    Label(new Rect(left + 14, bottom + 4, 286, 24), "LMB   Strike       RMB   Deflect / Guard", small, Ivory);
                    Label(new Rect(left + 14, bottom + 29, 286, 19), "WASD  Move   SPACE  Dodge   ESC  Pause", small, Slate);
                    Label(new Rect(left + 14, bottom + 48, 286, 18), "F2  Debug   F3  Fixed camera", small, Slate);
                }
            }
            else
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

        private static string Objective(CombatRun run)
        {
            if (run.Status == RunStatus.Proceed) return "Gate open. Follow the lantern path.";
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
