using System;
using System.ComponentModel;
using System.Threading;
using AwithGameFrame.Extensions.Presentation;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class ViewBindingTests
    {
        private sealed class Model : INotifyPropertyChanged
        {
            private PropertyChangedEventHandler changed;
            public int Value, Subscribers;
            public event PropertyChangedEventHandler PropertyChanged
            { add { changed += value; Subscribers++; } remove { changed -= value; Subscribers--; } }
            public void Publish(string name) => changed?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        [Test]
        public void BindingInitiallySynchronizesFiltersPropertiesAndDetachesIndividuallyAndAsScope()
        {
            var source = new Model { Value = 7 };
            var scope = new ViewBindingScope();
            int displayed = -1, updates = 0;
            var binding = scope.Bind(source, nameof(Model.Value), () => source.Value, value => { displayed = value; updates++; });
            Assert.That(displayed, Is.EqualTo(7));
            source.Value = 8; source.Publish("Other");
            Assert.That(updates, Is.EqualTo(1));
            source.Publish(null); Assert.That(displayed, Is.EqualTo(8));
            source.Value = 9; source.Publish(nameof(Model.Value)); Assert.That(displayed, Is.EqualTo(9));
            binding.Dispose(); source.Value = 10; source.Publish("");
            Assert.That(displayed, Is.EqualTo(9)); Assert.That(source.Subscribers, Is.Zero);
            scope.Bind(source, nameof(Model.Value), () => source.Value, value => displayed = value);
            scope.Dispose(); scope.Dispose();
            Assert.That(source.Subscribers, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => scope.Bind(source, nameof(Model.Value), () => source.Value, _ => { }));
        }

        [Test]
        public void CancellationDetachesWithoutCallingViewSetterAndObservableValueSuppressesEqualChanges()
        {
            using var cancellation = new CancellationTokenSource();
            using var scope = new ViewBindingScope(cancellation.Token);
            var source = new ObservableValue<string>("first"); int updates = 0;
            scope.Bind(source, nameof(source.Value), () => source.Value, _ => updates++);
            source.Value = "first"; source.Value = "second";
            Assert.That(updates, Is.EqualTo(2));
            Exception failure = null;
            var worker = new Thread(() => { try { cancellation.Cancel(); } catch (Exception error) { failure = error; } });
            worker.Start(); worker.Join();
            Assert.That(failure, Is.Null);
            source.Value = "third"; Assert.That(updates, Is.EqualTo(2));
            Assert.Throws<ObjectDisposedException>(() => scope.Bind(source, nameof(source.Value), () => source.Value, _ => { }));
        }

        [Test]
        public void SetterFailuresRemainObservableAndBackgroundNotificationsCannotTouchView()
        {
            var source = new Model { Value = 1 }; using var scope = new ViewBindingScope();
            Assert.Throws<FormatException>(() => scope.Bind(source, nameof(Model.Value), () => source.Value, _ => throw new FormatException("initial")));
            Assert.That(source.Subscribers, Is.Zero, "Failed initial sync must unsubscribe.");
            bool fail = false; int updates = 0;
            scope.Bind(source, nameof(Model.Value), () => source.Value, _ => { if (fail) throw new FormatException("update"); updates++; });
            fail = true; Assert.Throws<FormatException>(() => source.Publish(nameof(Model.Value))); fail = false;
            Exception failure = null;
            var worker = new Thread(() => { try { source.Publish(nameof(Model.Value)); } catch (Exception error) { failure = error; } });
            worker.Start(); worker.Join();
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(updates, Is.EqualTo(1));
            source.Publish(nameof(Model.Value)); Assert.That(updates, Is.EqualTo(2));
        }
    }
}
