using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Scenes;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Test.Client.Combat
{
    public sealed partial class RainPassCamp
    {
        private ContractReplayStore replays;
        private ContractReplayPanel replayPanel;
        private ViewHandle replayView;
        private Task replayOperation = Task.CompletedTask;
        private IReadOnlyList<string> replayVersions = Array.Empty<string>();
        private DownloadableContractOffer replayOffer;
        private string replaySaveIssue;
        private ContractReplay activeReplay;
        private int replaySelection, replayTick;
        private float replayClock;
        private bool replayPaused, replaySlow, replayReplacedResult;

        internal ContractReplay ActiveReplay => activeReplay;
        internal int ReplayTick => replayTick;
        internal bool ReplayPaused => replayPaused;
        internal bool ReplaySlow => replaySlow;

        private void OpenReplay()
        {
            var offer = SelectedOffer;
            if (offer == null || replayView != null || !operation.IsCompleted || !replayOperation.IsCompleted ||
                !contentOperation.IsCompleted || leaderboardView != null || settingsView != null) return;
            replayOperation = OpenReplayAsync(offer);
        }

        private async Task OpenReplayAsync(DownloadableContractOffer offer, ContractReplayStore store = null)
        {
            try
            {
                replayOffer = offer;
                replayVersions = (store ?? replays).Versions(offer.Id);
                int selected = replayVersions.ToList().IndexOf(activeContract?.Snapshot.Manifest.Version ?? InstalledVersion(offer.Id));
                replaySelection = Math.Max(selected, 0);
                replayView = await views.OpenAsync(new ViewRequest(ContractReplayPanel.Key, layer: 12, modal: true));
                replayPanel.Bind(() => SelectReplay(-1), () => SelectReplay(1), () => WatchReplay(store), CloseReplay,
                    ToggleReplayPause, () => replaySlow = !replaySlow,
                    () => JumpReplay(ContactResult.Deflect), () => JumpReplay(ContactResult.Finish));
                ShowReplayList();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass replay list failed: {exception}", this);
                if (replayView != null) await CloseReplayAsync();
                error = "Replay list could not be opened.";
            }
        }

        private void SelectReplay(int step)
        {
            if (!replayOperation.IsCompleted || replayVersions.Count < 2) return;
            replaySelection = (replaySelection + step + replayVersions.Count) % replayVersions.Count;
            ShowReplayList();
        }

        private void ShowReplayList(string message = null, bool busy = false, bool unavailable = false)
        {
            bool available = replayVersions.Count != 0;
            string selection = available ? "Recorded version " + replayVersions[replaySelection] : "No completed replay yet.";
            replayPanel.List(Translate(replayOffer.Title), message ?? selection, available && !busy && !unavailable,
                !busy && replayVersions.Count > 1);
        }

        private void WatchReplay(ContractReplayStore store)
        {
            if (replayView == null || activeReplay != null || !replayOperation.IsCompleted || replayVersions.Count == 0) return;
            replayOperation = WatchReplayAsync(replayOffer.Id, replayVersions[replaySelection], store);
        }

        private async Task WatchReplayAsync(string id, string version, ContractReplayStore store = null)
        {
            bool loadingScene = false;
            ShowReplayList("Loading replay " + version + "…", busy: true);
            try
            {
                var replay = await (store ?? replays).LoadAsync(id, version);
                if (replay == null)
                {
                    ShowReplayList("Replay " + version + " is unavailable.", unavailable: true);
                    return;
                }
                bool builtIn = id == ContractSession.CedarId && replay.Fingerprint == null && store != null;
                var installed = builtIn ? null : await downloads.OpenInstalledAsync(id, replay.Fingerprint);
                if (!builtIn && (installed == null || installed.Snapshot.Manifest.Version != version ||
                    installed.ConfigurationHash != replay.ConfigurationHash))
                {
                    ShowReplayList("Replay needs installed content " + version + ".", unavailable: true);
                    return;
                }
                if (Phase == ContractPhase.Result)
                {
                    replayReplacedResult = true;
                    await ReleaseBattleAsync();
                }
                loadingScene = true;
                Material banner = null;
                if (builtIn) battle = await scenes.LoadAsync(BattleScenePath, AwithGameFrame.Extensions.Scenes.SceneLoadMode.Additive);
                else banner = await LoadContractSceneAsync(installed);
                AttachBattleShowcase();
                while (!cancelRequested && !showcase.CommissionReady && !showcase.CommissionUnavailable) await Task.Yield();
                if (cancelRequested) throw new OperationCanceledException("Replay host is closing.");
                bool started = builtIn
                    ? showcase.BossConfigurationHash == replay.ConfigurationHash && showcase.StartCommission()
                    : showcase.StartCommission(installed, banner);
                if (!started) throw new InvalidOperationException("Could not prepare recorded route.");
                showcase.ApplyPlayerSettings(settings.Snapshot);
                showcase.BeginReplay(replay);
                activeReplay = replay;
                replayTick = 0;
                replayClock = 0;
                replayPaused = replaySlow = false;
                panel.gameObject.SetActive(false);
                RenderReplayControls();
                if (UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(replayPanel.CloseButton.gameObject);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass replay {id}/{version}: {exception}", this);
                if (loadingScene) await ReleaseBattleAsync();
                ShowReplayList("Replay " + version + " could not be opened.");
            }
        }

        private async Task<Material> LoadContractSceneAsync(InstalledContract installed)
        {
            var snapshot = installed.Snapshot;
            var catalog = UnityBundleCatalog.FromJson(File.ReadAllText(snapshot.GetPath("bundle-catalog.json")));
            battleStore = new ResourceStore(new UnityAssetBundleLoader(catalog, snapshot.GetPath), Host.Dispatcher);
            battleAssets = new ResourceScope(battleStore);
            var banner = (await battleAssets.LoadAsync<Material>("contract-banner")).Value;
            var downloadedScenes = new SceneService(Host.Context, new UnityBundleSceneBackend(), battleStore);
            battle = await downloadedScenes.LoadAsync("contract-scene", SceneLoadMode.Additive);
            return banner;
        }

        private void AttachBattleShowcase()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                showcase = root.GetComponentInChildren<CombatShowcase>();
                if (showcase != null) break;
            }
            if (showcase == null) throw new InvalidOperationException("The combat scene has no Rain Pass host.");
            if (campCamera != null) campCamera.enabled = false;
        }

        private void UpdateReplay()
        {
            if (replayView == null || !replayOperation.IsCompleted) return;
            if (activeReplay != null)
            {
                if (!replayPaused)
                {
                    replayClock += Time.unscaledDeltaTime * CombatRun.StepsPerSecond * (replaySlow ? .5f : 1);
                    int next = Mathf.Min((int)replayClock, activeReplay.Frames.Count - 1);
                    if (next != replayTick) SeekReplay(next);
                    if (next == activeReplay.Frames.Count - 1) replayPaused = true;
                }
                RenderReplayControls();
            }
            RainPassGamepad.Navigate(replayPanel, CloseReplay);
        }

        private void ToggleReplayPause()
        {
            if (activeReplay == null) return;
            if (replayPaused && replayTick == activeReplay.Frames.Count - 1)
            {
                replayClock = 0;
                SeekReplay(0);
            }
            replayPaused = !replayPaused;
        }

        private void JumpReplay(ContactResult result)
        {
            if (activeReplay == null) return;
            int target = activeReplay.PreviousMarker(replayTick, result);
            if (target < 0) target = activeReplay.Markers.FirstOrDefault(marker => marker.Result == result)?.Tick ?? -1;
            if (target < 0) return;
            replayPaused = true;
            replayClock = target;
            SeekReplay(target);
        }

        private void SeekReplay(int tick)
        {
            replayTick = tick;
            showcase.ShowReplayFrame(tick);
        }

        private void RenderReplayControls() => replayPanel.Playing(activeReplay.ContentVersion, replayTick,
            activeReplay.Frames.Count - 1, replayPaused, replaySlow,
            activeReplay.Markers.Any(marker => marker.Result == ContactResult.Deflect),
            activeReplay.Markers.Any(marker => marker.Result == ContactResult.Finish));

        private void CloseReplay()
        {
            if (replayView != null && replayOperation.IsCompleted) replayOperation = CloseReplayAsync();
        }

        private async Task CloseReplayAsync()
        {
            try
            {
                if (activeReplay != null) await ReleaseBattleAsync();
                if (replayReplacedResult && session.TryReturn()) session.FinishReturn();
                await views.CloseAsync(replayView);
            }
            catch (Exception exception)
            {
                Report("Replay could not be closed.", exception);
                if (replayReplacedResult && Phase == ContractPhase.Result && session.TryReturn())
                    operation = ReturnAsync(error);
            }
            finally
            {
                replayView = null;
                replayPanel = null;
                activeReplay = null;
                replayReplacedResult = false;
                UpdatePanel();
            }
        }
    }
}
