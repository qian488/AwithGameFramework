using System;
using System.Collections;
using System.IO;
using System.Linq;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class ExpeditionEvidence : MonoBehaviour
    {
        private RainPassCamp camp;
        private string output;

        internal void Run(RainPassCamp value)
        {
            camp = value;
            output = RainPassCamp.Argument("-expedition-evidence");
            StartCoroutine(RunWithResult());
        }

        private IEnumerator RunWithResult()
        {
            var steps = new System.Collections.Generic.Stack<IEnumerator>();
            steps.Push(Verify());
            while (steps.Count != 0)
            {
                object next;
                try
                {
                    var current = steps.Peek();
                    if (!current.MoveNext()) { steps.Pop(); continue; }
                    next = current.Current;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString());
                    UnityEngine.Application.Quit(1);
                    yield break;
                }
                if (next is IEnumerator routine) steps.Push(routine);
                else yield return next;
            }
            File.WriteAllText(Path.Combine(output, "passed.txt"), UnityEngine.Application.unityVersion);
            UnityEngine.Application.Quit();
        }

        private IEnumerator Verify()
        {
            var stage = RainPassCamp.Argument("-expedition-stage");
            yield return Until(() => Find("Expedition") != null, "camp expedition entrance");
            if (stage == "play")
            {
                Click("Expedition");
                yield return Until(() => Find("Install Salt Causeway") != null, "published v1 route");
                Click("Install Salt Causeway");
                yield return Until(() => Find("Start Salt Causeway") != null, "finish installing route");
                Click("Start Salt Causeway");
                yield return Until(() => camp.Phase == ContractPhase.Battle && camp.ExpeditionLeg == 1, "expedition first leg");
                Require(camp.ActiveContractVersion == "v1", "first leg uses v1");
                Require(camp.ActiveShowcase.Run.ArrowsRemaining == 6, "standard kit took effect");
                yield return WinBattle();
                yield return Until(() => camp.Expedition.Checkpoint != null && Find("Recover posture") != null, "save rest checkpoint");
                Require(camp.Expedition.Checkpoint.Version == "v1" && camp.Expedition.Marks == 0, "v1 checkpoint and no award before final");
                Click("Recover posture");
                yield return Until(() => camp.Expedition.Checkpoint.Supply == "posture", "persist supply choice");
                Click("Close expedition");
                yield return Until(() => Find("Next contract") != null, "return to camp before update");
                Click("Next contract");
                File.WriteAllText(Path.Combine(output, "publish-v2"), "");
                yield return Until(() => File.Exists(Path.Combine(output, "v2-available")), "v2 available");
                var refresh = camp.RefreshOffers();
                yield return Until(() => refresh.IsCompleted, "refresh v2");
                yield return Until(() => camp.SelectedOffer?.Id == "salt-causeway", "salt causeway remains selected");
                var install = camp.InstallSelected();
                yield return Until(() => install.IsCompleted, "install v2");
                Require(camp.InstalledVersion("salt-causeway") == "v2", "activate v2 before offline restart");
                Mark("v1-first-leg-checkpoint-v2-installed");
                yield break;
            }
            Click("Expedition");
            if (stage == "resume")
            {
                yield return Until(() => Find("Continue to Cedar Grove") != null, "restore saved checkpoint");
                Require(camp.Expedition.Checkpoint.Version == "v1" && camp.InstalledVersion("salt-causeway") == "v2" &&
                    camp.Expedition.Checkpoint.Supply == "posture", "restore route version and selected supplies");
                Click("Continue to Cedar Grove");
                yield return Until(() => camp.Phase == ContractPhase.Battle && camp.ExpeditionLeg == 2, "enter Cedar Grove offline");
                Require(camp.ActiveShowcase.Run.View(0).Vitality.Posture == 0, "selected posture supply took effect");
                yield return WinBattle();
                yield return Until(() => camp.Expedition.Latest != null && Find("Unlock guard") != null, "victory and award saved");
                Require(camp.Expedition.Latest.Pending && camp.Expedition.Marks == 1 &&
                    camp.Expedition.Checkpoint == null && camp.Expedition.Latest.Checkpoint.Version == "v1", "offline reward and pending score");
                Click("Unlock guard");
                yield return Until(() => camp.Expedition.Gear == "guard" && camp.Expedition.Marks == 0, "unlock kit and spend only once");
                yield return Until(() => Find("Replay route v1") != null, "saved first leg recording");
                Click("Replay route v1");
                yield return Until(() => camp.ActiveReplay?.ContentVersion == "v1", "load v1 recording while v2 is active");
                Require(camp.InstalledVersion("salt-causeway") == "v2", "replay preserves active pointer");
                Click("Close replay");
                yield return Until(() => Find("Expedition") != null && camp.ActiveReplay == null, "leave first replay");
                Click("Expedition");
                yield return Until(() => Find("Replay Cedar Grove") != null, "recorded final leg");
                Click("Replay Cedar Grove");
                yield return Until(() => camp.ActiveReplay?.ContractId == ContractSession.CedarId, "play recorded final leg");
                Mark("offline-final-award-unlock-replay-v1");
                yield break;
            }
            yield return Until(() => camp.Expedition.Latest?.Pending == false && Find("Expedition leaderboard") != null, "confirmed pending score");
            Require(camp.Expedition.Gear == "guard" && camp.Expedition.Marks == 0, "kit and balance restored");
            Click("Expedition leaderboard");
            yield return Until(() => camp.GetComponentsInChildren<Text>().Any(label => label.text.Contains("Your rank 1")), "rank in original expedition group");
            Mark("synced-offline-score-and-ranked-original-group");
        }

        private IEnumerator WinBattle()
        {
            var battle = camp.ActiveShowcase;
            var driver = battle.gameObject.AddComponent<PassEvidence>();
            for (int tick = 0; tick < 12000 && battle != null && (battle.Run.Status == RunStatus.Fighting || battle.Run.Status == RunStatus.Proceed); tick++)
            {
                driver.DriveContract(battle, true);
                if (tick % 60 == 0) yield return null;
            }
            Require(battle == null || battle.Run.Status == RunStatus.Victory, "win the configured route");
        }

        private static IEnumerator Until(Func<bool> predicate, string description)
        {
            float deadline = Time.realtimeSinceStartup + 35;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(predicate(), description);
        }

        private Button Find(string label) => camp.GetComponentsInChildren<Button>().FirstOrDefault(button =>
            button.IsInteractable() && button.GetComponentInChildren<Text>()?.text == label);

        private void Click(string label)
        {
            var button = Find(label);
            Require(button != null, "button " + label);
            button.onClick.Invoke();
        }

        private void Mark(string step) => File.AppendAllText(Path.Combine(output, "steps.txt"), step + Environment.NewLine);

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Expedition check failed: " + description);
        }
    }
}
