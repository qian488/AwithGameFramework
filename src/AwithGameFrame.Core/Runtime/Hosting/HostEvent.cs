using System;

namespace AwithGameFrame.Core.Hosting
{
    public enum HostPhase { Start, Pause, Resume, Stop }
    public enum HostEventStatus { Started, Completed, Failed }

    /// <summary>Instance lifecycle diagnostics. ModuleId is null for a host operation.</summary>
    public sealed class HostEvent
    {
        internal HostEvent(string hostId, HostPhase phase, HostEventStatus status, string moduleId,
            double elapsedSeconds, GameHostState state, Exception error, bool isRollback)
        {
            HostId = hostId;
            Phase = phase;
            Status = status;
            ModuleId = moduleId;
            ElapsedSeconds = elapsedSeconds;
            State = state;
            Error = error;
            IsRollback = isRollback;
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public string HostId { get; }
        public HostPhase Phase { get; }
        public HostEventStatus Status { get; }
        public string ModuleId { get; }
        public double ElapsedSeconds { get; }
        public DateTimeOffset TimestampUtc { get; }
        public GameHostState State { get; }
        public Exception Error { get; }
        public bool IsRollback { get; }
    }
}
