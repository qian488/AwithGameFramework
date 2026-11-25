using System;
using System.Linq;
using AwithGameFrame.Gameplay.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class PlayerSettingsPanel : MonoBehaviour
    {
        internal const string Key = "RainPassContract/Settings";
        [SerializeField] private Text title, languageLabel, musicLabel, effectsLabel, pauseLabel, feedback;
        [SerializeField] private Button language, pause, apply, close;
        [SerializeField] private Slider music, effects;
        private Func<string, string> text;
        private Font chineseFont;
        private UserSettingsData draft;
        private bool capture, busy;
        private string message;
        private bool offerRetry;

        internal UserSettingsData Draft => draft;

        internal void Bind(UserSettingsData snapshot, Func<string, string> translate, Font font, Action save, Action dismiss)
        {
            draft = snapshot;
            text = translate;
            chineseFont = font;
            music.SetValueWithoutNotify(draft.BusVolumes["music"]);
            effects.SetValueWithoutNotify(draft.BusVolumes["sfx"]);
            music.onValueChanged.AddListener(value => { draft.BusVolumes["music"] = value; Changed(); });
            effects.onValueChanged.AddListener(value => { draft.BusVolumes["sfx"] = value; Changed(); });
            language.onClick.AddListener(() =>
            {
                draft.Language = draft.Language == "en" ? "zh-CN" : "en";
                Changed();
            });
            pause.onClick.AddListener(() => { capture = true; Changed(); });
            apply.onClick.AddListener(() => { if (!capture && !busy) save(); });
            close.onClick.AddListener(() => { if (!busy) dismiss(); });
            Refresh();
        }

        private void Update()
        {
            if (!capture) return;
            for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++)
            {
                if (!Input.GetKeyDown(key)) continue;
                CaptureControl("keyboard/" + key.ToString().ToLowerInvariant());
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) CaptureControl("keyboard/escape");
            else if (Input.GetKeyDown(KeyCode.Space)) CaptureControl("keyboard/space");
        }

        internal void CaptureControl(string control)
        {
            if (!capture) return;
            capture = false;
            draft.InputBindings.Single(item => item.Action == "pause").Control = control;
            Changed();
        }

        internal void Report(string key, bool saving = false, bool retry = false)
        {
            message = key;
            busy = saving;
            offerRetry = retry;
            Refresh();
        }

        private void Changed()
        {
            message = null;
            offerRetry = false;
            Refresh();
        }

        private void Refresh()
        {
            Write(title, "Settings");
            Write(languageLabel, "Language");
            Write(musicLabel, "Music", "  " + Mathf.RoundToInt(music.value * 100) + "%");
            Write(effectsLabel, "Sound effects", "  " + Mathf.RoundToInt(effects.value * 100) + "%");
            Write(pauseLabel, "Pause key");
            Write(language.GetComponentInChildren<Text>(), draft.Language == "en" ? "English" : "简体中文");
            var key = draft.InputBindings.Single(item => item.Action == "pause").Control;
            Write(pause.GetComponentInChildren<Text>(), capture ? "Press a letter, Esc or Space" : KeyLabel(key));
            Write(apply.GetComponentInChildren<Text>(), offerRetry ? "Retry save" : "Apply and save");
            Write(close.GetComponentInChildren<Text>(), "Close");
            Write(feedback, message ?? "");
            foreach (var control in new Selectable[] { music, effects, language, pause, apply, close })
                control.interactable = !busy && (!capture || control == close);
            if (capture && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        internal static string KeyLabel(string control)
        {
            string key = control.Substring("keyboard/".Length);
            if (key == "escape") return "Esc";
            if (key == "space") return "Space";
            return key.ToUpperInvariant();
        }

        private void Write(Text label, string key, string suffix = "")
        {
            label.font = chineseFont;
            label.text = text(key) + suffix;
        }
    }
}
