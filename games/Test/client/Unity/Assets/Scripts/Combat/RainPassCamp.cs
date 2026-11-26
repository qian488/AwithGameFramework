using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Gameplay.Settings;
using AwithGameFrame.Networking.Content;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Presentation;
using AwithGameFrame.Unity.Scenes;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Test.Client.Combat
{
    [DisallowMultipleComponent]
    public sealed class RainPassCamp : GameHostBehaviour
    {
        public const string ScenePath = "Assets/Scenes/RainPassCamp.unity";
        private ContractSession session;
        private SceneService scenes;
        private ViewService views;
        private ContractPanel panel;
        private LocalizationService language;
        private UserSettingsService settings;
        private PlayerSettingsPanel settingsPanel;
        private ViewHandle settingsView;
        private Task settingsOperation = Task.CompletedTask;
        private string pauseKey;
        private Font settingsFont;
        private SceneHandle battle;
        private CombatShowcase showcase;
        private Task operation = Task.CompletedTask;
        private bool cancelRequested;
        private string error;
        private DownloadableContracts downloads;
        private ContractScoreSync scores;
        private ContractLeaderboardService leaderboard;
        private LeaderboardPanel leaderboardPanel;
        private ViewHandle leaderboardView;
        private Task leaderboardOperation = Task.CompletedTask;
        private CancellationTokenSource leaderboardQuery;
        private Task scoreExchange = Task.CompletedTask;
        private float nextScoreExchange;
        private IReadOnlyList<DownloadableContractOffer> offers = Array.Empty<DownloadableContractOffer>();
        private readonly Dictionary<string, ContentSnapshot> installed = new Dictionary<string, ContentSnapshot>();
        private int selection;
        private Task contentOperation = Task.CompletedTask;
        private CancellationTokenSource contentCancellation;
        private string contentIssue;
        private InstalledContract activeContract;
        private ResourceScope battleAssets;
        private ResourceStore battleStore;
        [SerializeField] private string contractFeedUrl;
        [SerializeField] private string contractScoreUrl;
        [SerializeField] private string contractLeaderboardUrl;
        [SerializeField] private Camera campCamera;
        [SerializeField] private Canvas contractCanvas;

        public ContractPhase Phase => session?.Phase ?? ContractPhase.Opening;
        public bool Completed => session?.Completed == true;
        internal string SaveDirectory { get; set; }
        internal string BattleScenePath { get; set; } = CombatShowcase.ScenePath;
        internal int LoadedBattleCount => battle == null ? 0 : 1;
        internal DownloadableContractOffer SelectedOffer => selection == 0 ? null : offers[selection - 1];
        internal string ActiveContractVersion => activeContract?.Snapshot.Manifest.Version;
        internal CombatShowcase ActiveShowcase => showcase;
        internal UserSettingsService PlayerSettings => settings;
        internal Task SettingsOperation => settingsOperation;
        internal bool SettingsOpen => settingsView != null;
        internal Task ContentOperation => contentOperation;
        internal string ContentIssue => contentIssue;
        internal bool WasMigrated => session.WasMigrated;
        internal bool HasCompleted(string id) => session.HasCompleted(id);
        internal int? BestTicks(string id, string version) => session.BestTicks(id, version);
        internal int PendingScoreCount => session.PendingScoreCount;
        internal long ScoreCursor => session.ScoreCursor;
        internal Test.Application.Protocol.SubmitContractScoreRequest PendingScore => session.NextPendingScore;
        internal string InstalledVersion(string id) => installed.TryGetValue(id, out var snapshot) ? snapshot.Manifest.Version : null;
        internal void UsePresentation(Camera camera, Canvas canvas)
        {
            campCamera = camera;
            contractCanvas = canvas;
        }

        protected override void ConfigureHost(GameHost host)
        {
            if (campCamera == null || contractCanvas == null)
                throw new InvalidOperationException("Assign the camp camera and contract canvas in the scene.");
            campCamera.enabled = true;
            var dataRoot = Argument("-contract-data");
            var userRoot = dataRoot ?? UnityEngine.Application.persistentDataPath;
            var scoreUrl = Argument("-contract-scores") ?? contractScoreUrl;
            var leaderboardUrl = Argument("-contract-leaderboard") ?? contractLeaderboardUrl;
            if (string.IsNullOrWhiteSpace(leaderboardUrl)) leaderboardUrl = scoreUrl;
            var profileRoot = SaveDirectory == null ? userRoot : Path.GetDirectoryName(Path.GetFullPath(SaveDirectory));
            string playerId = ContractSession.LoadOrCreatePlayerId(Path.Combine(profileRoot, "RainPassProfile"));
            session = new ContractSession(SaveDirectory ?? Path.Combine(userRoot, "RainPassContract"), playerId);
            if (!string.IsNullOrWhiteSpace(scoreUrl))
                scores = new ContractScoreSync(new Uri(scoreUrl), session, host.Dispatcher);
            if (!string.IsNullOrWhiteSpace(leaderboardUrl))
            {
                leaderboard = new ContractLeaderboardService(new Uri(leaderboardUrl),
                    session.PlayerId, Path.Combine(profileRoot, "RainPassLeaderboard"));
            }
            var feed = Argument("-contract-feed") ?? contractFeedUrl;
            if (!string.IsNullOrWhiteSpace(feed))
                downloads = new DownloadableContracts(new Uri(feed), Path.Combine(userRoot, "ContractDownloads"));
            var resources = new ResourceStore(new UnityResourceLoader(), host.Dispatcher);
            language = new LocalizationService(host.Context, "en");
            RainPassPreferences.LoadLanguages(language);
            var saves = new VersionedSaveService<UserSettingsData>(Path.Combine(profileRoot, "RainPassSettings"), new JsonSerializer(), 1);
            settings = new UserSettingsService(host.Context, new VersionedUserSettingsStore(saves), RainPassPreferences.Defaults(), language);
            settingsFont = Resources.Load<Font>("RainPassContract/RainPassUI");
            if (settingsFont == null) throw new InvalidOperationException("Missing Rain Pass UI font.");
            host.Events.Subscribe<UserSettingsChanged>(change => ApplyPlayerSettings(change.Settings));
            scenes = new SceneService(host.Context, new UnitySceneBackend(), resources);
            views = new ViewService(host.Context, new UnityViewFactory((RectTransform)contractCanvas.transform,
                (request, content) =>
                {
                    if (request.Key == PlayerSettingsPanel.Key)
                    {
                        settingsPanel = content.GetComponent<PlayerSettingsPanel>();
                        settingsPanel.Bind(settings.Snapshot, Translate, settingsFont, SaveSettings, CloseSettings);
                        return;
                    }
                    if (request.Key == LeaderboardPanel.Key)
                    {
                        leaderboardPanel = content.GetComponent<LeaderboardPanel>();
                        if (leaderboardPanel == null) throw new InvalidOperationException("The leaderboard prefab needs a LeaderboardPanel component.");
                        return;
                    }
                    panel = content.GetComponent<ContractPanel>();
                    if (panel == null) throw new InvalidOperationException("The contract panel prefab needs a ContractPanel component.");
                    panel.Bind(Primary, Secondary, OpenSettings, Translate, settingsFont, OpenLeaderboard);
                }), resources);
            host.AddModule(new ResourceModule(resources));
            host.AddModule(new SceneModule(scenes));
            host.AddModule(new LocalizationModule(language));
            host.AddModule(new UserSettingsModule(settings));
            host.AddModule(new PresentationModule(views));
            host.AddModule(new ContractLifetime(this));
        }

        protected override async Task OnStartedAsync()
        {
            await views.OpenAsync(new ViewRequest("RainPassContract/Panel"));
            operation = OpenAsync();
            await operation;
            if (leaderboard != null)
            {
                try { await leaderboard.InitializeAsync(CancellationToken.None); }
                catch (Exception exception) { Debug.LogWarning($"Rain Pass leaderboard cache: {exception.Message}", this); }
            }
            if (downloads != null) await BeginContent(RefreshOffersAsync);
            if (Argument("-contract-evidence") != null || Argument("-scores-evidence") != null)
                gameObject.AddComponent<ContractDownloadEvidence>().Run(this);
            if (Argument("-settings-evidence") != null) gameObject.AddComponent<PlayerSettingsEvidence>().Run(this);
            if (Argument("-leaderboard-evidence") != null) gameObject.AddComponent<LeaderboardEvidence>().Run(this);
        }

        private void OpenSettings()
        {
            if (settingsView != null || !settingsOperation.IsCompleted) return;
            settingsOperation = ChangeSettingsViewAsync(open: true);
        }

        private void OpenLeaderboard()
        {
            if (leaderboard == null || leaderboardView != null || !leaderboardOperation.IsCompleted ||
                settingsView != null || !settingsOperation.IsCompleted) return;
            var offer = SelectedOffer;
            var version = offer == null ? null : InstalledVersion(offer.Id);
            if (offer == null || version == null) return;
            leaderboardOperation = OpenLeaderboardAsync(offer, version);
        }

        private async Task OpenLeaderboardAsync(DownloadableContractOffer offer, string version)
        {
            try
            {
                leaderboardView = await views.OpenAsync(new ViewRequest(LeaderboardPanel.Key, layer: 11, modal: true));
                leaderboardPanel.Bind(Translate, settingsFont, CloseLeaderboard, RefreshLeaderboard);
                await RefreshLeaderboardAsync(offer.Title, offer.Id, version);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass leaderboard window failed: {exception}", this);
                if (leaderboardPanel != null) leaderboardPanel.ShowError();
            }
        }

        private void CloseLeaderboard()
        {
            if (leaderboardView == null) return;
            if (leaderboardQuery != null) leaderboardQuery.Cancel();
            leaderboardOperation = CloseLeaderboardAsync(leaderboardOperation);
        }

        private async Task CloseLeaderboardAsync(Task pending)
        {
            var view = leaderboardView;
            leaderboardView = null;
            await pending;
            try { await views.CloseAsync(view); }
            catch (Exception exception) { Debug.LogWarning($"Rain Pass leaderboard close failed: {exception.Message}", this); }
            finally { leaderboardPanel = null; }
        }

        private void RefreshLeaderboard()
        {
            if (leaderboardPanel == null || !leaderboardOperation.IsCompleted) return;
            var offer = SelectedOffer;
            var version = offer == null ? null : InstalledVersion(offer.Id);
            if (offer != null && version != null) leaderboardOperation = RefreshLeaderboardAsync(offer.Title, offer.Id, version);
        }

        private async Task RefreshLeaderboardAsync(string title, string contractId, string version)
        {
            using var query = new CancellationTokenSource();
            leaderboardQuery = query;
            leaderboardPanel.ShowLoading(title, version);
            try
            {
                var result = await leaderboard.QueryAsync(contractId, version, query.Token);
                if (result.CacheError != null) Debug.LogWarning($"Rain Pass leaderboard cache: {result.CacheError}", this);
                leaderboardPanel.Show(title, version, result);
            }
            catch (OperationCanceledException) when (query.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass leaderboard query failed: {exception.Message}", this);
                leaderboardPanel.ShowError();
            }
            finally { leaderboardQuery = null; }
        }

        private void CloseSettings()
        {
            if (settingsView == null || !settingsOperation.IsCompleted) return;
            settingsOperation = ChangeSettingsViewAsync(open: false);
        }

        private async Task ChangeSettingsViewAsync(bool open)
        {
            try
            {
                if (open) settingsView = await views.OpenAsync(new ViewRequest(PlayerSettingsPanel.Key, layer: 10, modal: true));
                else
                {
                    await views.CloseAsync(settingsView);
                    settingsView = null;
                    settingsPanel = null;
                }
            }
            catch (Exception exception) { Report("Could not change the settings window.", exception); }
        }

        private void SaveSettings()
        {
            if (settingsOperation.IsCompleted) settingsOperation = SaveSettingsAsync();
        }

        private async Task SaveSettingsAsync()
        {
            var draft = settingsPanel.Draft;
            var pause = draft.InputBindings.Find(binding => binding.Action == "pause");
            if (draft.InputBindings.Exists(binding => binding != pause && binding.ContextId == pause.ContextId && binding.Control == pause.Control))
            {
                settingsPanel.Report("This key is assigned to another action.");
                return;
            }
            try { settings.Update(draft); }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass settings apply failed: {exception}", this);
                settingsPanel.Report("Settings could not be applied.");
                return;
            }
            settingsPanel.Report("Saving…", saving: true);
            try
            {
                await settings.SaveAsync();
                settingsPanel.Report("Settings saved.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Rain Pass settings save failed: {exception}", this);
                settingsPanel.Report("Settings could not be saved. Your changes are active. Retry to keep them after restart.", retry: true);
            }
        }

        private void ApplyPlayerSettings(UserSettingsData snapshot)
        {
            pauseKey = PlayerSettingsPanel.KeyLabel(snapshot.InputBindings.Find(binding => binding.Action == "pause").Control);
            if (showcase != null) showcase.ApplyPlayerSettings(snapshot);
        }

        private string Translate(string key) => string.IsNullOrEmpty(key) ? key : language.GetText(key);
        private string Format(string key, params object[] values) => language.GetFormat(key, values);

        private async Task OpenAsync()
        {
            error = null;
            try { await session.OpenAsync(); }
            catch (Exception exception) { Report("Could not read the completion record.", exception); }
        }

        protected override void Update()
        {
            base.Update();
            if (panel == null || !IsReady) return;
            if (scores != null && Phase != ContractPhase.Opening && scoreExchange.IsCompleted && Time.unscaledTime >= nextScoreExchange)
            {
                nextScoreExchange = Time.unscaledTime + 1;
                scoreExchange = ExchangeScoresAsync();
            }
            if (showcase != null && showcase.Run != null && operation.IsCompleted &&
                session.TryRecordVictory(showcase.Run.Status, showcase.CommissionTicks))
                operation = SaveAsync();
            if (downloads != null && !SettingsOpen && settingsOperation.IsCompleted &&
                leaderboardView == null && leaderboardOperation.IsCompleted && (Phase == ContractPhase.Camp ||
                Phase == ContractPhase.Battle && showcase?.Run?.Status == RunStatus.Paused))
            {
                if (Input.GetKeyDown(KeyCode.F5)) _ = BeginContent(RefreshOffersAsync);
                if (Input.GetKeyDown(KeyCode.U) && SelectedOffer != null) _ = InstallSelected();
                if (Input.GetKeyDown(KeyCode.Backspace) && Phase == ContractPhase.Camp && SelectedOffer != null) _ = RollbackSelected();
            }
            UpdatePanel();
            if (leaderboardView != null && leaderboardPanel != null) RainPassGamepad.Navigate(leaderboardPanel, CloseLeaderboard);
            else if (settingsView != null && settingsPanel != null) RainPassGamepad.Navigate(settingsPanel, CloseSettings);
            else if (panel.isActiveAndEnabled && leaderboardOperation.IsCompleted && settingsOperation.IsCompleted)
                RainPassGamepad.Navigate(panel, BackFromContractPanel);
        }

        private void BackFromContractPanel()
        {
            if (Phase == ContractPhase.Battle && showcase.Run.Status == RunStatus.Paused) showcase.Run.TogglePause();
            else if (Phase == ContractPhase.Loading || Phase == ContractPhase.Battle && showcase.Run.Status == RunStatus.Defeated)
                Secondary();
        }

        private async Task ExchangeScoresAsync()
        {
            try { await scores.ExchangeAsync(); }
            catch (OperationCanceledException) when (!IsReady) { }
            catch (Exception exception) { Debug.LogWarning($"Rain Pass score connection: {exception.Message}", this); }
        }

        private void Primary()
        {
            if (!operation.IsCompleted) return;
            if (Phase == ContractPhase.Opening) operation = OpenAsync();
            else if (Phase == ContractPhase.Camp)
            {
                if (!contentOperation.IsCompleted) return;
                var offer = SelectedOffer;
                if (offer != null && !installed.ContainsKey(offer.Id))
                    _ = InstallSelected();
                else if (session.TryAccept(offer?.Id ?? ContractSession.CedarId, offer == null ? "built-in" : installed[offer.Id].Manifest.Version))
                    operation = LoadAsync(offer);
            }
            else if (Phase == ContractPhase.Result)
            {
                if (session.SavedResult) StartReturn();
                else operation = SaveAsync();
            }
            else if (Phase == ContractPhase.Returning) operation = ReturnAsync(error);
            else if (Phase == ContractPhase.Battle && showcase.Run.Status == RunStatus.Defeated)
                showcase.RetryEncounter();
        }

        private void Secondary()
        {
            if (Phase == ContractPhase.Camp && downloads != null && contentOperation.IsCompleted && offers.Count != 0)
                selection = (selection + 1) % (offers.Count + 1);
            else if (Phase == ContractPhase.Loading)
            {
                cancelRequested = true;
                UpdatePanel();
            }
            else if (Phase == ContractPhase.Battle && operation.IsCompleted &&
                (showcase.Run.Status == RunStatus.Paused || showcase.Run.Status == RunStatus.Defeated))
                StartReturn();
        }

        private async Task LoadAsync(DownloadableContractOffer offer)
        {
            cancelRequested = false;
            error = null;
            try
            {
                // The activated scene creates its own host. Await it so retreat can stop that host before native unload.
                Material banner = null;
                if (offer == null) battle = await scenes.LoadAsync(BattleScenePath, SceneLoadMode.Additive);
                else
                {
                    activeContract = await downloads.ResolveAsync(offer.Id, installed[offer.Id]);
                    var snapshot = activeContract.Snapshot;
                    var catalog = UnityBundleCatalog.FromJson(File.ReadAllText(snapshot.GetPath("bundle-catalog.json")));
                    battleStore = new ResourceStore(new UnityAssetBundleLoader(catalog, snapshot.GetPath), Host.Dispatcher);
                    battleAssets = new ResourceScope(battleStore);
                    banner = (await battleAssets.LoadAsync<Material>("contract-banner")).Value;
                    var downloadedScenes = new SceneService(Host.Context, new UnityBundleSceneBackend(), battleStore);
                    battle = await downloadedScenes.LoadAsync("contract-scene", SceneLoadMode.Additive);
                }
                var scene = SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                {
                    showcase = root.GetComponentInChildren<CombatShowcase>();
                    if (showcase != null) break;
                }
                if (showcase == null) throw new InvalidOperationException("The combat scene has no Rain Pass host.");
                if (campCamera != null) campCamera.enabled = false;
                while (!cancelRequested && !showcase.CommissionReady && !showcase.CommissionUnavailable)
                    await Task.Yield();
                if (!cancelRequested)
                {
                    bool started = offer == null ? showcase.StartCommission() : showcase.StartCommission(activeContract, banner);
                    if (!started) throw new InvalidOperationException("The contract battle could not start.");
                    showcase.SetViewInput(new ViewInputRouter(views));
                    showcase.ApplyPlayerSettings(settings.Snapshot);
                    session.TryBeginBattle();
                    return;
                }
            }
            catch (OperationCanceledException) when (cancelRequested) { }
            catch (Exception exception) { Report(offer == null ? "Could not enter Cedar Grove." : "Could not enter " + offer.Title + ".", exception); }

            if (session.TryReturn()) await ReturnAsync(error);
        }

        private async Task SaveAsync()
        {
            error = null;
            try { await session.SaveResultAsync(); }
            catch (Exception exception) { Report("Completion could not be saved.", exception); }
        }

        private void StartReturn()
        {
            if (session.TryReturn()) operation = ReturnAsync(null);
        }

        private async Task ReturnAsync(string message)
        {
            error = null;
            try
            {
                await ReleaseBattleAsync();
                session.FinishReturn();
                error = message;
            }
            catch (Exception exception) { Report("Could not return to camp.", exception); }
        }

        private async Task ReleaseBattleAsync()
        {
            if (showcase != null) await showcase.ShutdownAsync();
            if (gameObject.scene.isLoaded) SceneManager.SetActiveScene(gameObject.scene);
            if (battle != null) await battle.UnloadAsync();
            battle = null;
            showcase = null;
            if (battleAssets != null) await battleAssets.DisposeAsync();
            battleAssets = null;
            if (battleStore != null) await battleStore.DisposeAsync();
            battleStore = null;
            activeContract = null;
            if (campCamera != null) campCamera.enabled = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Report(string message, Exception exception)
        {
            error = message;
            Debug.LogError($"Rain Pass contract {Phase}: {message} {exception}", this);
        }

        private void UpdatePanel()
        {
            var offer = SelectedOffer;
            panel.SetSettingsAvailable(Phase == ContractPhase.Camp || Phase == ContractPhase.Battle && showcase?.Run?.Status == RunStatus.Paused);
            panel.SetLeaderboardAvailable(Phase == ContractPhase.Camp && leaderboard != null && offer != null && InstalledVersion(offer.Id) != null);
            if (Phase == ContractPhase.Camp && downloads != null)
            {
                var version = offer == null ? null : InstalledVersion(offer.Id);
                var status = !contentOperation.IsCompleted ? "Checking or downloading content…" :
                    offer == null ? "Local contract. Use F5 to refresh the published offers." :
                    version == null ? Format("Available {0}.", offer.Version) :
                    Format("Installed {0} · Available {1}. U updates. Backspace rolls back.", version, offer.Version);
                if (offer != null && session.HasCompleted(offer.Id))
                    status = Translate(status) + (BestTicks(offer.Id, version ?? "") is int ticks ? Format(" Best {0:0.0}s.", ticks / 60f) : Translate(" Completed."));
                if (PendingScoreCount != 0) status = Translate(status) + Format(" Pending scores: {0}.", PendingScoreCount);
                panel.ShowOffer(offer?.Title ?? "Cedar Sentinel", status,
                    offer != null && version == null ? "Download" : "Accept contract",
                    !contentOperation.IsCompleted, offers.Count != 0, error ?? contentIssue);
                return;
            }
            string resume = RainPassGamepad.HasPad ? pauseKey + " / Menu / B" : pauseKey;
            panel.Render(Phase, Completed, cancelRequested, showcase?.Run?.Status, session.SavedResult,
                error ?? (Phase == ContractPhase.Battle ? contentIssue : null), offer?.Title,
                offer != null && showcase?.Run?.Status == RunStatus.Paused ?
                    (contentOperation.IsCompleted ? "F5 refreshes offers. U downloads the latest release." : "Downloading the latest release…") : null,
                resume, session.SavedResult && session.HasResultSubmission ?
                    (session.IsResultPending ? "Saved locally. Score pending." : "Score synced.") : null);
        }

        internal Task RefreshOffers() => BeginContent(RefreshOffersAsync);

        internal Task InstallSelected()
        {
            var offer = SelectedOffer;
            if (offer == null) return Task.CompletedTask;
            return BeginContent(async token => installed[offer.Id] = (await downloads.InstallAsync(offer, token)).Snapshot);
        }

        internal Task RollbackSelected()
        {
            var offer = SelectedOffer;
            if (offer == null) return Task.CompletedTask;
            return BeginContent(async token => installed[offer.Id] = (await downloads.RollbackAsync(offer.Id, token)).Snapshot);
        }

        private async Task RefreshOffersAsync(CancellationToken token)
        {
            var id = SelectedOffer?.Id;
            Exception refreshError = null;
            try { offers = await downloads.ReadOffersAsync(token); }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                refreshError = exception;
                offers = await downloads.ReadCachedOffersAsync();
            }
            selection = 0;
            for (int index = 0; index < offers.Count; index++)
            {
                var offer = offers[index];
                if (offer.Id == id) selection = index + 1;
                var snapshot = await downloads.OpenAsync(offer.Id);
                if (snapshot != null) installed[offer.Id] = snapshot;
                else installed.Remove(offer.Id);
            }
            if (refreshError != null) throw new IOException("The feed is unavailable. Cached offers are shown.", refreshError);
        }

        private Task BeginContent(Func<CancellationToken, Task> execute)
        {
            if (!contentOperation.IsCompleted) return contentOperation;
            return contentOperation = RunContentAsync(execute);
        }

        private async Task RunContentAsync(Func<CancellationToken, Task> execute)
        {
            contentIssue = null;
            using var cancellation = new CancellationTokenSource();
            contentCancellation = cancellation;
            try { await execute(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                contentIssue = exception.Message;
                Debug.LogWarning($"Rain Pass contract content operation failed: {exception}", this);
            }
            finally { contentCancellation = null; }
        }

        internal static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private sealed class ContractLifetime : GameModule
        {
            private readonly RainPassCamp camp;
            public ContractLifetime(RainPassCamp camp) => this.camp = camp;
            public override string Id => "rain-pass-contract";
            public override System.Collections.Generic.IReadOnlyCollection<string> Dependencies { get; } =
                new[] { "scenes", "presentation", "settings" };

            public override async Task StopAsync(GameContext context, System.Threading.CancellationToken token)
            {
                camp.cancelRequested = true;
                camp.contentCancellation?.Cancel();
                await camp.contentOperation;
                await camp.settingsOperation;
                await camp.operation;
                await camp.ReleaseBattleAsync();
                camp.downloads?.Dispose();
                if (camp.scores != null)
                {
                    try { await camp.scores.DisposeAsync(); }
                    catch (Exception exception) { Debug.LogWarning($"Rain Pass score shutdown: {exception.Message}", camp); }
                    await camp.scoreExchange;
                }
                if (camp.leaderboard != null)
                {
                    camp.leaderboardQuery?.Cancel();
                    await camp.leaderboardOperation;
                    try { await camp.leaderboard.DisposeAsync(); }
                    catch (Exception exception) { Debug.LogWarning($"Rain Pass leaderboard shutdown: {exception.Message}", camp); }
                }
            }
        }
    }
}
