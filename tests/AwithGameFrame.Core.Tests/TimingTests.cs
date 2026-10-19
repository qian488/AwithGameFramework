using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Timing;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    public class TimingTests
    {
        private sealed class Probe : GameModule
        {
            public override string Id { get; }
            public Probe(string id = "probe") { Id = id; }
            public Func<GameContext, CancellationToken, Task> Start, Stop, Pause, Resume;
            public Action<GameContext, double> OnTick;
            public override Task StartAsync(GameContext context, CancellationToken token) => Start?.Invoke(context, token) ?? Task.CompletedTask;
            public override Task StopAsync(GameContext context, CancellationToken token) => Stop?.Invoke(context, token) ?? Task.CompletedTask;
            public override Task PauseAsync(GameContext context, CancellationToken token) => Pause?.Invoke(context, token) ?? Task.CompletedTask;
            public override Task ResumeAsync(GameContext context, CancellationToken token) => Resume?.Invoke(context, token) ?? Task.CompletedTask;
            public override void Tick(GameContext context, double seconds) => OnTick?.Invoke(context, seconds);
        }

        private static GameHost StartHost(int budget = 64)
        {
            var host = new GameHost(timerCallbackBudgetPerTick: budget);
            host.StartAsync().GetAwaiter().GetResult();
            return host;
        }

        [Test]
        public void IndependentClocksAreAvailableInContextAndServicesAndModulesReceiveScaledSeconds()
        {
            var first = new GameHost();
            var second = StartHost();
            var ticks = new List<double>();
            first.AddModule(new Probe { OnTick = (context, seconds) =>
            {
                Assert.That(context.Clock, Is.SameAs(first.Clock));
                Assert.That(context.Timers, Is.SameAs(first.Timers));
                Assert.That(context.Clock.ScaledDeltaTime, Is.EqualTo(seconds));
                ticks.Add(seconds);
            } });
            first.StartAsync().GetAwaiter().GetResult();
            first.Clock.TimeScale = 2;
            second.Clock.TimeScale = 0.5;
            first.Tick(0.25);
            second.Tick(0.25);
            Assert.That(first.Clock.ScaledTime, Is.EqualTo(0.5));
            Assert.That(first.Clock.UnscaledTime, Is.EqualTo(0.25));
            Assert.That(second.Clock.ScaledTime, Is.EqualTo(0.125));
            Assert.That(first.Services.Get<GameClock>(), Is.SameAs(first.Clock));
            Assert.That(first.Services.Get<TimerScheduler>(), Is.SameAs(first.Timers));
            Assert.That(ticks, Is.EqualTo(new[] { 0.5 }));
            first.StopAsync().GetAwaiter().GetResult();
            Assert.That(second.Timers.IsDisposed, Is.False);
            second.StopAsync().GetAwaiter().GetResult();
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidTimeScaleDoesNotChangeClock(double value)
        {
            var host = StartHost();
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Clock.TimeScale = value);
            Assert.That(host.Clock.TimeScale, Is.EqualTo(1));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void OverflowingTickDoesNotPartiallyAdvanceEitherDomain()
        {
            var host = StartHost();
            host.Clock.TimeScale = double.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Tick(double.MaxValue));
            Assert.That(host.Clock.ScaledTime, Is.Zero);
            Assert.That(host.Clock.UnscaledTime, Is.Zero);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void PauseFreezesBothDomainsAndTimersWhileDispatcherStillRuns()
        {
            var host = StartHost();
            host.Clock.TimeScale = 0.5;
            host.Tick(2);
            var scaled = host.Timers.DelayAsync(0.5);
            var unscaled = host.Timers.DelayAsync(0.5, GameTimeDomain.Unscaled);
            host.PauseAsync().GetAwaiter().GetResult();
            var dispatched = host.Dispatcher.InvokeAsync(() => { });
            host.Tick(100);
            Assert.That(dispatched.IsCompletedSuccessfully, Is.True);
            Assert.That(host.Clock.ScaledTime, Is.EqualTo(1));
            Assert.That(host.Clock.UnscaledTime, Is.EqualTo(2));
            Assert.That(host.Clock.ScaledDeltaTime, Is.Zero);
            Assert.That(host.Clock.UnscaledDeltaTime, Is.Zero);
            Assert.That(scaled.IsCompleted || unscaled.IsCompleted, Is.False);
            host.ResumeAsync().GetAwaiter().GetResult();
            host.Tick(0.5);
            Assert.That(unscaled.IsCompletedSuccessfully, Is.True);
            Assert.That(scaled.IsCompleted, Is.False);
            host.Tick(0.5);
            Assert.That(scaled.IsCompletedSuccessfully, Is.True);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void ZeroTimeScaleFreezesOnlyScaledTimersAndKeepsModuleTicks()
        {
            var host = new GameHost();
            var ticks = new List<double>();
            host.AddModule(new Probe { OnTick = (_, delta) => ticks.Add(delta) });
            host.StartAsync().GetAwaiter().GetResult();
            host.Clock.TimeScale = 0;
            var scaled = host.Timers.DelayAsync(1);
            var unscaled = host.Timers.DelayAsync(1, GameTimeDomain.Unscaled);
            host.Tick(1);
            Assert.That(unscaled.IsCompletedSuccessfully, Is.True);
            Assert.That(scaled.IsCompleted, Is.False);
            Assert.That(ticks, Is.EqualTo(new[] { 0d }));
            host.Clock.TimeScale = 1;
            host.Tick(1);
            Assert.That(scaled.IsCompletedSuccessfully, Is.True);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void OneShotWaitsForDeadlineAndZeroDelayNeverCallsSynchronously()
        {
            var host = StartHost();
            int count = 0;
            var once = host.Timers.Schedule(0.5, () => count++);
            host.Tick(0.25);
            Assert.That(count, Is.Zero);
            host.Tick(0.25);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(once.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(once.Cancel(), Is.False);
            var zero = host.Timers.Schedule(0, () => count++);
            Assert.That(count, Is.EqualTo(1));
            host.Tick(0);
            Assert.That(zero.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(count, Is.EqualTo(2));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void DeadlinesAndRegistrationOrderDetermineSameDomainDelivery()
        {
            var host = StartHost();
            var calls = new List<string>();
            host.Timers.Schedule(2, () => calls.Add("late"));
            host.Timers.Schedule(0.5, () => calls.Add("first"));
            host.Timers.Schedule(0.5, () => calls.Add("second"));
            host.Tick(3);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second", "late" }));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void RepeatingTimersSkipMissedIntervalsAndWaitAnotherInterval()
        {
            var host = StartHost();
            int count = 0;
            var repeat = host.Timers.Repeat(0.5, () => count++);
            host.Tick(100);
            Assert.That(count, Is.EqualTo(1));
            host.Tick(0);
            host.Tick(0.25);
            Assert.That(count, Is.EqualTo(1));
            host.Tick(0.25);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(repeat.Completion.IsCompleted, Is.False);
            repeat.Dispose();
            Assert.That(repeat.Completion.IsCanceled, Is.True);
            host.Tick(100);
            Assert.That(count, Is.EqualTo(2));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CallbackBudgetRetainsOverdueTimersForLaterTicks()
        {
            var host = StartHost(1);
            var calls = new List<int>();
            for (int i = 0; i < 3; i++) { var value = i; host.Timers.Schedule(0, () => calls.Add(value)); }
            host.Tick(10);
            Assert.That(calls, Is.EqualTo(new[] { 0 }));
            Assert.That(host.Timers.PendingCount, Is.EqualTo(2));
            host.Tick(0);
            host.Tick(0);
            Assert.That(calls, Is.EqualTo(new[] { 0, 1, 2 }));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CallbackCanAddForNextTickAndCancelAnotherDueCallbackImmediately()
        {
            var host = StartHost();
            var calls = new List<string>();
            TimerHandle cancelled = null, added = null;
            host.Timers.Schedule(0, () =>
            {
                calls.Add("first");
                Assert.That(cancelled.Cancel(), Is.True);
                added = host.Timers.Schedule(0, () => calls.Add("added"));
            });
            cancelled = host.Timers.Schedule(0, () => calls.Add("cancelled"));
            host.Tick(0);
            Assert.That(calls, Is.EqualTo(new[] { "first" }));
            Assert.That(cancelled.Completion.IsCanceled, Is.True);
            Assert.That(added.IsCompleted, Is.False);
            host.Tick(0);
            Assert.That(calls, Is.EqualTo(new[] { "first", "added" }));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void RepeatCanCancelItselfButStartedOneShotCannotBeCancelled()
        {
            var host = StartHost();
            TimerHandle repeat = null, once = null;
            int count = 0;
            repeat = host.Timers.Repeat(1, () => { count++; Assert.That(repeat.Cancel(), Is.True); });
            once = host.Timers.Schedule(1, () => Assert.That(once.Cancel(), Is.False));
            host.Tick(1);
            host.Tick(100);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(repeat.Completion.IsCanceled, Is.True);
            Assert.That(once.Completion.IsCompletedSuccessfully, Is.True);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CallbackShutdownCancelsRemainingTimersAndSkipsModuleTick()
        {
            var host = new GameHost();
            int moduleTicks = 0;
            host.AddModule(new Probe { OnTick = (_, _) => moduleTicks++ });
            host.StartAsync().GetAwaiter().GetResult();
            var first = host.Timers.Schedule(0, () => host.StopAsync().GetAwaiter().GetResult());
            var skipped = host.Timers.Schedule(0, () => Assert.Fail("A closed session cannot deliver another callback."));
            var delay = host.Timers.DelayAsync(5);
            host.Tick(1);
            Assert.That(first.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(skipped.Completion.IsCanceled && delay.IsCanceled, Is.True);
            Assert.That(host.Timers.PendingCount, Is.Zero);
            Assert.That(moduleTicks, Is.Zero);
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
        }

        [Test]
        public void CallbackPauseDefersOtherDueTimersUntilResume()
        {
            var host = StartHost();
            host.Timers.Schedule(0, () => host.PauseAsync().GetAwaiter().GetResult());
            var deferred = host.Timers.Schedule(0, () => { });
            host.Tick(0);
            Assert.That(host.State, Is.EqualTo(GameHostState.Paused));
            Assert.That(deferred.IsCompleted, Is.False);
            host.ResumeAsync().GetAwaiter().GetResult();
            host.Tick(0);
            Assert.That(deferred.Completion.IsCompletedSuccessfully, Is.True);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void FailingTimerAndModuleDoNotPreventRemainingCallbacksAndTicks()
        {
            var host = new GameHost();
            int goodTicks = 0, callbacks = 0;
            host.AddModule(new Probe("bad") { OnTick = (_, _) => throw new FormatException("module") });
            host.AddModule(new Probe("good") { OnTick = (_, _) => goodTicks++ });
            host.StartAsync().GetAwaiter().GetResult();
            var failed = host.Timers.Repeat(1, () => throw new InvalidOperationException("timer"));
            var good = host.Timers.Schedule(1, () => callbacks++);
            var error = Assert.Throws<AggregateException>(() => host.Tick(1));
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(failed.Completion.Exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(good.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(goodTicks, Is.EqualTo(1));
            Assert.That(host.Timers.PendingCount, Is.Zero);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void WorkerCancellationRemovesPendingDelayAndCanCancelActiveRepeat()
        {
            var host = StartHost();
            using var token = new CancellationTokenSource();
            var delay = host.Timers.DelayAsync(1, cancellationToken: token.Token);
            Task.Run(() => token.Cancel()).GetAwaiter().GetResult();
            Assert.That(delay.IsCanceled, Is.True);
            using var activeToken = new CancellationTokenSource();
            var repeat = host.Timers.Repeat(1, () => Task.Run(() => activeToken.Cancel()).GetAwaiter().GetResult(), cancellationToken: activeToken.Token);
            host.Tick(1);
            Assert.That(repeat.Completion.IsCanceled, Is.True);
            Assert.That(host.Timers.PendingCount, Is.Zero);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void AlreadyCancelledDelayCompletesWithoutOccupyingSchedulerAndWorkerSchedulingIsRejected()
        {
            var host = StartHost();
            using var token = new CancellationTokenSource();
            token.Cancel();
            var delay = host.Timers.DelayAsync(1, cancellationToken: token.Token);
            Assert.That(delay.IsCanceled, Is.True);
            Assert.That(host.Timers.PendingCount, Is.Zero);
            Task.Run(() => Assert.Throws<InvalidOperationException>(() => host.Timers.Schedule(0, () => { }))).GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void StartupTicksDoNotAdvanceTimeAndStopCancelsStartupTimers()
        {
            var host = new GameHost();
            Task delay = null;
            host.AddModule(new Probe { Start = (context, token) =>
            {
                delay = context.Timers.DelayAsync(1);
                return Task.Delay(Timeout.Infinite, token);
            } });
            var start = host.StartAsync();
            host.Tick(10);
            Assert.That(host.Clock.UnscaledTime, Is.Zero);
            Assert.That(delay.IsCompleted, Is.False);
            var stop = host.StopAsync();
            Assert.CatchAsync<OperationCanceledException>(() => start);
            stop.GetAwaiter().GetResult();
            Assert.That(delay.IsCanceled, Is.True);
            Assert.That(host.Timers.IsDisposed, Is.True);
        }

        [Test]
        public void FailedStartAndFailedPauseRollbackDisposeTimers()
        {
            var failedStart = new GameHost();
            var startupDelay = failedStart.Timers.DelayAsync(10);
            failedStart.AddModule(new Probe { Start = (_, _) => throw new FormatException("start") });
            Assert.ThrowsAsync<FormatException>(() => failedStart.StartAsync());
            Assert.That(startupDelay.IsCanceled, Is.True);
            Assert.That(failedStart.Timers.IsDisposed, Is.True);

            var failedPause = new GameHost();
            failedPause.AddModule(new Probe { Pause = (_, _) => throw new FormatException("pause"), Resume = (_, _) => throw new FormatException("rollback") });
            failedPause.StartAsync().GetAwaiter().GetResult();
            var pauseDelay = failedPause.Timers.DelayAsync(10);
            Assert.ThrowsAsync<AggregateException>(() => failedPause.PauseAsync());
            Assert.That(pauseDelay.IsCanceled, Is.True);
            Assert.That(failedPause.Timers.IsDisposed, Is.True);
            Assert.That(failedPause.State, Is.EqualTo(GameHostState.Faulted));
        }

        [Test]
        public void CleanupFailureAndStopBeforeStartStillCancelTimersAndPreventNewWork()
        {
            var host = new GameHost();
            host.AddModule(new Probe { Stop = (_, _) => throw new FormatException("cleanup") });
            host.StartAsync().GetAwaiter().GetResult();
            var delay = host.Timers.DelayAsync(1);
            Assert.ThrowsAsync<AggregateException>(() => host.StopAsync());
            Assert.That(delay.IsCanceled, Is.True);
            Assert.Throws<ObjectDisposedException>(() => host.Timers.Schedule(0, () => { }));
            Assert.Throws<ObjectDisposedException>(() => host.Clock.TimeScale = 1);

            var notStarted = new GameHost();
            var pending = notStarted.Timers.DelayAsync(1);
            notStarted.StopAsync().GetAwaiter().GetResult();
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(notStarted.Timers.IsDisposed, Is.True);
        }

        [Test]
        public void BudgetBacklogCannotBeStarvedByRepeatsFromAnotherClockDomain()
        {
            var host = new GameHost(timerCallbackBudgetPerTick: 1);
            host.StartAsync().GetAwaiter().GetResult();
            host.Clock.TimeScale = 100;
            host.Tick(10);
            int scaledCalls = 0, unscaledCalls = 0;
            using var repeated = host.Timers.Repeat(1, () => unscaledCalls++, GameTimeDomain.Unscaled);
            host.Timers.Schedule(100, () => scaledCalls++);
            host.Tick(1);
            host.Tick(1);
            Assert.That(scaledCalls, Is.EqualTo(1));
            Assert.That(unscaledCalls, Is.EqualTo(1));
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void InvalidTimerArgumentsAndBudgetsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameHost(timerCallbackBudgetPerTick: 0));
            var host = StartHost();
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.Schedule(-1, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.Schedule(double.NaN, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.Schedule(double.PositiveInfinity, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.Repeat(0, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.Repeat(double.PositiveInfinity, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.Timers.DelayAsync(1, (GameTimeDomain)99));
            Assert.Throws<ArgumentNullException>(() => host.Timers.Schedule(0, null));
            host.StopAsync().GetAwaiter().GetResult();
        }
    }
}
