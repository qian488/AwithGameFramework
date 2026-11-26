using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.Presentation;
using Test.Application.Combat;
using Test.Application.Expedition;
using Test.Client.Expedition;
using Test.Domain.Expedition;
using UnityEngine;

namespace Test.Client.Combat
{
    public sealed partial class RainPassCamp
    {
        private ExpeditionSession expedition;
        private ExpeditionCatalog expeditionCatalog;
        private ExpeditionOnline expeditionOnline;
        private ExpeditionPanel expeditionPanel;
        private ExpeditionFeedback expeditionFeedback;
        private ViewHandle expeditionView;
        private Task expeditionViewOperation = Task.CompletedTask;
        private string expeditionIssue;
        private string expeditionBoard;
        private float expeditionNextSync;

        internal ExpeditionSession Expedition => expedition;

        private void ConfigureExpedition(string root, string scoreUrl)
        {
            expedition = new ExpeditionSession(Path.Combine(root, "RainPassExpedition"),
                Path.Combine(root, "RainPassExpeditionReplays"));
            if (!string.IsNullOrWhiteSpace(scoreUrl)) expeditionOnline = new ExpeditionOnline(new Uri(scoreUrl));
        }

        private async Task InitializeExpeditionAsync()
        {
            var catalog = Resources.Load<TextAsset>("AwithConfiguration/Test/Expedition");
            if (catalog == null)
            {
                Debug.LogWarning("Rain Pass expedition configuration is missing. Sync the Test workspace.", this);
                return;
            }
            try
            {
                var configuration = await ExpeditionCatalog.LoadAsync(catalog.bytes);
                await expedition.OpenAsync();
                expeditionFeedback = ExpeditionFeedback.Create(contractCanvas, ExpeditionSfx(settings.Snapshot));
                expeditionCatalog = configuration;
            }
            catch (Exception exception) { Debug.LogWarning($"Rain Pass expedition initialization: {exception}", this); }
            finally { Resources.UnloadAsset(catalog); }
        }

        private bool BindExpeditionView(ViewRequest request, GameObject content)
        {
            if (request.Key != ExpeditionPanel.Key) return false;
            expeditionPanel = content.GetComponent<ExpeditionPanel>();
            if (expeditionPanel == null) throw new InvalidOperationException("Missing expedition panel component.");
            return true;
        }

        private void OpenExpedition()
        {
            if (Phase != ContractPhase.Camp || expeditionCatalog == null || expeditionView != null ||
                !expeditionViewOperation.IsCompleted || !operation.IsCompleted || !contentOperation.IsCompleted) return;
            expeditionViewOperation = OpenExpeditionAsync();
        }

        private async Task OpenExpeditionAsync()
        {
            expeditionView = await views.OpenAsync(new ViewRequest(ExpeditionPanel.Key, layer: 12, modal: true));
            RenderExpedition();
        }

