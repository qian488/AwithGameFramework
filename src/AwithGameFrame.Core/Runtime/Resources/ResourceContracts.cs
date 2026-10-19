using System;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Resources
{
    public interface IResourceLoader
    {
        Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken);
    }

    /// <summary>A backend value and its real release operation; no engine types cross this contract.</summary>
    public sealed class LoadedResource
    {
        private readonly object _sync = new object();
        private readonly Func<Task> _release;
        private Task _releaseTask;

        public LoadedResource(object value, Func<Task> releaseAsync, long estimatedSizeBytes = 0)
        {
            if (estimatedSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedSizeBytes));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            _release = releaseAsync ?? throw new ArgumentNullException(nameof(releaseAsync));
            EstimatedSizeBytes = estimatedSizeBytes;
        }

        public object Value { get; }
        /// <summary>Backend estimate. Zero means unknown and is never retained in the idle cache.</summary>
        public long EstimatedSizeBytes { get; }

        internal Task ReleaseAsync()
        {
            lock (_sync)
            {
                if (_releaseTask != null) return _releaseTask;
                try { _releaseTask = _release() ?? Task.FromException(new InvalidOperationException("Resource release returned a null Task.")); }
                catch (Exception ex) { _releaseTask = Task.FromException(ex); }
                return _releaseTask;
            }
        }
    }

    public sealed class ResourceLease<T> : IAsyncDisposable where T : class
    {
        private readonly object _sync = new object();
        private readonly T _value;
        private readonly Func<bool> _valid;
        private readonly Func<Task> _release;
        private Task _disposeTask;

        internal ResourceLease(string key, T value, Func<bool> valid, Func<Task> release)
        {
            Key = key;
            _value = value;
            _valid = valid;
            _release = release;
        }

        public string Key { get; }
        public T Value
        {
            get
            {
                lock (_sync)
                {
                    if (_disposeTask != null || !_valid()) throw new ObjectDisposedException(nameof(ResourceLease<T>));
                    return _value;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposeTask == null) _disposeTask = _release();
                return new ValueTask(_disposeTask);
            }
        }
    }
}
