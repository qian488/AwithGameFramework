using System;
using System.Collections.Generic;
using System.Threading;

namespace AwithGameFrame.Core.Events
{
    /// <summary>The result of one synchronous publication. Listener failures do not stop other listeners.</summary>
    public sealed class EventDispatchResult
    {
        internal EventDispatchResult(int invocationCount, List<Exception> errors)
        {
            InvocationCount = invocationCount;
            Errors = errors == null ? Array.Empty<Exception>() : errors.AsReadOnly();
        }

        public int InvocationCount { get; }
        public IReadOnlyList<Exception> Errors { get; }
        public bool IsSuccess => Errors.Count == 0;

        public void ThrowIfFailed()
        {
            if (!IsSuccess) throw new AggregateException("Event listeners reported errors.", Errors);
        }
    }

    /// <summary>
    /// An instance-local, strongly typed event bus. Callbacks run synchronously on the publishing thread.
    /// Publications capture a snapshot; changes, cancellation and disposal affect later publications.
    /// Concurrent publications may invoke the same listener concurrently. No thread is created or dispatched to.
    /// </summary>
    public sealed class EventBus : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Dictionary<Type, List<Subscription>> _subscriptions = new Dictionary<Type, List<Subscription>>();
        private readonly HashSet<EventScope> _scopes = new HashSet<EventScope>();
        private bool _disposed;
        private int _count;

        public bool IsDisposed { get { lock (_sync) return _disposed; } }
        public int SubscriptionCount { get { lock (_sync) return _count; } }

        public IDisposable Subscribe<T>(Action<T> listener, CancellationToken cancellationToken = default)
        {
            return Subscribe(listener, null, cancellationToken);
        }

        internal IDisposable Subscribe<T>(Action<T> listener, EventScope scope, CancellationToken cancellationToken)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var subscription = new TypedSubscription<T>(this, _sync, scope, listener);
            lock (_sync)
            {
                ThrowIfDisposed();
                if (scope != null && !scope.IsOpenLocked) throw new ObjectDisposedException(nameof(EventScope));
                if (!_subscriptions.TryGetValue(typeof(T), out var listeners))
                {
                    listeners = new List<Subscription>();
                    _subscriptions.Add(typeof(T), listeners);
                }
                listeners.Add(subscription);
                scope?.Subscriptions.Add(subscription);
                _count++;
            }
            try
            {
                if (cancellationToken.CanBeCanceled)
                    subscription.Attach(cancellationToken.Register(state => ((Subscription)state).Dispose(), subscription));
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
            return subscription;
        }

        public EventScope CreateScope(CancellationToken cancellationToken = default)
        {
            EventScope scope;
            lock (_sync)
            {
                ThrowIfDisposed();
                scope = new EventScope(this, _sync);
                _scopes.Add(scope);
            }
            try
            {
                if (cancellationToken.CanBeCanceled)
                    scope.Attach(cancellationToken.Register(state => ((EventScope)state).Dispose(), scope));
            }
            catch
            {
                scope.Dispose();
                throw;
            }
            return scope;
        }