        private void RenderExpedition()
        {
            if (expeditionPanel == null) return;
            var options = new List<(string Label, Action Execute)>();
            var checkpoint = expedition.Checkpoint;
            string status = $"Forging marks {expedition.Marks} · Equipment {expedition.Gear}";
            if (checkpoint != null)
            {
                status += $"\nCheckpoint {checkpoint.Route} {checkpoint.Version} · Supply {checkpoint.Supply}";
                options.Add(("Restock arrows", () => ChangeSupply(ExpeditionRules.ArrowsSupply)));
                options.Add(("Recover posture", () => ChangeSupply(ExpeditionRules.PostureSupply)));
                options.Add(("Continue to Cedar Grove", () => RunExpeditionAction(() => ContinueExpeditionAsync(checkpoint))));
                options.Add(("Abandon expedition", () => RunExpeditionAction(expedition.AbandonAsync)));
            }
            else
            {
                foreach (var route in expeditionCatalog.Routes)
                {
                    var offer = offers.FirstOrDefault(candidate => candidate.Id == route.Id);
                    if (offer == null) continue;
                    options.Add((installed.ContainsKey(route.Id) ? "Start " + offer.Title : "Install " + offer.Title,
                        () => RunExpeditionAction(() => PrepareExpeditionAsync(offer))));
                }
                foreach (var gear in expeditionCatalog.Equipment)
                {
                    if (gear.Id == expedition.Gear) continue;
                    string action = expedition.Owns(gear.Id) ? "Equip " :
                        expedition.Marks >= expeditionCatalog.UnlockCost ? "Unlock " : "Locked ";
                    var selected = gear.Id;
                    options.Add((action + selected, action == "Locked " ? null :
                        (Action)(() => RunExpeditionAction(() => expedition.SelectGearAsync(selected, expeditionCatalog.UnlockCost)))));
                }
                if (downloads == null)
                    options.Add(("Train in Cedar Grove", () => RunExpeditionAction(StartExpeditionTrainingAsync)));
            }
            if (expedition.Latest is ExpeditionResult result)
            {
                status += $"\nLast clear {result.Checkpoint.Version} · {Seconds(result.Checkpoint.FirstTicks)} + {Seconds(result.SecondTicks)} · Reward {result.Reward}";
                if (result.Pending) status += "\nScore pending";
                if (expeditionOnline != null)
                    options.Add(("Expedition leaderboard", () => RunExpeditionAction(() => ShowExpeditionBoardAsync(result))));
                options.Add(("Replay route " + result.Checkpoint.Version, () => RunExpeditionAction(() => OpenExpeditionReplayAsync(result, false))));
                options.Add(("Replay Cedar Grove", () => RunExpeditionAction(() => OpenExpeditionReplayAsync(result, true))));
            }
            if (expeditionIssue != null) status += "\n" + expeditionIssue;
            if (expeditionBoard != null) status += "\n" + expeditionBoard;
            options.Add(("Close expedition", CloseExpedition));
            expeditionPanel.Render("Rain Pass Expedition", status, options);
        }

        private static string Seconds(int ticks) => $"{ticks / (float)CombatRun.StepsPerSecond:0.0}s";

        private void RunExpeditionAction(Func<Task> execute)
        {
            if (expeditionViewOperation.IsCompleted && operation.IsCompleted)
                expeditionViewOperation = RunExpeditionActionAsync(execute);
        }

        private async Task RunExpeditionActionAsync(Func<Task> execute)
        {
            expeditionIssue = null;
            expeditionBoard = null;
            try { await execute(); }
            catch (Exception exception)
            {
                expeditionIssue = exception.Message;
                Debug.LogWarning($"Rain Pass expedition action: {exception}", this);
            }
            RenderExpedition();
        }

        private void CloseExpedition()
        {
            if (expeditionViewOperation.IsCompleted) expeditionViewOperation = CloseExpeditionAsync();
        }

        private async Task CloseExpeditionAsync()
        {
            if (expeditionView == null) return;
            await views.CloseAsync(expeditionView);
            expeditionView = null;
            expeditionPanel = null;
        }

        private async Task PrepareExpeditionAsync(DownloadableContractOffer offer)
        {
            if (!installed.TryGetValue(offer.Id, out var snapshot))
            {
                installed[offer.Id] = (await downloads.InstallAsync(offer, default)).Snapshot;
                return;
            }
            var battle = expedition.StartRoute(expeditionCatalog, offer.Id, snapshot.Manifest.Version, snapshot.Fingerprint);
            await EnterExpeditionBattleAsync(battle, offer);
        }

        private async Task StartExpeditionTrainingAsync()
        {
            await CloseExpeditionAsync();
            if (session.TryAccept()) operation = LoadAsync(null);
        }

        private async Task ContinueExpeditionAsync(ExpeditionCheckpoint checkpoint)
        {
            var retained = downloads == null ? null : await downloads.OpenInstalledAsync(checkpoint.Route, checkpoint.Fingerprint);
            var battle = expedition.Resume(expeditionCatalog, retained?.Snapshot.Fingerprint);
            await EnterExpeditionBattleAsync(battle, null);
        }

