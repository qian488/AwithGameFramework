using System;
using System.Collections.Generic;

namespace AwithGameFrame.Core.Hosting
{
    /// <summary>One host's explicitly registered services. Modules retain ownership of their resources.</summary>
    public sealed class GameServices
    {
        private readonly object _sync = new object();
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private bool _closed;

        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
            {
                if (_closed) throw new ObjectDisposedException(nameof(GameServices));
                if (_services.ContainsKey(typeof(T)))
                    throw new InvalidOperationException($"Service '{typeof(T).FullName}' is already registered in this host.");
                _services.Add(typeof(T), service);
            }
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new KeyNotFoundException($"Service '{typeof(T).FullName}' is not registered in this host.");
        }

        public bool TryGet<T>(out T service) where T : class
        {
            lock (_sync)
            {
                if (_services.TryGetValue(typeof(T), out var value))
                {
                    service = (T)value;
                    return true;
                }
                service = null;
                return false;
            }
        }

        internal void Close()
        {
            lock (_sync)
            {
                _closed = true;
                _services.Clear();
            }
        }
    }
}
