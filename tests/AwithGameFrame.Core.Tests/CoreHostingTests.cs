using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class CoreHostingTests
    {
        private sealed class Probe : GameModule
        {
            private readonly List<string> _events;
            public Probe(string id, List<string> events, params string[] dependencies) { Id = id; _events = events; Dependencies = dependencies; }
            public override string Id { get; }
            public override IReadOnlyCollection<string> Dependencies { get; }
            public Func<GameContext, CancellationToken, Task> OnStart, OnStop, OnPause, OnResume;
            public Action<double> OnTick;
            public override Task StartAsync(GameContext c, CancellationToken t) { _events.Add("start:" + Id); return OnStart?.Invoke(c, t) ?? Task.CompletedTask; }
            public override Task StopAsync(GameContext c, CancellationToken t) { _events.Add("stop:" + Id); return OnStop?.Invoke(c, t) ?? Task.CompletedTask; }
            public override Task PauseAsync(GameContext c, CancellationToken t) { _events.Add("pause:" + Id); return OnPause?.Invoke(c, t) ?? Task.CompletedTask; }
            public override Task ResumeAsync(GameContext c, CancellationToken t) { _events.Add("resume:" + Id); return OnResume?.Invoke(c, t) ?? Task.CompletedTask; }
            public override void Tick(GameContext c, double seconds) => OnTick?.Invoke(seconds);
        }

        [Test]
        public async Task DependenciesStartFirstAndStopLast()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("game", events, "data"));
            host.AddModule(new Probe("data", events, "clock"));
            host.AddModule(new Probe("clock", events));
            await host.StartAsync();
            await host.StopAsync();
            Assert.That(events, Is.EqualTo(new[] { "start:clock", "start:data", "start:game", "stop:game", "stop:data", "stop:clock" }));
        }

        [Test]
        public async Task HostsHaveIndependentServicesAndStoppingOneDoesNotAffectAnother()
        {
            var first = new GameHost();
            var second = new GameHost();
            first.Services.Register<string>("first");
            second.Services.Register<string>("second");
            await first.StartAsync();
            await second.StartAsync();
            await first.StopAsync();
            Assert.That(first.Services.TryGet<string>(out _), Is.False);
            Assert.That(second.Services.Get<string>(), Is.EqualTo("second"));
            Assert.Throws<ObjectDisposedException>(() => first.Services.Register<string>("late"));
            await second.StopAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidDependencyGraphFailsBeforeAnyModuleStarts(bool cycle)
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("a", events, "b"));
            if (cycle) host.AddModule(new Probe("b", events, "a"));
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.That(error.Message, Does.Contain(cycle ? "a -> b -> a" : "missing module 'b'"));
            Assert.That(events, Is.Empty);
            Assert.That(host.State, Is.EqualTo(GameHostState.Faulted));
            Assert.Throws<InvalidOperationException>(() => host.StartAsync());
        }

        [Test]
        public void FailedStartCleansPartiallyStartedModuleAndEveryDependency()
        {
            var events = new List<string>();
            var host = new GameHost();
            var partial = new Probe("partial", events, "base") { OnStart = (_, _) => throw new FormatException("startup") };
            host.AddModule(new Probe("base", events));
            host.AddModule(partial);
            Assert.ThrowsAsync<FormatException>(() => host.StartAsync());
            Assert.That(events, Is.EqualTo(new[] { "start:base", "start:partial", "stop:partial", "stop:base" }));
            Assert.That(host.Context.LifetimeToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void StartFailurePreservesOriginalAndAttemptsEveryCleanup()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("a", events));
            host.AddModule(new Probe("b", events) { OnStart = (_, _) => throw new FormatException("original"), OnStop = (_, _) => throw new Exception("cleanup") });
            var error = Assert.ThrowsAsync<AggregateException>(() => host.StartAsync());
            Assert.That(error.InnerExceptions[0], Is.TypeOf<FormatException>());
            Assert.That(events, Does.Contain("stop:a"));
        }

        [Test]
        public async Task StopCancelsPendingStartBeforeReverseCleanup()
        {
            var events = new List<string>();
            var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new GameHost();
            host.AddModule(new Probe("pending", events) { OnStart = async (_, token) => { reached.SetResult(true); await Task.Delay(Timeout.Infinite, token); } });
            var start = host.StartAsync();
            await reached.Task;
            var stop = host.StopAsync();
            Assert.CatchAsync<OperationCanceledException>(async () => await start.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(events, Is.EqualTo(new[] { "start:pending", "stop:pending" }));
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
            Assert.Throws<InvalidOperationException>(() => host.StartAsync());
        }

        [Test]
        public async Task StartupThatReturnsAfterStopCannotPublishRunningState()
        {
            var events = new List<string>();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new GameHost();
            host.AddModule(new Probe("slow", events) { OnStart = (_, _) => release.Task });
            var start = host.StartAsync();
            var stop = host.StopAsync();
            Assert.That(host.Context.LifetimeToken.IsCancellationRequested, Is.True);
            release.SetResult(true);
            Assert.CatchAsync<OperationCanceledException>(async () => await start.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
            Assert.That(events, Does.Contain("stop:slow"));
        }

        [Test]
        public void PauseKeepsDispatchAliveAndResumeReceivesSeconds()
        {
            var events = new List<string>();
            double elapsed = 0;
            var host = new GameHost();
            host.AddModule(new Probe("game", events) { OnTick = seconds => elapsed += seconds });
            host.StartAsync().GetAwaiter().GetResult();
            host.Tick(0.25);
            host.PauseAsync().GetAwaiter().GetResult();
            var dispatched = host.Dispatcher.InvokeAsync(() => events.Add("dispatch"));
            host.Tick(1.0);
            Assert.That(dispatched.IsCompletedSuccessfully, Is.True);
            Assert.That(elapsed, Is.EqualTo(0.25));
            host.ResumeAsync().GetAwaiter().GetResult();
            host.Tick(0.5);
            Assert.That(elapsed, Is.EqualTo(0.75));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public async Task FailedPauseRollsBackIncludingPartiallyPausedModule()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("a", events));
            host.AddModule(new Probe("b", events) { OnPause = (_, _) => throw new FormatException("pause") });
            await host.StartAsync();
            Assert.ThrowsAsync<FormatException>(() => host.PauseAsync());
            Assert.That(events, Is.EqualTo(new[] { "start:a", "start:b", "pause:a", "pause:b", "resume:b", "resume:a" }));
            Assert.That(host.State, Is.EqualTo(GameHostState.Running));
            await host.StopAsync();
        }

        [Test]
        public async Task FailedPauseRollbackTerminatesSessionInsteadOfClaimingRunning()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("bad", events) { OnPause = (_, _) => throw new Exception("pause"), OnResume = (_, _) => throw new Exception("resume") });
            await host.StartAsync();
            Assert.ThrowsAsync<AggregateException>(() => host.PauseAsync());
            Assert.That(host.State, Is.EqualTo(GameHostState.Faulted));
            Assert.That(events, Does.Contain("stop:bad"));
        }

        [Test]
        public async Task StopDuringPauseCancelsTransitionWithoutResumingStoppedModules()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.AddModule(new Probe("game", events) { OnPause = (_, token) => Task.Delay(Timeout.Infinite, token) });
            await host.StartAsync();
            var pause = host.PauseAsync();
            var stop = host.StopAsync();
            Assert.CatchAsync<OperationCanceledException>(async () => await pause.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(events, Does.Not.Contain("resume:game"));
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
        }

        [Test]
        public async Task ShutdownContinuesAfterCancellationAndModuleCleanupFailures()
        {
            var events = new List<string>();
            var host = new GameHost();
            host.Services.Register<string>("service");
            host.AddModule(new Probe("a", events));
            host.AddModule(new Probe("b", events) { OnStop = (_, token) => { Assert.That(token.IsCancellationRequested, Is.False); throw new FormatException("stop"); } });
            await host.StartAsync();
            using var registration = host.Context.LifetimeToken.Register(() => throw new InvalidOperationException("cancel"));
            var stop = host.StopAsync();
            var error = Assert.ThrowsAsync<AggregateException>(() => stop);
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(events, Is.EqualTo(new[] { "start:a", "start:b", "stop:b", "stop:a" }));
            Assert.That(host.Services.TryGet<string>(out _), Is.False);
            Assert.That(host.StopAsync(), Is.SameAs(stop));
        }

        [Test]
        public void ModuleCannotAwaitReentrantHostStop()
        {
            var host = new GameHost();
            host.AddModule(new Probe("bad", new List<string>()) { OnStart = (_, _) => host.StopAsync() });
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.That(error.Message, Does.Contain("reenter"));
        }

        [Test]
        public void DispatcherBoundsWorkAndIsolatesFaultsAndCancellation()
        {
            using var dispatcher = new MainThreadDispatcher(8);
            var events = new List<string>();
            Task deferred = null;
            var first = dispatcher.InvokeAsync(() => { events.Add("first"); deferred = dispatcher.InvokeAsync(() => events.Add("deferred")); });
            var failed = dispatcher.InvokeAsync(() => throw new FormatException("item"));
            using var cancelled = new CancellationTokenSource();
            var skipped = dispatcher.InvokeAsync(() => events.Add("cancelled"), cancelled.Token);
            var last = dispatcher.InvokeAsync(() => events.Add("last"));
            cancelled.Cancel();
            Assert.That(dispatcher.RunPending(2), Is.EqualTo(2));
            Assert.That(first.IsCompletedSuccessfully, Is.True);
            Assert.That(failed.Exception.InnerException, Is.TypeOf<FormatException>());
            Assert.That(last.IsCompleted, Is.False);
            Assert.That(dispatcher.RunPending(8), Is.EqualTo(3));
            Assert.That(skipped.IsCanceled, Is.True);
            Assert.That(deferred.IsCompletedSuccessfully, Is.True);
            Assert.That(events, Is.EqualTo(new[] { "first", "last", "deferred" }));
        }

        [Test]
        public void DispatcherAcceptsWorkerRequestsButOnlyOwnerCanPump()
        {
            using var dispatcher = new MainThreadDispatcher();
            int owner = Environment.CurrentManagedThreadId, executed = 0;
            Task work = null;
            Task.Run(() => { work = dispatcher.InvokeAsync(() => executed = Environment.CurrentManagedThreadId); Assert.Throws<InvalidOperationException>(() => dispatcher.RunPending(1)); }).GetAwaiter().GetResult();
            dispatcher.RunPending(1);
            Assert.That(work.IsCompletedSuccessfully, Is.True);
            Assert.That(executed, Is.EqualTo(owner));
        }

        [Test]
        public void DispatcherCapacityAndShutdownDoNotLeaveWaitersPending()
        {
            var dispatcher = new MainThreadDispatcher(1);
            var pending = dispatcher.InvokeAsync(() => Assert.Fail("closed work must not run"));
            var overflow = dispatcher.InvokeAsync(() => { });
            Assert.That(overflow.Exception.InnerException, Is.TypeOf<InvalidOperationException>());
            dispatcher.Dispose();
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(dispatcher.InvokeAsync(() => { }).Exception.InnerException, Is.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void DispatcherDoesNotRunNewWorkAfterCallbackCancelsOldQueue()
        {
            using var dispatcher = new MainThreadDispatcher();
            Task deferred = null;
            var first = dispatcher.InvokeAsync(() =>
            {
                dispatcher.CancelPending();
                deferred = dispatcher.InvokeAsync(() => { });
            });
            var cancelled = dispatcher.InvokeAsync(() => Assert.Fail("cancelled work ran"));
            Assert.That(dispatcher.RunPending(8), Is.EqualTo(1));
            Assert.That(first.IsCompletedSuccessfully, Is.True);
            Assert.That(cancelled.IsCanceled, Is.True);
            Assert.That(deferred.IsCompleted, Is.False);
            dispatcher.RunPending(8);
            Assert.That(deferred.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task LifecycleEventsIdentifyHostModuleAndRollbackWithoutObserverInterference()
        {
            var host = new GameHost();
            var events = new List<HostEvent>();
            host.Lifecycle += _ => throw new InvalidOperationException("observer");
            host.Lifecycle += events.Add;
            host.AddModule(new Probe("module", new List<string>()));
            await host.StartAsync();
            await host.PauseAsync();
            await host.ResumeAsync();
            await host.StopAsync();
            Assert.That(events, Has.Count.EqualTo(16));
            Assert.That(events.TrueForAll(item => item.HostId == host.Id && item.ElapsedSeconds >= 0 && item.Error == null), Is.True);
            Assert.That(events[0].Phase, Is.EqualTo(HostPhase.Start));
            Assert.That(events[0].ModuleId, Is.Null);
            Assert.That(events[1].ModuleId, Is.EqualTo("module"));
            Assert.That(events[2].Status, Is.EqualTo(HostEventStatus.Completed));
            Assert.That(events[15].State, Is.EqualTo(GameHostState.Stopped));

            var failing = new GameHost();
            var failures = new List<HostEvent>();
            failing.Lifecycle += failures.Add;
            failing.AddModule(new Probe("bad", new List<string>()) { OnStart = (_, _) => throw new FormatException("module") });
            Assert.ThrowsAsync<FormatException>(() => failing.StartAsync());
            Assert.That(failing.Id, Is.Not.EqualTo(host.Id));
            Assert.That(failures.Exists(item => item.Phase == HostPhase.Stop && item.ModuleId == "bad" && item.IsRollback), Is.True);
            Assert.That(failures[failures.Count - 1].Status, Is.EqualTo(HostEventStatus.Failed));
            Assert.That(failures[failures.Count - 1].Error, Is.TypeOf<FormatException>());
        }
    }
}