        /// <summary>Publishes to exactly T, in subscription order; base types/interfaces are not inferred.</summary>
        public EventDispatchResult Publish<T>(T message)
        {
            Action<T>[] snapshot;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_subscriptions.TryGetValue(typeof(T), out var listeners))
                    return new EventDispatchResult(0, null);
                snapshot = new Action<T>[listeners.Count];
                for (int i = 0; i < listeners.Count; i++)
                    snapshot[i] = ((TypedSubscription<T>)listeners[i]).Listener;
            }
            List<Exception> errors = null;
            foreach (var listener in snapshot)
            {
                try { listener(message); }
                catch (Exception error)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(error);
                }
            }
            return new EventDispatchResult(snapshot.Length, errors);
        }

        public void Dispose()
        {
            var registrations = new List<CancellationTokenRegistration>();
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var listeners in _subscriptions.Values)
                    foreach (var subscription in listeners) registrations.Add(subscription.DetachLocked());
                _subscriptions.Clear();
                _count = 0;
                foreach (var scope in _scopes) registrations.Add(scope.DetachLocked());
                _scopes.Clear();
            }
            foreach (var registration in registrations) registration.Dispose();
        }

        internal void CloseScope(EventScope scope)
        {
            var registrations = new List<CancellationTokenRegistration>();
            lock (_sync)
            {
                if (!scope.IsOpenLocked) return;
                foreach (var subscription in new List<Subscription>(scope.Subscriptions))
                    registrations.Add(RemoveLocked(subscription));
                _scopes.Remove(scope);
                registrations.Add(scope.DetachLocked());
            }
            foreach (var registration in registrations) registration.Dispose();
        }

        private void Remove(Subscription subscription)
        {
            CancellationTokenRegistration registration;
            lock (_sync)
            {
                if (!subscription.IsActive) return;
                registration = RemoveLocked(subscription);
            }
            registration.Dispose();
        }

        private CancellationTokenRegistration RemoveLocked(Subscription subscription)
        {
            var listeners = _subscriptions[subscription.MessageType];
            listeners.Remove(subscription);
            if (listeners.Count == 0) _subscriptions.Remove(subscription.MessageType);
            subscription.Scope?.Subscriptions.Remove(subscription);
            _count--;
            return subscription.DetachLocked();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));
        }

        internal abstract class Subscription : IDisposable
        {
            private readonly object _sync;
            private EventBus _owner;
            private CancellationTokenRegistration _registration;

            protected Subscription(EventBus owner, object sync, EventScope scope)
            {
                _owner = owner;
                _sync = sync;
                Scope = scope;
            }

            internal bool IsActive => _owner != null;
            internal EventScope Scope { get; private set; }
            internal abstract Type MessageType { get; }
            protected abstract void ClearListener();

            public void Dispose()
            {
                EventBus owner;
                lock (_sync) owner = _owner;
                owner?.Remove(this);
            }

            internal void Attach(CancellationTokenRegistration registration)
            {
                bool detached;
                lock (_sync)
                {
                    detached = !IsActive;
                    if (!detached) _registration = registration;
                }
                if (detached) registration.Dispose();
            }

            internal CancellationTokenRegistration DetachLocked()
            {
                _owner = null;
                Scope = null;
                ClearListener();
                var registration = _registration;
                _registration = default;
                return registration;
            }
        }

        private sealed class TypedSubscription<T> : Subscription
        {
            internal TypedSubscription(EventBus owner, object sync, EventScope scope, Action<T> listener)
                : base(owner, sync, scope) { Listener = listener; }

            internal Action<T> Listener { get; private set; }
            internal override Type MessageType => typeof(T);
            protected override void ClearListener() { Listener = null; }
        }
    }

    /// <summary>A group of subscriptions, removed together by disposal or its optional lifetime token.</summary>
    public sealed class EventScope : IDisposable
    {
        private readonly object _sync;
        private EventBus _bus;
        private CancellationTokenRegistration _registration;

        internal EventScope(EventBus bus, object sync) { _bus = bus; _sync = sync; }
        internal HashSet<EventBus.Subscription> Subscriptions { get; } = new HashSet<EventBus.Subscription>();
        internal bool IsOpenLocked => _bus != null;

        public bool IsDisposed { get { lock (_sync) return !IsOpenLocked; } }
        public int SubscriptionCount { get { lock (_sync) return Subscriptions.Count; } }

        public IDisposable Subscribe<T>(Action<T> listener, CancellationToken cancellationToken = default)
        {
            EventBus bus;
            lock (_sync) bus = _bus;
            if (bus == null) throw new ObjectDisposedException(nameof(EventScope));
            return bus.Subscribe(listener, this, cancellationToken);
        }

        public void Dispose()
        {
            EventBus bus;
            lock (_sync) bus = _bus;
            bus?.CloseScope(this);
        }

        internal void Attach(CancellationTokenRegistration registration)
        {
            bool detached;
            lock (_sync)
            {
                detached = !IsOpenLocked;
                if (!detached) _registration = registration;
            }
            if (detached) registration.Dispose();
        }

        internal CancellationTokenRegistration DetachLocked()
        {
            _bus = null;
            Subscriptions.Clear();
            var registration = _registration;
            _registration = default;
            return registration;
        }
    }
}
