using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Extensions.Localization;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    internal static class SettingsHostTest
    {
        public static void Wait(Task task, params GameHost[] hosts)
        {
            Until(() => task.IsCompleted, hosts);
            task.GetAwaiter().GetResult();
        }
        public static void Until(Func<bool> condition, params GameHost[] hosts)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Host work did not complete.");
                foreach (var host in hosts)
                    if (host.State != GameHostState.Stopped && host.State != GameHostState.Faulted) host.Dispatcher.RunPending(64);
                Thread.Sleep(1);
            }
        }
        public static LocalizationDataset Dataset(string language, params string[] pairs)
        {
            var result = new LocalizationDataset { LanguageCode = language };
            for (int i = 0; i < pairs.Length; i += 2) result.Entries.Add(new LocalizationEntry { Key = pairs[i], Value = pairs[i + 1] });
            return result;
        }
        public static LocalizationService Localization(GameHost host)
        {
            var service = new LocalizationService(host.Context);
            service.ReplaceDatasets(new[] {
                Dataset("en", "greeting", "Hello", "only-default", "Fallback", "number", "{0:0.0}", "bad-format", "{1}"),
                Dataset("fr", "greeting", "Bonjour", "parent", "Parent"), Dataset("fr-CA", "regional", "Quebec") });
            return service;
        }
    }

    [TestFixture]
    public class LocalizationServiceTests
    {
        [Test]
        public void ParentAndDefaultFallbackArePerKeyAndHostsRemainIsolated()
        {
            var first = new GameHost(); var second = new GameHost();
            using var one = SettingsHostTest.Localization(first);
            using var two = SettingsHostTest.Localization(second);
            int firstChanges = 0, secondChanges = 0;
            using var firstSubscription = first.Events.Subscribe<LanguageChanged>(_ => firstChanges++);
            using var secondSubscription = second.Events.Subscribe<LanguageChanged>(_ => secondChanges++);
            string firstText = null, secondText = null;
            using var firstBinding = one.BindText("greeting", value => firstText = value);
            using var secondBinding = two.BindText("greeting", value => secondText = value);
            one.SwitchLanguage("fr-CA");
            Assert.Multiple(() => {
                Assert.That(one.GetText("regional"), Is.EqualTo("Quebec"));
                Assert.That(one.GetText("parent"), Is.EqualTo("Parent"));
                Assert.That(one.GetText("only-default"), Is.EqualTo("Fallback"));
                Assert.That(one.GetText("missing"), Is.EqualTo("missing"));
                Assert.That(one.GetText("missing", "custom"), Is.EqualTo("custom"));
                Assert.That(one.GetFormat("number", 12.5), Is.EqualTo("12,5"));
                Assert.That(firstText, Is.EqualTo("Bonjour"));
                Assert.That(secondText, Is.EqualTo("Hello"));
                Assert.That(firstChanges, Is.EqualTo(1)); Assert.That(secondChanges, Is.Zero);
                Assert.That(two.CurrentLanguage, Is.EqualTo("en"));
                Assert.That(one.IsLanguageSupported("fr-FR"), Is.True);
                Assert.That(one.IsLanguageSupported("de"), Is.False);
            });
            Assert.Throws<FormatException>(() => one.GetFormat("bad-format", 1));
            firstBinding.Dispose(); one.SwitchLanguage("en");
            Assert.That(firstText, Is.EqualTo("Bonjour"));
            SettingsHostTest.Wait(first.StopAsync(), first); SettingsHostTest.Wait(second.StopAsync(), second);
        }

        [Test]
        public void CatalogPublicationCopiesInputsValidatesAtomicallyAndRefreshesBindings()
        {
            var host = new GameHost();
            using var service = SettingsHostTest.Localization(host);
            service.SwitchLanguage("fr");
            string text = null;
            using var binding = service.BindText("greeting", value => text = value);
            var en = SettingsHostTest.Dataset("en", "greeting", "New");
            service.ReplaceDatasets(new[] { en });
            en.Entries[0] = new LocalizationEntry { Key = "greeting", Value = "External mutation" };
            Assert.That(service.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(text, Is.EqualTo("New"));
            Assert.That(service.GetText("greeting"), Is.EqualTo("New"));
            Assert.Throws<ArgumentException>(() => service.ReplaceDatasets(new[] { SettingsHostTest.Dataset("fr", "x", "y") }));
            Assert.Throws<ArgumentException>(() => service.ReplaceDatasets(new[] { SettingsHostTest.Dataset("en", "x", "a", "x", "b") }));
            Assert.Throws<ArgumentException>(() => service.ReplaceDatasets(new[] { en, en }));
            Assert.Throws<ArgumentException>(() => service.SwitchLanguage("de"));
            Assert.That(service.GetText("greeting"), Is.EqualTo("New"));
            SettingsHostTest.Wait(host.StopAsync(), host);
            Assert.Throws<OperationCanceledException>(() => service.SwitchLanguage("en"));
        }

        [Test]
        public void ListenerFailuresAreObservableAfterCommitAndReentryIsRejected()
        {
            var host = new GameHost();
            using var service = SettingsHostTest.Localization(host);
            using var throwing = host.Events.Subscribe<LanguageChanged>(_ => service.SwitchLanguage("en"));
            Assert.Throws<AggregateException>(() => service.SwitchLanguage("fr"));
            Assert.That(service.CurrentLanguage, Is.EqualTo("fr"));
            Assert.Throws<InvalidOperationException>(() => service.BindText("greeting", _ => service.SwitchLanguage("en")));
            throwing.Dispose(); service.SwitchLanguage("en");
            SettingsHostTest.Wait(host.StopAsync(), host);
        }

        [Test]
        public void ConcurrentFormattingReadsOneLanguageAndTemplateSnapshot()
        {
            var host = new GameHost();
            using var service = new LocalizationService(host.Context);
            service.ReplaceDatasets(new[] { SettingsHostTest.Dataset("en", "format", "en {0:0.0}"), SettingsHostTest.Dataset("fr", "format", "fr {0:0.0}") });
            using var reading = new CancellationTokenSource();
            var reader = Task.Run(() => {
                while (!reading.IsCancellationRequested)
                {
                    string text = service.GetFormat("format", 12.5);
                    if (text != "en 12.5" && text != "fr 12,5") throw new InvalidOperationException("Mixed localization snapshot: " + text);
                }
            });
            try { for (int i = 0; i < 4000; i++) service.SwitchLanguage(i % 2 == 0 ? "fr" : "en"); }
            finally { reading.Cancel(); reader.GetAwaiter().GetResult(); SettingsHostTest.Wait(host.StopAsync(), host); }
        }

        [Test]
        public void AsyncModulePublishesOnHostThreadAndDisposesItsBindings()
        {
            var host = new GameHost(); var service = new LocalizationService(host.Context);
            int hostThread = Thread.CurrentThread.ManagedThreadId, observedThread = 0, updates = 0;
            var pending = new TaskCompletionSource<IEnumerable<LocalizationDataset>>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.AddModule(new LocalizationModule(service, _ => pending.Task));
            var startup = host.StartAsync();
            Task.Run(() => pending.SetResult(new[] { SettingsHostTest.Dataset("en", "x", "ready") })).GetAwaiter().GetResult();
            SettingsHostTest.Wait(startup, host);
            Assert.That(host.Services.Get<LocalizationService>(), Is.SameAs(service));
            using var binding = service.BindText("x", _ => { observedThread = Thread.CurrentThread.ManagedThreadId; updates++; });
            Assert.That(observedThread, Is.EqualTo(hostThread));
            SettingsHostTest.Wait(host.StopAsync(), host);
            Assert.That(updates, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => service.GetText("x"));
        }
    }
}
