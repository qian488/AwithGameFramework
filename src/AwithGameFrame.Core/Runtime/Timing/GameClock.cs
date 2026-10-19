using System;

namespace AwithGameFrame.Core.Timing
{
    /// <summary>A host-owned game clock, advanced with unscaled seconds only while the host runs.</summary>
    public sealed class GameClock
    {
        private readonly object sync = new object();
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private double timeScale = 1;
        private double scaledTime, unscaledTime, scaledDelta, unscaledDelta;
        private bool stopped;

        internal GameClock() { }

        public double ScaledTime { get { lock (sync) return scaledTime; } }
        public double UnscaledTime { get { lock (sync) return unscaledTime; } }
        public double ScaledDeltaTime { get { lock (sync) return scaledDelta; } }
        public double UnscaledDeltaTime { get { lock (sync) return unscaledDelta; } }

        public double TimeScale
        {
            get { lock (sync) return timeScale; }
            set
            {
                if (Environment.CurrentManagedThreadId != ownerThread)
                    throw new InvalidOperationException("Change the game clock on its host thread.");
                if (!IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                lock (sync)
                {
                    if (stopped) throw new ObjectDisposedException(nameof(GameClock));
                    timeScale = value;
                }
            }
        }

        internal double GetTime(GameTimeDomain domain)
        {
            lock (sync) return domain == GameTimeDomain.Scaled ? scaledTime : unscaledTime;
        }

        internal void Advance(double unscaledSeconds)
        {
            lock (sync)
            {
                if (stopped) return;
                var scaledSeconds = unscaledSeconds * timeScale;
                var nextScaled = scaledTime + scaledSeconds;
                var nextUnscaled = unscaledTime + unscaledSeconds;
                if (!IsFinite(scaledSeconds) || !IsFinite(nextScaled) || !IsFinite(nextUnscaled))
                    throw new ArgumentOutOfRangeException(nameof(unscaledSeconds), "The tick exceeds the game clock's numeric range.");
                scaledDelta = scaledSeconds;
                unscaledDelta = unscaledSeconds;
                scaledTime = nextScaled;
                unscaledTime = nextUnscaled;
            }
        }

        internal void Freeze()
        {
            lock (sync) scaledDelta = unscaledDelta = 0;
        }

        internal void Stop()
        {
            lock (sync)
            {
                stopped = true;
                scaledDelta = unscaledDelta = 0;
            }
        }

        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
