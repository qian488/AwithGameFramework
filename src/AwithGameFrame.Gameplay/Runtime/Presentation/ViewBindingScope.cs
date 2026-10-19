using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>
    /// Explicit, reflection-free bindings owned by a view. Bind, source notifications and explicit Dispose
    /// must run on the creating host thread. Getter/setter failures propagate to the caller or event publisher.
    /// Cancellation only unsubscribes; a source's event removal must support the thread cancelling its token.
    /// </summary>
    public sealed class ViewBindingScope : IDisposable
    {
        private readonly object sync = new object();
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly List<Binding> bindings = new List<Binding>();
        private CancellationTokenRegistration cancellation;
        private bool disposed;

        public ViewBindingScope(CancellationToken cancellationToken = default)
        { cancellation = cancellationToken.Register(DisposeCore); }

        /// <summary>Synchronizes immediately, then on the named property or an all-properties notification.</summary>
        public IDisposable Bind<T>(INotifyPropertyChanged source, string propertyName, Func<T> getter, Action<T> setter)
        {
            VerifyThread();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("A property name is required.", nameof(propertyName));
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ViewBindingScope));
                var binding = new Binding(this, source, propertyName, () => setter(getter()));
                bindings.Add(binding);
                try { binding.Attach(); binding.Refresh(); return binding; }
                catch (Exception failure)
                {
                    bindings.Remove(binding);
                    try { binding.Detach(); }
                    catch (Exception cleanup) { throw new AggregateException("Binding initialization and unsubscription failed.", failure, cleanup); }
                    throw;
                }
            }
        }

        public void Dispose()
        {
            VerifyThread();
            try { DisposeCore(); }
            finally { cancellation.Dispose(); }
        }

        private void DisposeCore()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                var errors = new List<Exception>();
                foreach (var binding in bindings)
                { try { binding.Detach(); } catch (Exception error) { errors.Add(error); } }
                bindings.Clear();
                if (errors.Count != 0) throw new AggregateException("View binding unsubscription failed.", errors);
            }
        }

        private void Remove(Binding binding)
        {
            VerifyThread();
            lock (sync) { bindings.Remove(binding); binding.Detach(); }
        }

        private void OnChanged(Binding binding, string propertyName)
        {
            lock (sync)
            {
                if (disposed || binding.IsDetached) return;
                VerifyThread();
                if (string.IsNullOrEmpty(propertyName) || string.Equals(propertyName, binding.PropertyName, StringComparison.Ordinal)) binding.Refresh();
            }
        }

        private void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("View bindings must be created, updated and explicitly disposed on their creating host thread. Dispatch model updates through the host dispatcher.");
        }

        private sealed class Binding : IDisposable
        {
            private readonly ViewBindingScope owner;
            private readonly INotifyPropertyChanged source;
            private readonly Action refresh;
            public Binding(ViewBindingScope owner, INotifyPropertyChanged source, string propertyName, Action refresh)
            { this.owner = owner; this.source = source; PropertyName = propertyName; this.refresh = refresh; }
            public string PropertyName { get; }
            public bool IsDetached { get; private set; }
            public void Attach() => source.PropertyChanged += Changed;
            public void Refresh() => refresh();
            public void Detach() { if (IsDetached) return; IsDetached = true; source.PropertyChanged -= Changed; }
            private void Changed(object sender, PropertyChangedEventArgs args) => owner.OnChanged(this, args?.PropertyName);
            public void Dispose() => owner.Remove(this);
        }
    }

    /// <summary>A small model value for explicit UI binding; publish changes on the owning host thread.</summary>
    public sealed class ObservableValue<T> : INotifyPropertyChanged
    {
        private T value;
        public ObservableValue(T initialValue = default) => value = initialValue;
        public T Value
        {
            get => value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
                this.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
