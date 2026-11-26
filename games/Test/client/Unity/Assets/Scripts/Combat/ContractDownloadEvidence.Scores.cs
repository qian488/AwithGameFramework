using System;
using System.Collections;
using System.IO;
using System.Linq;
using AwithGameFrame.Foundation.DataPersistence;
using Test.Application.Combat;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed partial class ContractDownloadEvidence
    {
        private IEnumerator VerifyScores()
        {
            string stage = RainPassCamp.Argument("-scores-stage");
            yield return Until(() => Has("Next contract"), "published score-test offers");
            if (stage == "reopen")
            {
                Require(camp.ScoreCursor == 2 && camp.PendingScoreCount == 1, "offline submission reopens on the last durable cursor");
                Require(Shows("Pending scores: 1."), "camp shows the pending score count");
                File.WriteAllText(Path.Combine(output, "pending-visible"), "");
                yield return Until(() => camp.ScoreCursor == 3 && camp.PendingScoreCount == 0, "confirmation recovered");
                Require(!Shows("Pending scores:"), "settled score leaves the camp queue");
                Mark("lost-receipt-recovered");
                yield break;
            }
            if (stage == "confirmed")
            {
                Require(camp.ScoreCursor == 3 && camp.PendingScoreCount == 0, "confirmed cursor survives client and server restart");
                Require(camp.BestTicks("salt-causeway", "v1") > 0 && camp.BestTicks("salt-causeway", "v2") > 0 &&
                    camp.BestTicks("ember-foundry", "v1") > 0, "three local score records survive restart");
                Mark("reopened-confirmed-scores");
                yield break;
            }

            Click("Next contract");
            yield return null;
            Require(camp.SelectedOffer?.Id == "salt-causeway", "salt score contract");
            Click("Download");
            yield return Wait(camp.ContentOperation);
            yield return PlayScore("v1", Confirmed(1));
            File.WriteAllText(Path.Combine(output, "publish-v2"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "v2-available")), "second score version announced");
            yield return Wait(camp.RefreshOffers());
            yield return Wait(camp.InstallSelected());
            yield return PlayScore("v2", Confirmed(2));
            Mark("online-score-versions-confirmed");

            File.WriteAllText(Path.Combine(output, "go-offline"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "server-stopped")), "score service offline");
            Click("Next contract");
            yield return null;
            Require(camp.SelectedOffer?.Id == "ember-foundry", "ember score contract");
            Click("Download");
            yield return Wait(camp.ContentOperation);
            yield return PlayScore("v1", CaptureOfflineScore());
            Require(Shows("Pending scores: 1."), "camp shows offline score after returning");
            Mark("offline-score-persisted");
        }

        private IEnumerator PlayScore(string version, IEnumerator verifyScore)
        {
            yield return Until(() => Has("Accept contract"), "installed score contract ready");
            Click("Accept contract");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "enter score contract");
            Require(camp.ActiveContractVersion == version, "played content version " + version);
            yield return Finish(camp.ActiveShowcase, verifyScore);
        }

        private IEnumerator Confirmed(long sequence)
        {
            yield return Until(() => camp.ScoreCursor == sequence && Shows("Score synced."), "online score confirmation " + sequence);
            Require(camp.PendingScoreCount == 0, "confirmed score left the local queue");
        }

        private IEnumerator CaptureOfflineScore()
        {
            yield return Until(() => Shows("Saved locally. Score pending."), "offline score status");
            Require(camp.ScoreCursor == 2 && camp.PendingScoreCount == 1 &&
                camp.PendingScore.ContractId == "ember-foundry" && camp.PendingScore.ContentVersion == "v1", "durable offline score and cursor");
            var encoding = new JsonSerializer().SerializeAsync(camp.PendingScore);
            yield return Wait(encoding);
            File.WriteAllBytes(Path.Combine(output, "offline-score.json"), encoding.GetAwaiter().GetResult());
        }

        private bool Shows(string text) => camp.GetComponentsInChildren<Text>().Any(label => label.text.Contains(text));
    }
}
