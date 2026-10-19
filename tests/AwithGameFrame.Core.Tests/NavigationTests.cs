using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Flow;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Extensions.Presentation;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class NavigationTests
    {
        [Test]
        public async Task NavigateThenBack_ShouldExitPlayingAndReenterMenuWithStableHistorySnapshot()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var snapshot = navigation.History;
            Assert.That(navigation.Current, Is.SameAs(playing));
            Assert.That(snapshot, Is.EqualTo(new[] { menu }));
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(navigation.Current, Is.SameAs(menu));
            Assert.That(navigation.History, Is.Empty);
            Assert.That(snapshot, Is.EqualTo(new[] { menu }));
            Assert.That(await navigation.BackAsync(), Is.False);
            await navigation.StopAsync();
            await host.StopAsync();
            Assert.That(events, Is.EqualTo(new[] { "enter:menu", "exit:menu", "enter:playing", "exit:playing", "enter:menu", "exit:menu" }));
            Assert.That(menu.IsActive || playing.IsActive, Is.False);
        }

        [Test]
        public async Task Replace_ShouldDiscardCurrentDestinationWhileKeepingItsReturnHistory()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var loading = new Probe("loading", events);
            var playing = new Probe("playing", events);
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(loading);
            await navigation.ReplaceAsync(playing);
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(loading.Exits, Is.EqualTo(1));
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(navigation.Current, Is.SameAs(menu));
            Assert.That(loading.Enters, Is.EqualTo(1));
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task FailedNavigation_ShouldCleanPartialDestinationAndRestorePreviousWithoutCommittingHistory()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            var broken = new Probe("broken", events) { OnEnter = token => throw new FormatException("missing view data") };
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            Assert.ThrowsAsync<FormatException>(() => navigation.NavigateAsync(broken));
            Assert.That(navigation.Current, Is.SameAs(playing));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(playing.Enters, Is.EqualTo(2));
            Assert.That(broken.IsActive, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "enter:menu", "exit:menu", "enter:playing", "exit:playing", "enter:broken", "exit:broken", "enter:playing" }));
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task FailedBack_ShouldKeepReturnDestinationForRetryAndRestoreActiveFlow()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            menu.OnEnter = token => menu.Enters == 2 ? throw new FormatException("failed to reload menu") : Task.CompletedTask;
            var playing = new Probe("playing", events);
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            Assert.ThrowsAsync<FormatException>(async () => await navigation.BackAsync());
            Assert.That(navigation.Current, Is.SameAs(playing));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(navigation.Current, Is.SameAs(menu));
            Assert.That(menu.Enters, Is.EqualTo(3));
            Assert.That(navigation.History, Is.Empty);
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task CallerCancellation_ShouldRestoreCommittedCurrentAndKeepHistory()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            var entered = Signal();
            var pending = new Probe("pending", events) { OnEnter = token => { entered.TrySetResult(true); return Task.Delay(Timeout.Infinite, token); } };
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            using var cancellation = new CancellationTokenSource();
            var change = navigation.NavigateAsync(pending, cancellation.Token);
            await entered.Task;
            Assert.That(navigation.Current, Is.Null);
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await change.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.That(navigation.Current, Is.SameAs(playing));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(pending.IsActive, Is.False);
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task NewNavigation_ShouldCancelPartialEnterAndUseLastCommittedHistory()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            var entered = Signal();
            var pending = new Probe("pending", events) { OnEnter = token => { entered.TrySetResult(true); return Task.Delay(Timeout.Infinite, token); } };
            var settings = new Probe("settings", events);
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var first = navigation.NavigateAsync(pending);
            await entered.Task;
            await navigation.NavigateAsync(settings).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(navigation.Current, Is.SameAs(settings));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu, playing }));
            Assert.That(playing.Enters, Is.EqualTo(1), "Superseded navigation must not needlessly restore the previous view.");
            Assert.That(pending.IsActive, Is.False);
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(navigation.Current, Is.SameAs(playing));
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task NewNavigationDuringRollback_ShouldCancelRollbackAndPreserveOriginalFailure()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var restoring = Signal();
            var playing = new Probe("playing", events);
            playing.OnEnter = token =>
            {
                if (playing.Enters == 1) return Task.CompletedTask;
                restoring.TrySetResult(true);
                return Task.Delay(Timeout.Infinite, token);
            };
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var failed = navigation.NavigateAsync(new Probe("broken", events) { OnEnter = token => throw new FormatException("original error") });
            await restoring.Task;
            var settings = new Probe("settings", events);
            await navigation.NavigateAsync(settings).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.ThrowsAsync<FormatException>(async () => await failed);
            Assert.That(navigation.Current, Is.SameAs(settings));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu, playing }));
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task LatestRequestCancelledWhileQueued_ShouldWaitForPriorCleanupAndRestoreCommittedFlow()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            var releaseCleanup = Signal();
            var cleaning = Signal();
            var pending = new Probe("pending", events)
            {
                OnEnter = token => Task.Delay(Timeout.Infinite, token),
                OnExit = () => { cleaning.TrySetResult(true); return releaseCleanup.Task; }
            };
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var first = navigation.NavigateAsync(pending);
            using var cancellation = new CancellationTokenSource();
            var queued = navigation.NavigateAsync(new Probe("queued", events), cancellation.Token);
            await cleaning.Task;
            cancellation.Cancel();
            releaseCleanup.TrySetResult(true);
            Assert.CatchAsync<OperationCanceledException>(async () => await first.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.CatchAsync<OperationCanceledException>(async () => await queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.That(navigation.Current, Is.SameAs(playing));
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(playing.IsActive, Is.True);
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task FailedNavigationAndRestoration_ShouldReportBothAndExposeActualEmptyCurrent()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            playing.OnEnter = token => playing.Enters > 1 ? throw new IOException("restore failed") : Task.CompletedTask;
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var error = Assert.ThrowsAsync<AggregateException>(() => navigation.NavigateAsync(new Probe("broken", events)
            {
                OnEnter = token => throw new FormatException("enter failed")
            }));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<FormatException>());
            Assert.That(error.InnerExceptions[1], Is.TypeOf<IOException>());
            Assert.That(navigation.Current, Is.Null);
            Assert.That(navigation.History, Is.EqualTo(new[] { menu }));
            Assert.That(playing.IsActive, Is.False);
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(navigation.Current, Is.SameAs(menu));
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task StopDuringEnter_ShouldCleanPartialFlowClearHistoryAndPreventReopen()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            var playing = new Probe("playing", events);
            var entered = Signal();
            var pending = new Probe("pending", events) { OnEnter = token => { entered.TrySetResult(true); return Task.Delay(Timeout.Infinite, token); } };
            await navigation.NavigateAsync(menu);
            await navigation.NavigateAsync(playing);
            var change = navigation.NavigateAsync(pending);
            await entered.Task;
            var stop = navigation.StopAsync();
            await stop.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.CatchAsync<OperationCanceledException>(async () => await change);
            Assert.That(navigation.StopAsync(), Is.SameAs(stop));
            Assert.That(navigation.IsStopped, Is.True);
            Assert.That(navigation.Current, Is.Null);
            Assert.That(navigation.History, Is.Empty);
            Assert.That(menu.IsActive || playing.IsActive || pending.IsActive, Is.False);
            Assert.That(playing.Enters, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => navigation.NavigateAsync(menu));
            Assert.Throws<ObjectDisposedException>(() => navigation.BackAsync());
            await host.StopAsync();
        }

        [Test]
        public async Task StopFailure_ShouldStillClearHistoryAndPreserveFaultedStopTask()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            await navigation.NavigateAsync(new Probe("menu", events));
            var playing = new Probe("playing", events) { OnExit = () => throw new IOException("release failure") };
            await navigation.NavigateAsync(playing);
            var stop = navigation.StopAsync();
            var error = Assert.ThrowsAsync<AggregateException>(async () => await stop);
            Assert.That(error.Flatten().InnerExceptions[0], Is.TypeOf<IOException>());
            Assert.That(navigation.IsStopped, Is.True);
            Assert.That(navigation.Current, Is.Null);
            Assert.That(navigation.History, Is.Empty);
            Assert.That(navigation.StopAsync(), Is.SameAs(stop));
            Assert.That(playing.Exits, Is.EqualTo(1));
            await host.StopAsync();
        }

        [Test]
        public async Task PreCancelledNavigation_ShouldNotCancelAnotherInFlightEnter()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var release = Signal();
            var pending = new Probe("pending", events) { OnEnter = token => release.Task.WaitAsync(token) };
            var first = navigation.NavigateAsync(pending);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => navigation.NavigateAsync(new Probe("cancelled", events), cancellation.Token));
            Assert.That(first.IsCompleted, Is.False);
            release.TrySetResult(true);
            await first;
            Assert.That(navigation.Current, Is.SameAs(pending));
            Assert.That(pending.Exits, Is.Zero);
            await navigation.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task ReentrantNavigation_ShouldFailWithoutDeadlockingAndRestorePreviousFlow()
        {
            var events = new List<string>();
            var host = new GameHost();
            var navigation = new NavigationStack(host.Context);
            var menu = new Probe("menu", events);
            await navigation.NavigateAsync(menu);
            var reentrant = new Probe("reentrant", events)
            {
                OnEnter = token => navigation.NavigateAsync(new Probe("nested", events))
            };
            Assert.ThrowsAsync<InvalidOperationException>(() => navigation.NavigateAsync(reentrant));
            Assert.That(navigation.Current, Is.SameAs(menu));
            Assert.That(navigation.History, Is.Empty);
            Assert.That(reentrant.IsActive, Is.False);
            await navigation.StopAsync();
            await host.StopAsync();
        }

        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Probe : GameFlow
        {
            private readonly List<string> events;
            internal Probe(string id, List<string> events) { Id = id; this.events = events; }
            public override string Id { get; }
            internal Func<CancellationToken, Task> OnEnter;
            internal Func<Task> OnExit;
            internal int Enters;
            internal int Exits;
            internal bool IsActive;
            public override Task EnterAsync(GameContext context, CancellationToken cancellationToken)
            {
                Enters++;
                IsActive = true;
                events.Add("enter:" + Id);
                return OnEnter?.Invoke(cancellationToken) ?? Task.CompletedTask;
            }
            public override Task ExitAsync(GameContext context, CancellationToken cancellationToken)
            {
                Assert.That(cancellationToken.IsCancellationRequested, Is.False, "Cleanup must receive a usable token.");
                Exits++;
                IsActive = false;
                events.Add("exit:" + Id);
                return OnExit?.Invoke() ?? Task.CompletedTask;
            }
        }
    }
}
