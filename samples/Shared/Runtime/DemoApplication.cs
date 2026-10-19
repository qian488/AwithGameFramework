using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Flow;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Pooling;
using AwithGameFrame.Data.Configuration;
using AwithGameFrame.Data.Diagnostics;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Samples.Shared
{
    [Serializable]
    public sealed class DemoSaveData
    {
        public int Coins { get; set; }
        public int CompletedLevels { get; set; }
    }

    /// <summary>The same application module runs in all three hosts. Hosts own rendering and input.</summary>
    public sealed class DemoApplication : GameModule
    {
        private readonly byte[] configuration;
        private readonly VersionedSaveService<DemoSaveData> saves;
        private ConfigTable<LevelConfig> levels;
        private NavigationStack flow;
        private DemoSaveData progress = new DemoSaveData();
        private readonly SemaphoreSlim commands = new SemaphoreSlim(1, 1);
        private CancellationToken lifetime;

        public DemoApplication(string saveDirectory, byte[] configuration)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            saves = new VersionedSaveService<DemoSaveData>(saveDirectory, new JsonSerializer(), 1);
        }

        public override string Id => "demo.application";
        public string Phase { get; private set; } = "Stopped";
        public int Coins => progress.Coins;
        public int CompletedLevels => progress.CompletedLevels;
        public int LevelId { get; private set; }
        public long Frames { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public bool RecoveredFromBackup { get; private set; }
        public event Action Changed;

        public override async Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            lifetime = context.LifetimeToken;
            levels = await ConfigTable<LevelConfig>.LoadAsync(configuration, new JsonSerializer(), LevelConfig.SchemaHash, row => row.Id, cancellationToken);
            var saved = await saves.LoadAsync(0, cancellationToken);
            if (saved.Status != SaveLoadStatus.NotFound) progress = saved.Data;
            RecoveredFromBackup = saved.Status == SaveLoadStatus.RecoveredFromBackup;
            flow = new NavigationStack(context);
            await flow.NavigateAsync(new PhaseFlow(this, "Menu"), cancellationToken);
        }

        public Task EnterLevelAsync(int levelId = 1, CancellationToken cancellationToken = default)
        {
            return RunCommandAsync(async token =>
            {
                if (Phase != "Menu") throw new InvalidOperationException("Enter a level from the menu.");
                _ = levels[levelId];
                LevelId = levelId;
                await flow.NavigateAsync(new PhaseFlow(this, "Playing"), token);
            }, cancellationToken);
        }

        public Task CompleteLevelAsync(CancellationToken cancellationToken = default)
        {
            return RunCommandAsync(async token =>
            {
                if (Phase != "Playing") throw new InvalidOperationException("No level is running.");
                var next = new DemoSaveData { Coins = checked(progress.Coins + levels[LevelId].Reward), CompletedLevels = checked(progress.CompletedLevels + 1) };
                // Publish new progress only after the durable save succeeds.
                await saves.SaveAsync(0, next, "Completed level " + LevelId, token);
                progress = next;
                if (!await flow.BackAsync(token)) throw new InvalidOperationException("The level has no return destination.");
            }, cancellationToken);
        }

        private async Task RunCommandAsync(Func<CancellationToken, Task> command, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
            await commands.WaitAsync(linked.Token);
            try { linked.Token.ThrowIfCancellationRequested(); await command(linked.Token); }
            finally { commands.Release(); }
        }

        public override void Tick(GameContext context, double deltaSeconds)
        {
            if (Phase != "Playing") return;
            Frames++;
            ElapsedSeconds += deltaSeconds;
        }

        public override async Task StopAsync(GameContext context, CancellationToken cancellationToken)
        {
            // The host has cancelled lifetime; admitted commands finish/cancel before their flow is released.
            await commands.WaitAsync(cancellationToken);
            try
            {
                if (flow != null) await flow.StopAsync();
                SetPhase("Stopped");
            }
            finally { commands.Release(); }
        }

        private void SetPhase(string value) { Phase = value; Changed?.Invoke(); }

        private sealed class PhaseFlow : GameFlow
        {
            private readonly DemoApplication application;
            private readonly string phase;
            public PhaseFlow(DemoApplication application, string phase) { this.application = application; this.phase = phase; }
            public override string Id => phase;
            public override Task EnterAsync(GameContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                application.SetPhase(phase);
                return Task.CompletedTask;
            }
        }
    }

    public static class DemoScenario
    {
        private sealed class FrameBuffer { public int Value; }
        private readonly struct FrameSignal
        {
            public FrameSignal(int value) => Value = value;
            public int Value { get; }
        }
        /// <summary>Run a real host on one thread in console tools without an engine synchronization context.</summary>
        public static void RunBlocking(string directory, byte[] configuration, Action<string> report = null)
        {
            var previous = SynchronizationContext.Current;
            using var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var scenario = RunAsync(directory, configuration, report);
                while (!scenario.IsCompleted) context.PumpOnce();
                scenario.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        /// <summary>Exercise real startup, ticks, pause/resume, save, independent hosts and reopen.</summary>
        public static async Task RunAsync(string directory, byte[] configuration, Action<string> report = null)
        {
            Directory.CreateDirectory(directory);
            var isolatedDirectory = Path.Combine(directory, "isolated");
            var host = new GameHost();
            using var subscriptions = host.Events.CreateScope(host.Context.LifetimeToken);
            using var pool = new ObjectPool<FrameBuffer>(() => new FrameBuffer(), capacity: 2, onReturn: buffer => buffer.Value = 0);
            pool.Prewarm(2);
            int eventCount = 0, timerCount = 0;
            subscriptions.Subscribe<FrameSignal>(signal => eventCount += signal.Value);
            using var journal = new HostEventJournal(Path.Combine(directory, "host-events.jsonl"), host);
            var app = new DemoApplication(directory, configuration);
            host.AddModule(app);
            try
            {
                await host.StartAsync();
                var scaledDelay = host.Timers.DelayAsync(0.25);
                host.Clock.TimeScale = 0.5;
                host.Tick(0.5);
                if (!scaledDelay.IsCompleted || Math.Abs(host.Clock.ScaledTime - 0.25) > 1e-9)
                    throw new InvalidOperationException("Scaled game clock or delay failed.");
                host.Clock.TimeScale = 1;
                using var repeat = host.Timers.Repeat(1.0 / 60, () =>
                {
                    using var buffer = pool.RentLease();
                    if (buffer.Value.Value != 0) throw new InvalidOperationException("Pooled state was not reset.");
                    buffer.Value.Value = 1;
                    timerCount++;
                    host.Events.Publish(new FrameSignal(buffer.Value.Value)).ThrowIfFailed();
                });
                int coinsBefore = app.Coins;
                await app.EnterLevelAsync();
                for (int frame = 0; frame < 60; frame++) host.Tick(1.0 / 60);
                await host.PauseAsync();
                var pausedTime = host.Clock.ScaledTime;
                host.Tick(1.0);
                if (app.Frames != 60) throw new InvalidOperationException("Pause advanced game frames.");
                if (timerCount != 60 || eventCount != 60 || host.Clock.ScaledTime != pausedTime || pool.CountRented != 0)
                    throw new InvalidOperationException("Timer, event or pooled state did not follow host time.");
                await host.ResumeAsync();
                await app.CompleteLevelAsync();
                if (app.Coins != coinsBefore + 10 || app.Phase != "Menu") throw new InvalidOperationException("Level result was not saved.");

                var independent = new GameHost();
                var independentApp = new DemoApplication(isolatedDirectory, configuration);
                independent.AddModule(independentApp);
                try
                {
                    await independent.StartAsync();
                    independent.Events.Publish(new FrameSignal(100)).ThrowIfFailed();
                    if (independentApp.Coins != 0) throw new InvalidOperationException("Host state leaked between sessions.");
                    if (eventCount != 60 || independent.Clock.ScaledTime != 0) throw new InvalidOperationException("Host events or clocks were shared.");
                }
                finally { await independent.StopAsync(); }

                await host.StopAsync();
                if (!repeat.Completion.IsCanceled || !subscriptions.IsDisposed)
                    throw new InvalidOperationException("Host shutdown retained timers or event subscriptions.");
                report?.Invoke("FOUNDATION_CORE_PASSED events=60 timers=60 pool=2");
                var reopened = new GameHost();
                var restored = new DemoApplication(directory, configuration);
                reopened.AddModule(restored);
                try
                {
                    await reopened.StartAsync();
                    if (restored.Coins != coinsBefore + 10 || restored.CompletedLevels != app.CompletedLevels)
                        throw new InvalidOperationException("Reopened save differs from the completed level.");
                    report?.Invoke("SHARED_SCENARIO_PASSED coins=" + restored.Coins + " frames=60 phase=" + restored.Phase);
                }
                finally { await reopened.StopAsync(); }
            }
            finally { await host.StopAsync(); }
        }

        private sealed class PumpContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<Action> work = new BlockingCollection<Action>();
            public override void Post(SendOrPostCallback callback, object state) => work.Add(() => callback(state));
            public void PumpOnce() { if (work.TryTake(out var callback, 10)) callback(); }
            public void Dispose() => work.Dispose();
        }
    }
}
