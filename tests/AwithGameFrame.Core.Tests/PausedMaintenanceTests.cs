using System;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Timing;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class PausedMaintenanceTests
    {
        private sealed class Module : GameModule, IGameMaintenanceModule
        {
            private readonly string id;
            public int Ticks, Maintenance;
            public bool Fail;
            public Module(string id) => this.id = id;
            public override string Id => id;
            public override void Tick(GameContext context, double deltaSeconds) => Ticks++;
            public void Maintain(GameContext context)
            {
                Maintenance++;
                if (Fail) throw new FormatException("cleanup");
            }
        }

        [Test]
        public void PauseFreezesSimulationAndTimersWhileMaintenanceFailuresDoNotSkipOtherModules()
        {
            var host = new GameHost();
            var first = new Module("first"); var second = new Module("second");
            host.AddModule(first); host.AddModule(second);
            host.StartAsync().GetAwaiter().GetResult();
            host.Tick(0.25);
            Assert.That(first.Ticks, Is.EqualTo(1));
            Assert.That(first.Maintenance, Is.Zero);
            int timers = 0;
            host.Timers.Schedule(0.25, () => timers++);
            host.Timers.Schedule(0.25, () => timers++, GameTimeDomain.Unscaled);
            host.PauseAsync().GetAwaiter().GetResult();
            first.Fail = true;
            var failure = Assert.Throws<AggregateException>(() => host.Tick(5));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<FormatException>());
            Assert.That(first.Ticks, Is.EqualTo(1));
            Assert.That(second.Ticks, Is.EqualTo(1));
            Assert.That(first.Maintenance, Is.EqualTo(1));
            Assert.That(second.Maintenance, Is.EqualTo(1));
            Assert.That(host.Clock.ScaledTime, Is.EqualTo(0.25));
            Assert.That(host.Clock.UnscaledTime, Is.EqualTo(0.25));
            Assert.That(host.Clock.ScaledDeltaTime, Is.Zero);
            Assert.That(host.Clock.UnscaledDeltaTime, Is.Zero);
            Assert.That(timers, Is.Zero);
            host.ResumeAsync().GetAwaiter().GetResult();
            host.Tick(0.5);
            Assert.That(first.Ticks, Is.EqualTo(2));
            Assert.That(first.Maintenance, Is.EqualTo(1));
            Assert.That(timers, Is.EqualTo(2));
            host.StopAsync().GetAwaiter().GetResult();
            host.Tick(1);
            Assert.That(first.Maintenance, Is.EqualTo(1));
        }
    }
}
