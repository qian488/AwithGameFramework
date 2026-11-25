using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class PlayerSettingsEvidence : MonoBehaviour
    {
        private RainPassCamp camp;
        private string output;
        internal void Run(RainPassCamp owner)
        {
            camp = owner;
            output = RainPassCamp.Argument("-settings-evidence");
            UnityEngine.Application.runInBackground = true;
            StartCoroutine(Report(Verify()));
        }

        private IEnumerator Report(IEnumerator flow)
        {
            var steps = new Stack<IEnumerator>();
            steps.Push(flow);
            while (steps.Count > 0)
            {
                object current;
                try
                {
                    var step = steps.Peek();
                    if (!step.MoveNext()) { steps.Pop(); continue; }
                    current = step.Current;
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                    Debug.LogError(error);
                    UnityEngine.Application.Quit(1);
                    yield break;
                }
                if (current is IEnumerator child) steps.Push(child);
                else yield return current;
            }
            File.WriteAllText(Path.Combine(output, "passed.txt"), UnityEngine.Application.unityVersion);
            UnityEngine.Application.Quit();
        }

        private IEnumerator Verify()
        {
            yield return Until(() => Find("Settings") != null || Find("设置") != null, "camp settings button");
            bool reopen = RainPassCamp.Argument("-settings-reopen") != null;
            if (!reopen)
            {
                Click("Settings");
                yield return Until(() => camp.SettingsOpen, "settings open");
                var panel = camp.GetComponentInChildren<PlayerSettingsPanel>();
                Click("Esc");
                panel.CaptureControl("keyboard/w");
                Click("Apply and save");
                Require(HasText("This key is assigned to another action.") &&
                    camp.PlayerSettings.Snapshot.InputBindings.Single(binding => binding.Action == "pause").Control == "keyboard/escape",
                    "conflicting binding rejected");
                Mark("binding-conflict");
                Click("W");
                panel.CaptureControl("keyboard/p");
                Click("English");
                var sliders = panel.GetComponentsInChildren<Slider>();
                sliders[0].value = .25f;
                sliders[1].value = .70f;
                var settingsDirectory = Path.Combine(RainPassCamp.Argument("-contract-data"), "RainPassSettings");
                File.WriteAllText(settingsDirectory, "simulate a blocked settings directory");
                Click("Apply and save");
                yield return Until(() => camp.SettingsOperation.IsCompleted, "settings failed save");
                Require(camp.PlayerSettings.IsDirty && camp.PlayerSettings.Snapshot.Language == "zh-CN" &&
                    camp.PlayerSettings.LastSaveError != null && HasText("设置保存失败。更改已生效。请重试以便重开后恢复。"),
                    "failed save keeps settings active and offers retry");
                File.Delete(settingsDirectory);
                Click("重试保存");
                yield return Until(() => camp.SettingsOperation.IsCompleted && HasText("设置已保存。"), "successful save retry");
                Click("关闭");
                yield return Until(() => !camp.SettingsOpen, "settings close");
                Mark("save-failure-and-retry");
            }

            var settings = camp.PlayerSettings.Snapshot;
            Require(settings.Language == "zh-CN" && Mathf.Approximately(settings.BusVolumes["sfx"], .70f) &&
                Mathf.Approximately(settings.BusVolumes["music"], reopen ? .55f : .25f), "player preferences");
            if (reopen)
                yield return Until(() => camp.ContentOperation.IsCompleted &&
                    HasText("The feed is unavailable. Cached offers are shown."), "English fallback from offline catalog");
            yield return Enter("salt-causeway");
            var first = camp.ActiveShowcase;
            AssertPauseBinding(first);
            Frame(first, "keyboard/p");
            Require(first.Run.Status == RunStatus.Paused, "P pauses the downloaded route");
            Frame(first);
            yield return Until(() => Find("设置") != null, "pause settings button");
            Frame(first, "keyboard/w");
            Require(first.ForwardHeld, "press reaches gameplay before opening settings");
            Click("设置");
            yield return Until(() => camp.SettingsOpen, "paused settings");
            Frame(first, "keyboard/w");
            Require(!first.ForwardHeld, "modal blocks a held key and releases gameplay state");
            if (!reopen)
            {
                // Advance the existing audio sources with deterministic frame times in batch mode.
                float previous = first.Mix.Music;
                float effects = first.Mix.Effects;
                var settingsPanel = camp.GetComponentInChildren<PlayerSettingsPanel>();
                settingsPanel.GetComponentsInChildren<Slider>()[0].value = .55f;
                Click("应用并保存");
                yield return Until(() => camp.SettingsOperation.IsCompleted, "saved in battle");
                yield return Until(() => first.Mix.Music > previous + .01f, "live music level");
                Require(Mathf.Approximately(first.Mix.Effects, effects), "effects level stays independent");
            }
            Click("关闭");
            yield return Until(() => !camp.SettingsOpen, "dismiss modal");
            Frame(first, "keyboard/w");
            Require(!first.ForwardHeld, "same held key stays suppressed after dismissal");
            Frame(first);
            Frame(first, "keyboard/w");
            Require(first.ForwardHeld, "press after physical release reaches gameplay");
            float savedEffects = first.Mix.Effects;
            Frame(first);
            Mark("modal-input-and-live-audio");
            yield return Retreat();
            yield return Enter("ember-foundry");
            var second = camp.ActiveShowcase;
            AssertPauseBinding(second);
            Require(Mathf.Approximately(second.Mix.Effects, savedEffects), "same effects scaling");
            Frame(second, "keyboard/p");
            Require(second.Run.Status == RunStatus.Paused, "second route uses the same pause key");
            Frame(second);
            yield return Retreat();
            Mark(reopen ? "reopened-both-routes" : "shared-across-routes");
        }

        private IEnumerator Enter(string id)
        {
            for (int next = 0; next < 3 && camp.SelectedOffer?.Id != id; next++)
            {
                Click("下一个委托");
                yield return null;
            }
            Require(camp.SelectedOffer?.Id == id, "offer " + id);
            if (Find("下载") != null)
            {
                Click("下载");
                yield return Until(() => camp.ContentOperation.IsCompleted && Find("接取委托") != null, "installed " + id);
            }
            Click("接取委托");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "battle " + id);
        }

        private IEnumerator Retreat()
        {
            yield return Until(() => Find("撤回营地") != null, "retreat available");
            Click("撤回营地");
            yield return Until(() => camp.Phase == ContractPhase.Camp, "retreated");
            yield return null;
        }

        private static void AssertPauseBinding(CombatShowcase battle)
        {
            Require(battle.BattleInput.CaptureBindings().Single(binding => binding.Action == "pause").Control == "keyboard/p", "saved pause binding");
            Frame(battle, "keyboard/escape");
            Require(battle.Run.Status == RunStatus.Fighting, "Esc released as a battle binding");
            Frame(battle);
        }

        private static void Frame(CombatShowcase battle, string control = null)
        {
            battle.FeedEvidenceInput(control);
        }

        private Button Find(string label) => camp.GetComponentsInChildren<Button>().FirstOrDefault(
            button => button.IsInteractable() && button.GetComponentInChildren<Text>().text == label);
        private bool HasText(string label) => camp.GetComponentsInChildren<Text>().Any(item => item.text == label);
        private void Click(string label)
        {
            var button = Find(label);
            Require(button != null, "button " + label);
            button.onClick.Invoke();
        }

        private static IEnumerator Until(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), "Timed out waiting for " + description);
        }

        private void Mark(string step) => File.AppendAllText(Path.Combine(output, "steps.txt"), step + Environment.NewLine);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