        private async Task EnterExpeditionBattleAsync(ExpeditionBattle battle, DownloadableContractOffer offer)
        {
            try
            {
                expeditionFeedback?.Stop();
                await CloseExpeditionAsync();
                if (!session.TryAccept(battle.ContractId, battle.ContractVersion))
                    throw new InvalidOperationException("Return to camp before entering an expedition.");
                // The camp owns cancellation and cleanup while Unity is loading.
                operation = LoadAsync(offer);
                await operation;
            }
            finally
            {
                if (Phase != ContractPhase.Battle) expedition.CancelBattle();
            }
        }

        private void ChangeSupply(string supply) => RunExpeditionAction(async () =>
        {
            await expedition.SelectSupplyAsync(supply);
            expeditionFeedback?.Play(ExpeditionFeedbackCue.Resupply);
        });

        private static float ExpeditionSfx(AwithGameFrame.Gameplay.Settings.UserSettingsData data) =>
            data.MasterMuted || data.BusMuted["sfx"] ? 0 : data.MasterVolume * data.BusVolumes["sfx"];

        private async Task FinishExpeditionLegAsync()
        {
            expeditionIssue = null;
            ExpeditionBattleOutcome outcome;
            try
            {
                var run = showcase.Run;
                outcome = await expedition.FinishBattleAsync(showcase.CommissionTicks, run.ArrowsRemaining,
                    run.View(0).Vitality.Posture, showcase.FinishReplay);
            }
            catch (Exception exception)
            {
                expeditionIssue = "Expedition could not be saved. Retry save.";
                Debug.LogWarning($"Rain Pass expedition result: {exception}", this);
                return;
            }
            if (session.TryReturn()) await ReturnAsync(null);
            expeditionFeedback?.Play(outcome == ExpeditionBattleOutcome.Completed ? ExpeditionFeedbackCue.Reward : ExpeditionFeedbackCue.CheckpointSaved);
            if (Phase == ContractPhase.Camp) await OpenExpeditionAsync();
        }

        private async Task ShowExpeditionBoardAsync(ExpeditionResult result)
        {
            var board = await expeditionOnline.ReadAsync(session.PlayerId, result.Checkpoint);
            expeditionBoard = board.OwnRank == 0 ? "No synced expedition score." :
                $"Your rank {board.OwnRank} · {board.OwnBestTicks / (float)CombatRun.StepsPerSecond:0.0}s";
            if (board.Entries != null)
                expeditionBoard += "\n" + string.Join(" · ", board.Entries.Take(3).Select(item =>
                    $"#{item.Rank} {item.TotalTicks / (float)CombatRun.StepsPerSecond:0.0}s"));
        }

        private async Task SyncExpeditionAsync(ExpeditionResult result)
        {
            try
            {
                await expeditionOnline.SubmitAsync(session.PlayerId, result);
                await expedition.ConfirmAsync(result.Checkpoint.Id);
                RenderExpedition();
            }
            catch (Exception exception) { Debug.LogWarning($"Rain Pass expedition score pending: {exception.Message}", this); }
        }

        private async Task OpenExpeditionReplayAsync(ExpeditionResult result, bool finalLeg)
        {
            var checkpoint = result.Checkpoint;
            var offer = finalLeg
                ? new DownloadableContractOffer { Id = ContractSession.CedarId, Title = "Cedar Grove" }
                : offers.FirstOrDefault(item => item.Id == checkpoint.Route);
            if (offer == null) throw new IOException("The expedition route catalog is unavailable.");
            var store = expedition.ReplaysFor(result);
            await CloseExpeditionAsync();
            await OpenReplayAsync(offer, store);
            if (replayView == null) return;
            int version = replayVersions.ToList().IndexOf(checkpoint.Version);
            if (version < 0)
            {
                ShowReplayList("The expedition recording is unavailable.", unavailable: true);
                return;
            }
            replaySelection = version;
            await WatchReplayAsync(offer.Id, checkpoint.Version, store);
        }
    }
}
