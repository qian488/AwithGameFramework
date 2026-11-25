using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
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
        private SceneHandle battle;
        private CombatShowcase showcase;
        private Task operation = Task.CompletedTask;
        private bool cancelRequested;
        private string error;
        [SerializeField] private Camera campCamera;
        [SerializeField] private Canvas contractCanvas;

        public ContractPhase Phase => session?.Phase ?? ContractPhase.Opening;
        public bool Completed => session?.Completed == true;
        internal string SaveDirectory { get; set; }
        internal string BattleScenePath { get; set; } = CombatShowcase.ScenePath;
        internal int LoadedBattleCount => scenes.Loaded.Count;
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
            session = new ContractSession(SaveDirectory ?? Path.Combine(UnityEngine.Application.persistentDataPath, "RainPassContract"));
            var resources = new ResourceStore(new UnityResourceLoader(), host.Dispatcher);
            scenes = new SceneService(host.Context, new UnitySceneBackend(), resources);
            views = new ViewService(host.Context, new UnityViewFactory((RectTransform)contractCanvas.transform,
                (_, content) =>
                {
                    panel = content.GetComponent<ContractPanel>();
                    if (panel == null) throw new InvalidOperationException("The contract panel prefab needs a ContractPanel component.");
                    panel.Bind(Primary, Secondary);
                }), resources);
            host.AddModule(new ResourceModule(resources));
            host.AddModule(new SceneModule(scenes));
            host.AddModule(new PresentationModule(views));
            host.AddModule(new ContractLifetime(this));
        }

        protected override async Task OnStartedAsync()
        {
            await views.OpenAsync(new ViewRequest("RainPassContract/Panel"));
            operation = OpenAsync();
            await operation;
        }

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
            if (showcase != null && showcase.Run != null && operation.IsCompleted &&
                session.TryRecordVictory(showcase.Run.Status))
                operation = SaveAsync();
            UpdatePanel();
        }

        private void Primary()
        {
            if (!operation.IsCompleted) return;
            if (Phase == ContractPhase.Opening) operation = OpenAsync();
            else if (session.TryAccept()) operation = LoadAsync();
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
            if (Phase == ContractPhase.Loading)
            {
                cancelRequested = true;
                UpdatePanel();
            }
            else if (Phase == ContractPhase.Battle && operation.IsCompleted &&
                (showcase.Run.Status == RunStatus.Paused || showcase.Run.Status == RunStatus.Defeated))
                StartReturn();
        }

        private async Task LoadAsync()
        {
            cancelRequested = false;
            error = null;
            try
            {
                // The activated scene creates its own host. Await it so retreat can stop that host before native unload.
                battle = await scenes.LoadAsync(BattleScenePath, SceneLoadMode.Additive);
                var scene = SceneManager.GetSceneByPath(battle.Key);
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
                    if (!showcase.StartCommission()) throw new InvalidOperationException("Cedar Sentinel could not start.");
                    session.TryBeginBattle();
                    return;
                }
            }
            catch (OperationCanceledException) when (cancelRequested) { }
            catch (Exception exception) { Report("Could not enter Cedar Grove.", exception); }

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
            panel.Render(Phase, Completed, cancelRequested, showcase?.Run?.Status, session.SavedResult, error);
        }

        private sealed class ContractLifetime : GameModule
        {
            private readonly RainPassCamp camp;
            public ContractLifetime(RainPassCamp camp) => this.camp = camp;
            public override string Id => "rain-pass-contract";
            public override System.Collections.Generic.IReadOnlyCollection<string> Dependencies { get; } =
                new[] { "scenes", "presentation" };

            public override async Task StopAsync(GameContext context, System.Threading.CancellationToken token)
            {
                camp.cancelRequested = true;
                await camp.operation;
                await camp.ReleaseBattleAsync();
            }
        }
    }
}
