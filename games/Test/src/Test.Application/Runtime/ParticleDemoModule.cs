using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Timing;

namespace Test.Application
{
    public sealed class ParticleDemoModule : GameModule
    {
        private readonly double repeatSeconds;
        private readonly Action requestPlayback;
        private TimerHandle firstPlayback;
        private TimerHandle repeatedPlayback;

        public ParticleDemoModule(double repeatSeconds, Action requestPlayback)
        {
            if (double.IsNaN(repeatSeconds) || double.IsInfinity(repeatSeconds) || repeatSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(repeatSeconds));

            this.repeatSeconds = repeatSeconds;
            this.requestPlayback = requestPlayback ?? throw new ArgumentNullException(nameof(requestPlayback));
        }

        public override string Id => "Test.particle-demo";

        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            firstPlayback = context.Timers.Schedule(0, requestPlayback, cancellationToken: context.LifetimeToken);
            repeatedPlayback = context.Timers.Repeat(repeatSeconds, requestPlayback, cancellationToken: context.LifetimeToken);
            return Task.CompletedTask;
        }

        public override Task StopAsync(GameContext context, CancellationToken cancellationToken)
        {
            firstPlayback?.Cancel();
            repeatedPlayback?.Cancel();
            return Task.CompletedTask;
        }
    }
}
