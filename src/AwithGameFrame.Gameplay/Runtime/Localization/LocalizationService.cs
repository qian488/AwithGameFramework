using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Extensions.Localization
{
    public sealed class LanguageChanged
    {
        public LocalizationService Service { get; }
        public string PreviousLanguage { get; }
        public string Language { get; }
        internal LanguageChanged(LocalizationService service, string previous, string language)
        { Service = service; PreviousLanguage = previous; Language = language; }
    }

    public sealed class LocalizationCatalogChanged
    {
        public LocalizationService Service { get; }
        internal LocalizationCatalogChanged(LocalizationService service) { Service = service; }
    }

    /// <summary>Host-owned localization. Catalogs are copied, validated and published as a whole.</summary>
    public sealed class LocalizationService : IDisposable
    {
        private readonly object sync = new object();
        private readonly GameContext context;
        private readonly List<TextBinding> bindings = new List<TextBinding>();
        private Dictionary<string, Dictionary<string, string>> catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private string language;
        private bool disposed, publishing;

        public LocalizationService(GameContext context, string fallbackLanguage = "en")
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            if (!context.Dispatcher.CheckAccess) throw new InvalidOperationException("Create localization on the host thread.");
            FallbackLanguage = NormalizeLanguage(fallbackLanguage);
            language = FallbackLanguage;
        }

        internal GameContext Context => context;
        public string FallbackLanguage { get; }
        public string CurrentLanguage { get { lock (sync) return language; } }
        public bool IsReady { get { lock (sync) return !disposed && catalogs.ContainsKey(FallbackLanguage); } }
        public IReadOnlyList<string> AvailableLanguages
        {
            get { lock (sync) { ThrowIfDisposed(); var names = new List<string>(catalogs.Keys); names.Sort(StringComparer.Ordinal); return names.AsReadOnly(); } }
        }

        public bool IsLanguageSupported(string languageCode)
        {
            string normalized = NormalizeLanguage(languageCode);
            lock (sync)
            {
                ThrowIfDisposed();
                foreach (string candidate in Parents(normalized)) if (catalogs.ContainsKey(candidate)) return true;
                return false;
            }
        }

        public void ReplaceDatasets(IEnumerable<LocalizationDataset> datasets)
        {
            VerifyMutation();
            if (datasets == null) throw new ArgumentNullException(nameof(datasets));
            var next = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var dataset in datasets)
            {
                if (dataset == null || dataset.Entries == null) throw new ArgumentException("A localization dataset and its entries are required.", nameof(datasets));
                string code = NormalizeLanguage(dataset.LanguageCode);
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in dataset.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null || entries.ContainsKey(entry.Key))
                        throw new ArgumentException("Localization keys must be unique and nonempty; values cannot be null.", nameof(datasets));
                    entries.Add(entry.Key, entry.Value);
                }
                if (next.ContainsKey(code)) throw new ArgumentException("Localization language codes must be unique.", nameof(datasets));
                next.Add(code, entries);
            }
            if (!next.ContainsKey(FallbackLanguage)) throw new ArgumentException("The fallback language dataset is required.", nameof(datasets));
            string previous;
            lock (sync)
            {
                ThrowIfDisposed();
                previous = language;
                catalogs = next;
                if (!IsLanguageSupported(language)) language = FallbackLanguage;
            }
            Publish(() =>
            {
                var errors = new List<Exception>();
                if (previous != CurrentLanguage) errors.AddRange(context.Events.Publish(new LanguageChanged(this, previous, CurrentLanguage)).Errors);
                errors.AddRange(context.Events.Publish(new LocalizationCatalogChanged(this)).Errors);
                if (errors.Count != 0) throw new AggregateException("Localization listeners failed after catalog publication.", errors);
            });
        }

        public void SwitchLanguage(string languageCode)
        {
            VerifyMutation();
            string normalized = NormalizeLanguage(languageCode);
            string previous;
            lock (sync)
            {
                if (!IsLanguageSupported(normalized)) throw new ArgumentException("No dataset supports the requested language.", nameof(languageCode));
                previous = language;
                if (previous == normalized) return;
                language = normalized;
            }
            Publish(() => context.Events.Publish(new LanguageChanged(this, previous, normalized)).ThrowIfFailed());
        }

        public string GetText(string key, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A localization key is required.", nameof(key));
            lock (sync)
            {
                ThrowIfDisposed();
                foreach (string candidate in Parents(language))
                    if (catalogs.TryGetValue(candidate, out var entries) && entries.TryGetValue(key, out var text)) return text;
                foreach (string candidate in Parents(FallbackLanguage))
                    if (catalogs.TryGetValue(candidate, out var entries) && entries.TryGetValue(key, out var text)) return text;
                return fallback ?? key;
            }
        }

        public string GetFormat(string key, params object[] args)
        {
            CultureInfo culture;
            string format;
            lock (sync)
            {
                culture = CultureInfo.GetCultureInfo(language);
                format = GetText(key);
            }
            return string.Format(culture, format, args ?? Array.Empty<object>());
        }

        /// <summary>Initial synchronization plus language/catalog updates; dispose with the native view.</summary>
        public IDisposable BindText(string key, Action<string> setter, string fallback = null)
        {
            VerifyMutation();
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            var binding = new TextBinding(this, key, setter, fallback);
            lock (sync) bindings.Add(binding);
            try { binding.Attach(); Publish(binding.Refresh); return binding; }
            catch { binding.Dispose(); throw; }
        }

        public void Dispose()
        {
            TextBinding[] pending;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                pending = bindings.ToArray();
                bindings.Clear();
            }
            foreach (var binding in pending) binding.Dispose();
        }

        internal static string NormalizeLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code != code.Trim()) throw new ArgumentException("A nonempty culture name is required.", nameof(code));
            try
            {
                var culture = CultureInfo.GetCultureInfo(code);
                if (culture.Name.Length == 0) throw new ArgumentException("The invariant culture is not a language.", nameof(code));
                return culture.Name;
            }
            catch (CultureNotFoundException error) { throw new ArgumentException("The language is not a valid culture name.", nameof(code), error); }
        }

        private static IEnumerable<string> Parents(string code)
        {
            var culture = CultureInfo.GetCultureInfo(code);
            while (culture.Name.Length != 0) { yield return culture.Name; culture = culture.Parent; }
        }
        private void VerifyMutation()
        {
            if (!context.Dispatcher.CheckAccess) throw new InvalidOperationException("Change localization on the host thread.");
            lock (sync)
            {
                ThrowIfDisposed();
                context.LifetimeToken.ThrowIfCancellationRequested();
                if (publishing) throw new InvalidOperationException("Localization listeners cannot reenter mutations; dispatch the change for a later frame.");
            }
        }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(LocalizationService)); }
        private void Publish(Action publish)
        {
            lock (sync) publishing = true;
            try { publish(); }
            finally { lock (sync) publishing = false; }
        }

        private sealed class TextBinding : IDisposable
        {
            private readonly LocalizationService service;
            private readonly string key, fallback;
            private readonly Action<string> setter;
            private EventScope scope;
            private bool closed;
            internal TextBinding(LocalizationService service, string key, Action<string> setter, string fallback)
            { this.service = service; this.key = key; this.setter = setter; this.fallback = fallback; }
            internal void Attach()
            {
                scope = service.context.Events.CreateScope(service.context.LifetimeToken);
                scope.Subscribe<LanguageChanged>(message => { if (message.Service == service) Refresh(); });
                scope.Subscribe<LocalizationCatalogChanged>(message => { if (message.Service == service) Refresh(); });
            }
            internal void Refresh()
            {
                lock (service.sync)
                {
                    if (closed || service.disposed || service.context.LifetimeToken.IsCancellationRequested) return;
                    setter(service.GetText(key, fallback));
                }
            }
            public void Dispose()
            {
                lock (service.sync) { if (closed) return; closed = true; service.bindings.Remove(this); }
                scope?.Dispose();
            }
        }
    }

    public sealed class LocalizationModule : GameModule
    {
        private readonly LocalizationService service;
        private readonly Func<CancellationToken, Task<IEnumerable<LocalizationDataset>>> load;
        public LocalizationModule(LocalizationService service,
            Func<CancellationToken, Task<IEnumerable<LocalizationDataset>>> loadAsync = null,
            IReadOnlyCollection<string> dependencies = null, string id = "localization")
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A module ID is required.", nameof(id));
            load = loadAsync;
            Id = id;
            Dependencies = new List<string>(dependencies ?? Array.Empty<string>()).AsReadOnly();
        }
        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override async Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            if (service.Context != context) throw new InvalidOperationException("Localization belongs to a different host.");
            if (load != null)
            {
                var datasets = await load(cancellationToken).ConfigureAwait(false);
                if (context.Dispatcher.CheckAccess) service.ReplaceDatasets(datasets);
                else await context.Dispatcher.InvokeAsync(() => service.ReplaceDatasets(datasets), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!service.IsReady) throw new InvalidOperationException("Localization requires a validated fallback dataset before startup completes.");
            context.Services.Register(service);
        }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken)
        { service.Dispose(); return Task.CompletedTask; }
    }
}
