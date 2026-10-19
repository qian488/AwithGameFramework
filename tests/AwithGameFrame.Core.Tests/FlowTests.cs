using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Flow;
using AwithGameFrame.Core.Hosting;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class FlowTests
    {
        private sealed class Probe : GameFlow
        {
            private readonly List<string> _events;
            public Probe(string id, List<string> events) { Id = id; _events = events; }
            public override string Id { get; }
            public Func<CancellationToken, Task> OnEnter;
            public Func<Task> OnExit;
            public Action<double> OnTick;
            public override Task EnterAsync(GameContext context, CancellationToken token) { _events.Add("enter:" + Id); return OnEnter?.Invoke(token) ?? Task.CompletedTask; }
            public override Task ExitAsync(GameContext context, CancellationToken token) { Assert.That(token.IsCancellationRequested, Is.False); _events.Add("exit:" + Id); return OnExit?.Invoke() ?? Task.CompletedTask; }
            public override void Tick(GameContext context, double seconds) => OnTick?.Invoke(seconds);
        }

        [Test]
        public async Task SwitchingExitsPreviousFlowBeforeEnteringNext()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            await machine.SwitchAsync(new Probe("a", events));
            await machine.SwitchAsync(new Probe("b", events));
            await machine.StopAsync();
            Assert.That(events, Is.EqualTo(new[] { "enter:a", "exit:a", "enter:b", "exit:b" }));
            Assert.That(machine.Current, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => machine.SwitchAsync(new Probe("late", events)));
            await host.StopAsync();
        }

        [Test]
        public async Task NewRequestCancelsPartialEnterAndCleansItBeforeNextEnter()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            var first = machine.SwitchAsync(new Probe("pending", events) { OnEnter = token => Task.Delay(Timeout.Infinite, token) });
            var next = new Probe("next", events);
            await machine.SwitchAsync(next).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(events, Is.EqualTo(new[] { "enter:pending", "exit:pending", "enter:next" }));
            Assert.That(machine.Current, Is.SameAs(next));
            await machine.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task CancelledRequestDoesNotEvictCurrentFlow()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            var current = new Probe("current", events);
            await machine.SwitchAsync(current);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => machine.SwitchAsync(new Probe("cancelled", events), cancel.Token));
            Assert.That(machine.Current, Is.SameAs(current));
            Assert.That(events, Is.EqualTo(new[] { "enter:current" }));
            await machine.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task CancellationAfterEnterReturnsStillCleansPartialFlow()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            using var cancel = new CancellationTokenSource();
            var flow = new Probe("cancelled", events) { OnEnter = _ => { cancel.Cancel(); return Task.CompletedTask; } };
            Assert.CatchAsync<OperationCanceledException>(() => machine.SwitchAsync(flow, cancel.Token));
            Assert.That(machine.Current, Is.Null);
            Assert.That(events, Is.EqualTo(new[] { "enter:cancelled", "exit:cancelled" }));
            await machine.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task FailedEnterAndFailedCleanupPreserveBothErrors()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            var error = Assert.ThrowsAsync<AggregateException>(() => machine.SwitchAsync(new Probe("bad", events)
            {
                OnEnter = _ => throw new FormatException("enter"),
                OnExit = () => throw new InvalidOperationException("exit")
            }));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<FormatException>());
            Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
            Assert.That(machine.Current, Is.Null);
            await machine.StopAsync();
            await host.StopAsync();
        }

        [Test]
        public async Task StopCancelsInFlightEnterAndCannotBeReopened()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            var enter = machine.SwitchAsync(new Probe("pending", events) { OnEnter = token => Task.Delay(Timeout.Infinite, token) });
            var stop = machine.StopAsync();
            Assert.CatchAsync<OperationCanceledException>(async () => await enter.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(events, Is.EqualTo(new[] { "enter:pending", "exit:pending" }));
            Assert.That(machine.StopAsync(), Is.SameAs(stop));
            Assert.Throws<ObjectDisposedException>(() => machine.SwitchAsync(new Probe("late", events)));
            await host.StopAsync();
        }

        [Test]
        public void FlowModuleRunsInitialFlowTicksAndFinalExitWithHost()
        {
            var events = new List<string>();
            double elapsed = 0;
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            host.AddModule(new FlowModule(machine, new Probe("game", events) { OnTick = seconds => elapsed += seconds }));
            host.StartAsync().GetAwaiter().GetResult();
            host.Tick(0.1);
            host.PauseAsync().GetAwaiter().GetResult();
            host.Tick(1);
            host.StopAsync().GetAwaiter().GetResult();
            Assert.That(elapsed, Is.EqualTo(0.1));
            Assert.That(events, Is.EqualTo(new[] { "enter:game", "exit:game" }));
        }

        [Test]
        public async Task HostStopCancelsInitialFlowEntryWithoutHangingFlowModuleCleanup()
        {
            var events = new List<string>();
            var host = new GameHost();
            var machine = new FlowMachine(host.Context);
            host.AddModule(new FlowModule(machine, new Probe("loading", events) { OnEnter = token => Task.Delay(Timeout.Infinite, token) }));
            var start = host.StartAsync();
            var stop = host.StopAsync();
            Assert.CatchAsync<OperationCanceledException>(async () => await start.WaitAsync(TimeSpan.FromSeconds(3)));
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(events, Is.EqualTo(new[] { "enter:loading", "exit:loading" }));
            Assert.That(host.State, Is.EqualTo(GameHostState.Stopped));
        }
    }
}
