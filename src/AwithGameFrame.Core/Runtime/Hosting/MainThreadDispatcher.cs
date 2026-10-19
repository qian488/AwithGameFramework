using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Hosting
{
    /// <summary>Queues work for the creating thread. Pump it even while asynchronous startup is pending.</summary>
    public sealed class MainThreadDispatcher : IDisposable
    {
        private sealed class Work
        {
            public Action Action;
            public CancellationToken Token;
            public CancellationTokenRegistration Registration;
            public bool Cleanup;
            public long Sequence;
            public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object _sync = new object();
        private readonly Queue<Work> _queue = new Queue<Work>();
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly int _capacity;
        private bool _closed;
        private long _sequence;

        public MainThreadDispatcher(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public bool CheckAccess => Environment.CurrentManagedThreadId == _ownerThread;
        public int PendingCount { get { lock (_sync) return _queue.Count; } }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
            => Enqueue(action, cancellationToken, false);

        // Cleanup must survive session cancellation and a full application queue, or native resources can leak.
        internal Task InvokeCleanupAsync(Action action) => Enqueue(action, CancellationToken.None, true);

        private Task Enqueue(Action action, CancellationToken cancellationToken, bool cleanup)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            var work = new Work { Action = action, Token = cancellationToken, Cleanup = cleanup };
            lock (_sync)
            {
                if (_closed) return Task.FromException(new ObjectDisposedException(nameof(MainThreadDispatcher)));
                if (!cleanup && _queue.Count >= _capacity)
                    return Task.FromException(new InvalidOperationException($"Dispatcher queue capacity {_capacity} exceeded."));
                work.Registration = cancellationToken.Register(() => work.Completion.TrySetCanceled(cancellationToken));
                work.Sequence = ++_sequence;
                _queue.Enqueue(work);
            }
            return work.Completion.Task;
        }

        /// <summary>Budget counts dequeued items, including cancelled ones. Work added by a callback waits for a later pump.</summary>
        public int RunPending(int budget)
        {
            if (!CheckAccess) throw new InvalidOperationException("Dispatcher must be pumped on its creating thread.");
            if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
            int count;
            long cutoff;
            lock (_sync) { count = Math.Min(budget, _queue.Count); cutoff = _sequence; }
            int processed = 0;
            while (processed < count)
            {
                Work work;
                lock (_sync)
                {
                    if (_queue.Count == 0 || _queue.Peek().Sequence > cutoff) break;
                    work = _queue.Dequeue();
                }
                try
                {
                    if (!work.Completion.Task.IsCompleted)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        work.Action();
                        work.Completion.TrySetResult(true);
                    }
                }
                catch (OperationCanceledException ex) { work.Completion.TrySetCanceled(ex.CancellationToken); }
                catch (Exception ex) { work.Completion.TrySetException(ex); }
                finally { work.Registration.Dispose(); }
                processed++;
            }
            return processed;
        }

        /// <summary>Cancels existing waiters while leaving the queue available for asynchronous cleanup work.</summary>
        public void CancelPending()
        {
            CancelQueued(false);
        }

        public void Dispose()
        {
            CancelQueued(true);
        }

        private void CancelQueued(bool close)
        {
            Work[] pending;
            lock (_sync)
            {
                if (_closed) return;
                _closed = close;
                var cancelled = new List<Work>();
                int count = _queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var work = _queue.Dequeue();
                    if (!close && work.Cleanup) _queue.Enqueue(work);
                    else cancelled.Add(work);
                }
                pending = cancelled.ToArray();
            }
            foreach (var work in pending)
            {
                work.Completion.TrySetCanceled();
                work.Registration.Dispose();
            }
        }
    }
}
