using System;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Timing
{
    /// <summary>Owns one scheduled timer. Dispose cancels pending work; observe Completion for failures.</summary>
    public sealed class TimerHandle : IDisposable
    {
        private readonly TimerScheduler scheduler;
        private readonly TimerScheduler.Entry entry;

        internal TimerHandle(TimerScheduler scheduler, TimerScheduler.Entry entry)
        {
            this.scheduler = scheduler;
            this.entry = entry;
        }

        public Task Completion => entry.Completion.Task;
        public bool IsCompleted => Completion.IsCompleted;

        /// <summary>A one-shot callback which has already started cannot be cancelled retroactively.</summary>
        public bool Cancel() => scheduler.Cancel(entry, default);
        public void Dispose() => Cancel();
    }
}
