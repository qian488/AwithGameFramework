using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Timing
{
    /// <summary>Host-thread timers in game seconds, with cancellation, bounded callbacks and skipped repeat catch-up.</summary>
    public sealed class TimerScheduler : IDisposable
    {
        internal enum EntryState { Scheduled, Executing, Completed, Cancelled, Faulted }

        internal sealed class Entry
        {
            internal long Order;
            internal long ReadyOrder;
            internal double Due, Interval;
            internal GameTimeDomain Domain;
            internal Action Callback;
            internal EntryState State;
            internal CancellationTokenRegistration Registration;
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object sync = new object();
        private readonly GameClock clock;
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly CancellationToken lifetimeToken;
        private CancellationTokenRegistration lifetimeRegistration;
        private long nextOrder;
        private long nextReadyOrder;
        private bool disposed;
        private bool ticking;

        internal TimerScheduler(GameClock clock, int callbackBudgetPerTick, CancellationToken lifetimeToken)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (callbackBudgetPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(callbackBudgetPerTick));
            CallbackBudgetPerTick = callbackBudgetPerTick;
            this.lifetimeToken = lifetimeToken;
            lifetimeRegistration = lifetimeToken.Register(Dispose);
            if (IsDisposed) lifetimeRegistration.Dispose();
        }

        public int CallbackBudgetPerTick { get; }
        public int PendingCount { get { lock (sync) return entries.Count; } }
        public bool IsDisposed { get { lock (sync) return disposed; } }

        public TimerHandle Schedule(double delaySeconds, Action callback, GameTimeDomain domain = GameTimeDomain.Scaled, CancellationToken cancellationToken = default) =>
            Add(delaySeconds, 0, callback, domain, cancellationToken);

        /// <summary>At most once per tick. Missed repeats are skipped; the next deadline is now + interval.</summary>
        public TimerHandle Repeat(double intervalSeconds, Action callback, GameTimeDomain domain = GameTimeDomain.Scaled, CancellationToken cancellationToken = default)
        {
            if (!GameClock.IsFinite(intervalSeconds) || intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            return Add(intervalSeconds, intervalSeconds, callback, domain, cancellationToken);
        }

        public Task DelayAsync(double delaySeconds, GameTimeDomain domain = GameTimeDomain.Scaled, CancellationToken cancellationToken = default) =>
            Schedule(delaySeconds, () => { }, domain, cancellationToken).Completion;

        private TimerHandle Add(double delaySeconds, double interval, Action callback, GameTimeDomain domain, CancellationToken cancellationToken)
        {
            CheckAccess();
            if (!GameClock.IsFinite(delaySeconds) || delaySeconds < 0) throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (domain != GameTimeDomain.Scaled && domain != GameTimeDomain.Unscaled) throw new ArgumentOutOfRangeException(nameof(domain));
            Entry entry;
            TimerHandle handle;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TimerScheduler));
                var due = clock.GetTime(domain) + delaySeconds;
                if (!GameClock.IsFinite(due)) throw new ArgumentOutOfRangeException(nameof(delaySeconds), "The deadline exceeds the game clock's numeric range.");
                entry = new Entry { Order = checked(++nextOrder), Due = due, Interval = interval, Callback = callback, Domain = domain };
                handle = new TimerHandle(this, entry);
                if (cancellationToken.IsCancellationRequested)
                {
                    entry.State = EntryState.Cancelled;
                    entry.Callback = null;
                    entry.Completion.TrySetCanceled(cancellationToken);
                    return handle;
                }
                entries.Add(entry.Order, entry);
            }
            if (cancellationToken.CanBeCanceled)
            {
                var registration = cancellationToken.Register(() => Cancel(entry, cancellationToken));
                bool release;
                lock (sync)
                {
                    release = entry.State != EntryState.Scheduled && entry.State != EntryState.Executing;
                    if (!release) entry.Registration = registration;
                }
                if (release) registration.Dispose();
            }
            return handle;
        }

        internal bool Cancel(Entry entry, CancellationToken cancellationToken)
        {
            CancellationTokenRegistration registration;
            lock (sync)
            {
                if (entry.State != EntryState.Scheduled && !(entry.State == EntryState.Executing && entry.Interval > 0)) return false;
                entries.Remove(entry.Order);
                entry.State = EntryState.Cancelled;
                entry.Callback = null;
                entry.Completion.TrySetCanceled(cancellationToken);
                registration = entry.Registration;
                entry.Registration = default;
            }
            registration.Dispose();
            return true;
        }

        internal void RunDue(Func<bool> canContinue)
        {
            CheckAccess();
            Entry[] due;
            lock (sync)
            {
                if (disposed) return;
                if (ticking) throw new InvalidOperationException("Timer callbacks cannot recursively run the scheduler.");
                ticking = true;
                // A fixed snapshot also keeps callbacks added by callbacks out of this tick.
                due = entries.Values.Where(e => e.State == EntryState.Scheduled && e.Due <= clock.GetTime(e.Domain))
                    .OrderBy(e => e.Due).ThenBy(e => e.Order).ToArray();
                // Deadlines from scaled and unscaled clocks are not comparable. Preserve a due backlog
                // ahead of newly eligible repeats so a small budget cannot starve another time domain.
                foreach (var entry in due) if (entry.ReadyOrder == 0) entry.ReadyOrder = checked(++nextReadyOrder);
                Array.Sort(due, (left, right) => left.ReadyOrder.CompareTo(right.ReadyOrder));
            }
            List<Exception> errors = null;
            int invoked = 0;
            try
            {
                foreach (var entry in due)
                {
                    if (invoked >= CallbackBudgetPerTick || !canContinue()) break;
                    Action callback;
                    lock (sync)
                    {
                        if (disposed) break;
                        if (entry.State != EntryState.Scheduled) continue;
                        entry.State = EntryState.Executing;
                        callback = entry.Callback;
                    }
                    invoked++;
                    Exception failure = null;
                    try { callback(); }
                    catch (Exception exception) { failure = exception; }
                    CancellationTokenRegistration registration = default;
                    lock (sync)
                    {
                        if (entry.State == EntryState.Executing)
                        {
                            if (failure == null && entry.Interval > 0 && !disposed)
                            {
                                var nextDue = clock.GetTime(entry.Domain) + entry.Interval;
                                if (GameClock.IsFinite(nextDue))
                                {
                                    entry.Due = nextDue;
                                    entry.ReadyOrder = 0;
                                    entry.State = EntryState.Scheduled;
                                }
                                else failure = new InvalidOperationException("The repeating deadline exceeds the game clock's numeric range.");
                            }
                            if (entry.State == EntryState.Executing)
                            {
                                entries.Remove(entry.Order);
                                entry.Callback = null;
                                entry.State = failure == null ? EntryState.Completed : EntryState.Faulted;
                                if (failure == null) entry.Completion.TrySetResult(true);
                                else
                                {
                                    entry.Completion.TrySetException(failure);
                                    _ = entry.Completion.Task.Exception; // Tick already reports callback errors; awaiting Completion still throws.
                                }
                                registration = entry.Registration;
                                entry.Registration = default;
                            }
                        }
                    }
                    registration.Dispose();
                    if (failure != null) (errors ??= new List<Exception>()).Add(failure);
                }
            }
            finally { lock (sync) ticking = false; }
            if (errors != null) throw new AggregateException("One or more timer callbacks failed.", errors);
        }

        public void Dispose()
        {
            var registrations = new List<CancellationTokenRegistration>();
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                foreach (var entry in entries.Values.ToArray())
                {
                    // A one-shot callback that already started is allowed to finish.
                    if (entry.State == EntryState.Executing && entry.Interval == 0) continue;
                    entries.Remove(entry.Order);
                    entry.State = EntryState.Cancelled;
                    entry.Callback = null;
                    entry.Completion.TrySetCanceled(lifetimeToken.IsCancellationRequested ? lifetimeToken : default);
                    registrations.Add(entry.Registration);
                    entry.Registration = default;
                }
            }
            foreach (var registration in registrations) registration.Dispose();
            lifetimeRegistration.Dispose();
        }

        private void CheckAccess()
        {
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Schedule and run timers on their host thread; use the dispatcher for worker requests.");
        }
    }
}
