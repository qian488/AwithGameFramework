using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Resources
{
    /// <summary>Owns the leases for one scene, window or flow, including loads that are still pending.</summary>
    public sealed class ResourceScope : IAsyncDisposable
    {
        private readonly object _sync = new object();
        private readonly ResourceStore _store;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly List<IAsyncDisposable> _leases = new List<IAsyncDisposable>();
        private readonly TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _closing;
        private int _pending;
        private Task _disposeTask;

        public ResourceScope(ResourceStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

        public async Task<ResourceLease<T>> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            CancellationTokenSource linked;
            lock (_sync)
            {
                if (_closing) throw new ObjectDisposedException(nameof(ResourceScope));
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
                _pending++;
            }
            try
            {
                var lease = await _store.LoadAsync<T>(key, linked.Token);
                lock (_sync)
                {
                    if (!_closing)
                    {
                        _leases.Add(lease);
                        return lease;
                    }
                }
                await lease.DisposeAsync();
                throw new ObjectDisposedException(nameof(ResourceScope));
            }
            finally
            {
                linked.Dispose();
                lock (_sync)
                {
                    _pending--;
                    if (_closing && _pending == 0) _drained.TrySetResult(true);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (_sync)
            {
                if (_disposeTask != null) return new ValueTask(_disposeTask);
                _closing = true;
                if (_pending == 0) _drained.TrySetResult(true);
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }
            _ = CompleteDisposeAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task CompleteDisposeAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                try { _cancellation.Cancel(); }
                catch (Exception ex) { errors.Add(ex); }
                await _drained.Task;
                IAsyncDisposable[] leases;
                lock (_sync) { leases = _leases.ToArray(); _leases.Clear(); }
                for (int i = leases.Length - 1; i >= 0; i--)
                {
                    try { await leases[i].DisposeAsync(); }
                    catch (Exception ex) { errors.Add(ex); }
                }
                _cancellation.Dispose();
                if (errors.Count == 0) completion.TrySetResult(true);
                else completion.TrySetException(new AggregateException("Resource scope cleanup reported errors.", errors));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }
    }
}
