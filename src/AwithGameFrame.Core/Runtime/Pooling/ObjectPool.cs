using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AwithGameFrame.Core.Pooling
{
    /// <summary>
    /// A bounded, reference-identity pool. All active operations belong to its creating thread.
    /// Factory and lifecycle callbacks cannot reenter pool mutations. Capacity includes rented values.
    /// </summary>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Func<T> _factory;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _destroy;
        private readonly Dictionary<T, Entry> _owned = new Dictionary<T, Entry>(ReferenceComparer.Instance);
        private readonly Stack<Entry> _available = new Stack<Entry>();
        private readonly ConditionalWeakTable<T, object> _issued = new ConditionalWeakTable<T, object>();
        private bool _busy;
        private bool _disposed;

        public ObjectPool(Func<T> factory, int capacity = 128, Action<T> onRent = null,
            Action<T> onReturn = null, Action<T> destroy = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _onRent = onRent;
            _onReturn = onReturn;
            _destroy = destroy ?? (value => (value as IDisposable)?.Dispose());
        }

        public int Capacity { get; }
        public int CountAll { get { VerifyThread(); return _owned.Count; } }
        public int CountAvailable { get { VerifyThread(); return _available.Count; } }
        public int CountRented { get { VerifyThread(); return _owned.Count - _available.Count; } }
        public bool IsDisposed { get { VerifyThread(); return _disposed; } }

        public T Rent()
        {
            Enter();
            try { return Checkout().Value; }
            finally { _busy = false; }
        }

        public PoolLease<T> RentLease()
        {
            Enter();
            try
            {
                var entry = Checkout();
                var lease = new PoolLease<T>(this, entry);
                entry.Lease = lease;
                return lease;
            }
            finally { _busy = false; }
        }

        /// <summary>Returns a raw Rent value. Values obtained through RentLease must use their lease.</summary>
        public void Return(T value)
        {
            Enter();
            try
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (!_owned.TryGetValue(value, out var entry))
                    throw new ArgumentException("This pool does not own the supplied reference.", nameof(value));
                if (!entry.Rented) throw new InvalidOperationException("The value has already been returned.");
                if (entry.Lease != null) throw new InvalidOperationException("Return this value by disposing its lease.");
                ReturnEntry(entry);
            }
            finally { _busy = false; }
        }

        /// <summary>Ensures at least availableCount idle values; a failed batch rolls back only its new values.</summary>
        public void Prewarm(int availableCount)
        {
            Enter();
            var created = new List<Entry>();
            try
            {
                if (availableCount < 0 || availableCount > Capacity)
                    throw new ArgumentOutOfRangeException(nameof(availableCount));
                int needed = Math.Max(0, availableCount - _available.Count);
                if (needed > Capacity - _owned.Count)
                    throw new InvalidOperationException("Prewarm would exceed pool capacity, including rented values.");
                try
                {
                    for (int i = 0; i < needed; i++) created.Add(CreateEntry());
                }
                catch (Exception failure)
                {
                    var errors = new List<Exception> { failure };
                    foreach (var entry in created) DestroyEntry(entry, errors);
                    if (errors.Count > 1) throw new AggregateException("Prewarm failed and rollback reported errors.", errors);
                    throw;
                }
                foreach (var entry in created) _available.Push(entry);
            }
            finally { _busy = false; }
        }

        public void Dispose()
        {
            VerifyThread();
            if (_busy) throw new InvalidOperationException("Pool callbacks cannot reenter pool operations.");
            if (_disposed) return;
            _busy = true;
            _disposed = true;
            var entries = new List<Entry>(_owned.Values);
            var errors = new List<Exception>();
            _owned.Clear();
            _available.Clear();
            try
            {
                foreach (var entry in entries) DestroyEntry(entry, errors);
                if (errors.Count != 0) throw new AggregateException("Pool destruction reported errors.", errors);
            }
            finally { _busy = false; }
        }

        internal T GetValue(PoolLease<T> lease, Entry entry)
        {
            VerifyThread();
            if (_disposed || entry == null || !entry.Rented || !ReferenceEquals(entry.Lease, lease))
                throw new ObjectDisposedException(nameof(PoolLease<T>));
            return entry.Value;
        }

        internal void ReturnLease(PoolLease<T> lease, Entry entry)
        {
            Enter();
            try
            {
                if (!ReferenceEquals(entry.Lease, lease)) throw new InvalidOperationException("Lease ownership is invalid.");
                entry.Lease = null;
                lease.Detach();
                ReturnEntry(entry);
            }
            finally { _busy = false; }
        }

        private Entry Checkout()
        {
            Entry entry;
            if (_available.Count != 0) entry = _available.Pop();
            else
            {
                if (_owned.Count >= Capacity) throw new InvalidOperationException("The pool is at capacity; return a rented value first.");
                entry = CreateEntry();
            }
            entry.Rented = true;
            try { _onRent?.Invoke(entry.Value); }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                DestroyEntry(entry, errors);
                if (errors.Count > 1) throw new AggregateException("Rent failed and destruction reported errors.", errors);
                throw;
            }
            return entry;
        }

        private Entry CreateEntry()
        {
            var value = _factory();
            if (value == null) throw new InvalidOperationException("The pool factory returned null.");
            // Weak identity tracking also rejects a factory handing out an already destroyed reference,
            // without retaining destroyed objects for the remaining lifetime of the pool.
            if (_issued.TryGetValue(value, out _))
                throw new InvalidOperationException("The pool factory returned a reference it has already supplied.");
            _issued.Add(value, new object());
            var entry = new Entry { Value = value };
            _owned.Add(value, entry);
            return entry;
        }

        private void ReturnEntry(Entry entry)
        {
            entry.Rented = false;
            try { _onReturn?.Invoke(entry.Value); }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                DestroyEntry(entry, errors);
                if (errors.Count > 1) throw new AggregateException("Return failed and destruction reported errors.", errors);
                throw;
            }
            _available.Push(entry);
        }

        private void DestroyEntry(Entry entry, List<Exception> errors)
        {
            var value = entry.Value;
            _owned.Remove(value);
            entry.Value = null;
            entry.Rented = false;
            entry.Lease?.Detach();
            entry.Lease = null;
            try { _destroy(value); }
            catch (Exception error) { errors.Add(error); }
        }

        private void Enter()
        {
            VerifyThread();
            if (_busy) throw new InvalidOperationException("Pool callbacks cannot reenter pool operations.");
            if (_disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
            _busy = true;
        }

        private void VerifyThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new InvalidOperationException("Use the pool on its creating thread; dispatch explicitly at the host boundary.");
        }

        internal sealed class Entry
        {
            internal T Value;
            internal bool Rented;
            internal PoolLease<T> Lease;
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }

    /// <summary>An idempotent checkout handle. A failed return still consumes the lease and destroys its value.</summary>
    public sealed class PoolLease<T> : IDisposable where T : class
    {
        private ObjectPool<T> _pool;
        private ObjectPool<T>.Entry _entry;

        internal PoolLease(ObjectPool<T> pool, ObjectPool<T>.Entry entry) { _pool = pool; _entry = entry; }

        public T Value
        {
            get
            {
                var pool = _pool;
                if (pool == null) throw new ObjectDisposedException(nameof(PoolLease<T>));
                return pool.GetValue(this, _entry);
            }
        }

        public void Dispose()
        {
            var pool = _pool;
            pool?.ReturnLease(this, _entry);
        }

        internal void Detach() { _pool = null; _entry = null; }
    }
}
