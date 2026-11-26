using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed partial class ContractDownloadEvidence
    {
        private IEnumerator VerifyReplay()
        {
            yield return Until(() => Has("Next contract"), "published or cached offers");
            Click("Next contract");
            yield return null;
            Require(camp.SelectedOffer?.Id == "salt-causeway", "select Salt Causeway");
            string stage = RainPassCamp.Argument("-replay-stage");
            if (stage != "record")
            {
                Require(camp.InstalledVersion("salt-causeway") == "v2", "v2 remains active");
                yield return OpenReplay();
                if (stage == "missing")
                {
                    yield return Until(() => camp.GetComponentsInChildren<Text>().Any(text => text.text == "Replay needs installed content v1."),
                        "explicit missing v1 message");
                    Require(Find("Watch replay")?.IsInteractable() == false, "unavailable replay cannot be started");
                    Require(camp.ActiveReplay == null && camp.LoadedBattleCount == 0, "no scene loaded for removed replay content");
                    Click("Close replay");
                    yield return Until(() => Has("Accept contract"), "recover to camp");
                    Mark("missing-content-recovers-to-camp");
                    yield break;
                }
                yield return Until(() => camp.ActiveReplay != null, "offline playback");
                VerifyReplayControls();
                Require(camp.InstalledVersion("salt-causeway") == "v2", "opening old replay keeps active pointer");
                Click("Close replay");
                yield return Until(() => camp.ActiveReplay == null && camp.LoadedBattleCount == 0, "release old resources");
                Mark("offline-v1-with-v2-active");
                yield break;
            }

            Click("Download");
            yield return Until(() => Has("Accept contract"), "install v1");
            Click("Accept contract");
            yield return Until(() => camp.Phase == ContractPhase.Battle, "enter v1");
            var battle = camp.ActiveShowcase;
            var v1 = battle.CommissionConfigHash;
            string banner = battle.CommissionMaterial.name;
            var original = new List<(int Tick, ContactResult Result)>();
            battle.Run.Cue += cue =>
            {
                if (cue.Result == ContactResult.Deflect || cue.Result == ContactResult.Finish)
                    original.Add((battle.CommissionTicks, cue.Result));
            };
            var driver = battle.gameObject.AddComponent<PassEvidence>();
            for (int tick = 0; tick < 12000 && (battle.Run.Status == RunStatus.Fighting || battle.Run.Status == RunStatus.Proceed); tick++)
            {
                driver.DriveContract(battle, true);
                if (tick % 60 == 0) yield return null;
            }
            Require(battle.Run.Status == RunStatus.Victory, "played v1 through victory");
            int end = battle.CommissionTicks;
            var savedPosition = battle.Space.Position(0);
            yield return Until(() => Has("Return to camp") && Has("Replays"), "saved result and replay");
            yield return OpenReplay();
            yield return Until(() => camp.ActiveReplay != null, "replay from settlement");
            VerifyReplayControls();
            var replay = camp.ActiveReplay;
            Require(replay.Frames.Count - 1 == end && replay.Frames[end].Actors[0].Position == savedPosition &&
                replay.Markers.Select(marker => (marker.Tick, marker.Result)).SequenceEqual(original), "actual cues, final position and completion tick match");
            Require(camp.ActiveShowcase.CommissionConfigHash == v1 && camp.ActiveShowcase.CommissionMaterial.name == banner, "same installed configuration and banner");
            File.WriteAllText(Path.Combine(output, "fingerprint.txt"), replay.Fingerprint);
            File.WriteAllText(Path.Combine(output, "banner.txt"), banner);
            File.WriteAllText(Path.Combine(output, "configuration.txt"), v1);
            Click("Close replay");
            yield return Until(() => camp.Phase == ContractPhase.Camp && Has("Accept contract"), "close scene and return to camp");
            Mark("settlement-replay-matches-played-ticks");
            File.WriteAllText(Path.Combine(output, "publish-v2"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "v2-available")), "publisher v2");
            yield return Wait(camp.RefreshOffers());
            yield return Wait(camp.InstallSelected());
            Require(camp.InstalledVersion("salt-causeway") == "v2", "activated v2 after recording v1");
            Mark("installed-v2");
        }

        private IEnumerator OpenReplay()
        {
            Click("Replays");
            yield return Until(() => Find("Watch replay")?.IsInteractable() == true, "replay version selection");
            Click("Watch replay");
        }

        private void VerifyReplayControls()
        {
            var replay = camp.ActiveReplay;
            Require(replay.ContentVersion == "v1" && replay.ConfigurationHash == camp.ActiveShowcase.CommissionConfigHash,
                "old configuration matches the recorded version");
            var source = Path.Combine(Path.GetDirectoryName(output), "Record");
            if (Directory.Exists(source) && source != output && File.Exists(Path.Combine(source, "banner.txt")))
            {
                Require(camp.ActiveShowcase.CommissionMaterial.name == File.ReadAllText(Path.Combine(source, "banner.txt")) &&
                    replay.ConfigurationHash == File.ReadAllText(Path.Combine(source, "configuration.txt")), "offline playback uses v1 banner and configuration");
            }
            Click("Last deflect");
            Require(camp.ReplayPaused && replay.Markers.Any(marker => marker.Result == ContactResult.Deflect && marker.Tick == camp.ReplayTick), "deflect marker pauses at recorded tick");
            Click("Last execution");
            Require(replay.Markers.Any(marker => marker.Result == ContactResult.Finish && marker.Tick == camp.ReplayTick), "execution marker seeks to recorded tick");
            Click("1x");
            Require(camp.ReplaySlow, "0.5x control");
            Require(camp.ActiveShowcase.Space.Position(0) == replay.Frames[camp.ReplayTick].Actors[0].Position,
                "visual actor uses captured position");
            Mark("marker-jumps-and-half-speed");
        }
    }
}
