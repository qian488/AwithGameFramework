using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Test.Server
{
    /// <summary>Owned by the console host thread; posted await continuations run during Drain.</summary>
    internal sealed class HostSynchronizationContext : SynchronizationContext
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _pending =
            new ConcurrentQueue<(SendOrPostCallback, object)>();

        public override SynchronizationContext CreateCopy() => this;

        public override void Post(SendOrPostCallback callback, object state)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _pending.Enqueue((callback, state));
        }

        public override void Send(SendOrPostCallback callback, object state)
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new NotSupportedException("Use asynchronous dispatch to the host thread.");
            callback(state);
        }

        internal void Drain(int budget = 128)
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("Continuations must run on the host thread.");
            for (int i = 0; i < budget && _pending.TryDequeue(out var work); i++)
                work.Callback(work.State);
        }
    }
}
