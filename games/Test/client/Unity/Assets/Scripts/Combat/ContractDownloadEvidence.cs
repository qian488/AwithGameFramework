using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Test.Application.Combat;
using System.Linq;

namespace Test.Client.Combat
{
    internal sealed class ContractDownloadEvidence : MonoBehaviour
    {
        private RainPassCamp camp;
        private string output;

        internal void Run(RainPassCamp value)
        {
            camp = value;
            output = RainPassCamp.Argument("-contract-evidence");
            UnityEngine.Application.runInBackground = true;
            StartCoroutine(Report(Verify()));
        }

        private IEnumerator Report(IEnumerator flow)
        {
            var steps = new Stack<IEnumerator>();
            steps.Push(flow);
            while (steps.Count != 0)
            {
                object current;
                try
                {
                    var step = steps.Peek();
                    if (!step.MoveNext())
                    {
                        steps.Pop();
                        continue;
                    }
                    current = step.Current;
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                    Debug.LogError(error);
                    UnityEngine.Application.Quit(1);
                    yield break;
                }
                if (current is IEnumerator nested) steps.Push(nested);
                else yield return current;
            }
            File.WriteAllText(Path.Combine(output, "passed.txt"), UnityEngine.Application.unityVersion + " " + UnityEngine.Application.platform);
            UnityEngine.Application.Quit();
        }

        private IEnumerator Verify()
        {
            bool reopened = RainPassCamp.Argument("-contract-offline") != null;
            Require(camp.HasCompleted(ContractSession.CedarId), "Legacy Cedar completion exists");
            Mark(reopened ? "legacy-record-reopened" : "legacy-record-migrated");
            Require(camp.WasMigrated != reopened, "Migration runs exactly once");
            yield return Until(() => Has("Next contract"), "published offers in the camp panel");
            Click("Next contract");
            yield return null;
            Require(camp.SelectedOffer?.Id == "salt-causeway", "Salt Causeway discovery");
            if (reopened)
            {
                Require(camp.InstalledVersion("salt-causeway") == "v1", "Rollback persisted across restart");
                Require(camp.BestTicks("salt-causeway", "v1") > 0 && camp.BestTicks("salt-causeway", "v2") > 0 &&
                    camp.BestTicks("ember-foundry", "v1") > 0, "Both contracts and versions retained after restart");
                Click("Accept contract");
                yield return Until(() => camp.Phase == ContractPhase.Battle, "offline entry");
                Require(camp.ActiveContractVersion == "v1", "Offline launch uses the active snapshot");
                camp.ActiveShowcase.Run.TogglePause();
                yield return null;
                Click("Retreat to camp");
                yield return Until(() => camp.Phase == ContractPhase.Camp, "offline retreat");
                Mark("offline-reopen");
                yield break;
            }

            Click("Download");
            yield return Wait(camp.ContentOperation);
            Require(camp.InstalledVersion("salt-causeway") == "v1", "First install");
            yield return null;
            Click("Accept contract");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "Salt Causeway v1 entry");
            var first = camp.ActiveShowcase;
            var oldMaterial = first.CommissionMaterial;
            string oldBanner = oldMaterial.name;
            string oldHash = first.CommissionConfigHash;
            Require(first.ActiveMapId == CombatMapId.SaltCauseway, "Salt Causeway route loaded");

            File.WriteAllText(Path.Combine(output, "publish-v2"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "v2-available")), "publisher v2");
            yield return Wait(camp.RefreshOffers());
            Require(camp.SelectedOffer.Version == "v2", "Publisher updated the available version");
            yield return Wait(camp.InstallSelected());
            Require(camp.InstalledVersion("salt-causeway") == "v1" && camp.ContentIssue != null, "Interrupted download leaves v1 active");
            yield return Wait(camp.InstallSelected());
            Require(camp.InstalledVersion("salt-causeway") == "v1" && camp.ContentIssue != null, "Corrupt download leaves v1 active");
            File.WriteAllText(Path.Combine(output, "restore-v2"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "v2-restored")), "publisher repair");
            yield return Wait(camp.InstallSelected());
            Require(camp.InstalledVersion("salt-causeway") == "v2" && camp.ActiveContractVersion == "v1" &&
                first.CommissionConfigHash == oldHash && oldMaterial != null, "In-flight battle pins v1");
            Mark("update-pins-active-battle");
            yield return Finish(first);
            Require(camp.BestTicks("salt-causeway", "v1") > 0, "Best time recorded under played v1");

            Click("Accept contract");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "Salt Causeway v2 entry");
            var second = camp.ActiveShowcase;
            Require(camp.ActiveContractVersion == "v2" && second.CommissionConfigHash != oldHash &&
                second.CommissionMaterial.name != oldBanner,
                $"Second release updates downloaded configuration and banner ({camp.ActiveContractVersion}, {second.CommissionConfigHash}, {second.CommissionMaterial.name})");
            yield return Finish(second);
            Require(camp.BestTicks("salt-causeway", "v2") > 0, "Independent v2 best time");
            yield return Wait(camp.RollbackSelected());
            Require(camp.InstalledVersion("salt-causeway") == "v1", "Rollback activates v1");
            Mark("update-and-rollback");

            Click("Next contract");
            yield return null;
            Require(camp.SelectedOffer?.Id == "ember-foundry", "Second contract discovered from published data");
            Click("Download");
            yield return Wait(camp.ContentOperation);
            Require(camp.InstalledVersion("ember-foundry") == "v1" && camp.InstalledVersion("salt-causeway") == "v1", "Independent installs");
            yield return null;
            Click("Accept contract");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "Ember Foundry entry");
            Require(camp.ActiveShowcase.ActiveMapId == CombatMapId.EmberFoundry, "Ember Foundry route");
            yield return Finish(camp.ActiveShowcase);
            Require(camp.BestTicks("ember-foundry", "v1") > 0, "Independent Ember record");
            Mark("second-contract-completed");
        }

        private IEnumerator Finish(CombatShowcase battle)
        {
            var driver = battle.gameObject.AddComponent<PassEvidence>();
            var material = battle.CommissionMaterial;
            var scene = battle.gameObject.scene;
            for (int tick = 0; tick < 12000 && (battle.Run.Status == RunStatus.Fighting || battle.Run.Status == RunStatus.Proceed); tick++)
            {
                driver.DriveContract(battle, true);
                if (tick % 60 == 0) yield return null;
            }
            Require(battle.Run.Status == RunStatus.Victory, $"Victory in {battle.ActiveMapId} at {battle.CurrentArea}");
            yield return Until(() => Has("Return to camp"), "save completion");
            Click("Return to camp");
            yield return Until(() => camp.Phase == ContractPhase.Camp, "release battle");
            Require(camp.LoadedBattleCount == 0 && !scene.isLoaded && material == null &&
                SceneManager.GetActiveScene() == camp.gameObject.scene, "Scene and material lease released");
            yield return null;
        }

        private static IEnumerator Wait(Task task) => Until(() => task.IsCompleted, "content operation");

        private static IEnumerator Until(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 45;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), "Timed out waiting for " + description);
        }

        private Button Find(string label) => camp.GetComponentsInChildren<Button>().FirstOrDefault(
            button => button.GetComponentInChildren<Text>().text == label);
        private bool Has(string label) => Find(label) != null;

        private void Click(string label)
        {
            var button = Find(label);
            Require(button != null && button.IsInteractable(), "Available button " + label);
            button.onClick.Invoke();
        }

        private void Mark(string step) => File.AppendAllText(Path.Combine(output, "steps.txt"), step + Environment.NewLine);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
