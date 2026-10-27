using System;
using System.Collections.Generic;
using AwithGameFrame.FeatureTemplate.Application;
using AwithGameFrame.FeatureTemplate.Domain;
using NUnit.Framework;

namespace AwithGameFrame.FeatureTemplate.Tests
{
    [TestFixture]
    public sealed class FeatureServiceTests
    {
        [Test]
        public void InitialDataAndReturnedSnapshotsCannotChangeOwnedState()
        {
            var initial = new FeatureState();
            var sink = new RecordingSink();
            var service = new FeatureService(initial, sink);
            var first = service.Snapshot;

            initial.IsEnabled = true;
            first.IsEnabled = true;

            Assert.Multiple(() =>
            {
                Assert.That(service.Snapshot.IsEnabled, Is.False);
                Assert.That(service.Snapshot, Is.Not.SameAs(first));
                Assert.That(sink.Changes, Is.Empty);
            });
        }

        [Test]
        public void ServicesCreatedFromTheSameDataRemainIndependent()
        {
            var initial = new FeatureState();
            var firstSink = new RecordingSink();
            var secondSink = new RecordingSink();
            var first = new FeatureService(initial, firstSink);
            var second = new FeatureService(initial, secondSink);

            first.SetEnabled(true);

            Assert.Multiple(() =>
            {
                Assert.That(first.Snapshot.IsEnabled, Is.True);
                Assert.That(second.Snapshot.IsEnabled, Is.False);
                Assert.That(initial.IsEnabled, Is.False);
                Assert.That(firstSink.Changes, Has.Count.EqualTo(1));
                Assert.That(secondSink.Changes, Is.Empty);
            });
        }

        [Test]
        public void SinkObservesCommittedStateAndReceivesThePreviousValue()
        {
            var sink = new RecordingSink();
            var service = new FeatureService(new FeatureState(), sink);
            sink.OnChange = (previous, current) => Assert.Multiple(() =>
            {
                Assert.That(previous.IsEnabled, Is.False);
                Assert.That(current.IsEnabled, Is.True);
                Assert.That(service.Snapshot.IsEnabled, Is.True);
            });

            Assert.That(service.SetEnabled(true), Is.True);
            Assert.That(sink.Changes, Has.Count.EqualTo(1));
        }

        [Test]
        public void RepeatingTheSameCommandDoesNotRepeatEffects()
        {
            var sink = new RecordingSink();
            var service = new FeatureService(new FeatureState(), sink);

            Assert.That(service.SetEnabled(false), Is.False);
            Assert.That(sink.Changes, Is.Empty);
            Assert.That(service.SetEnabled(true), Is.True);
            Assert.That(service.SetEnabled(true), Is.False);
            Assert.That(sink.Changes, Has.Count.EqualTo(1));
        }

        [Test]
        public void SinkMutationAndRetainedNotificationDataCannotChangeOwnedState()
        {
            var sink = new RecordingSink();
            var service = new FeatureService(new FeatureState(), sink);
            sink.OnChange = (previous, current) =>
            {
                previous.IsEnabled = true;
                current.IsEnabled = false;
            };

            service.SetEnabled(true);
            Assert.That(service.Snapshot.IsEnabled, Is.True);

            sink.OnChange = null;
            service.SetEnabled(false);
            sink.Changes[0].Current.IsEnabled = true;

            Assert.Multiple(() =>
            {
                Assert.That(service.Snapshot.IsEnabled, Is.False);
                Assert.That(sink.Changes[1].Previous.IsEnabled, Is.True);
                Assert.That(sink.Changes[1].Current.IsEnabled, Is.False);
            });
        }

        [Test]
        public void EffectFailurePropagatesAfterCommitAndDoesNotLeaveTheServiceBusy()
        {
            var failure = new InvalidOperationException("The host event sink failed.");
            var sink = new RecordingSink { OnChange = (previous, current) => throw failure };
            var service = new FeatureService(new FeatureState(), sink);

            var observed = Assert.Throws<InvalidOperationException>(() => service.SetEnabled(true));

            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.SameAs(failure));
                Assert.That(service.Snapshot.IsEnabled, Is.True);
                Assert.That(sink.Changes, Has.Count.EqualTo(1));
                Assert.That(service.SetEnabled(true), Is.False);
            });

            sink.OnChange = null;
            Assert.That(service.SetEnabled(false), Is.True);
            Assert.That(sink.Changes, Has.Count.EqualTo(2));
            Assert.That(sink.Changes[1].Previous.IsEnabled, Is.True);
        }

        [Test]
        public void ReentrantModificationIsRejectedAfterTheOuterCommit()
        {
            var sink = new RecordingSink();
            var service = new FeatureService(new FeatureState(), sink);
            sink.OnChange = (previous, current) => service.SetEnabled(false);

            Assert.Throws<InvalidOperationException>(() => service.SetEnabled(true));
            Assert.That(service.Snapshot.IsEnabled, Is.True);
            Assert.That(sink.Changes, Has.Count.EqualTo(1));

            sink.OnChange = null;
            Assert.That(service.SetEnabled(false), Is.True);
        }

        [Test]
        public void DependenciesMustBeExplicit()
        {
            Assert.Throws<ArgumentNullException>(() => new FeatureService(null, new RecordingSink()));
            Assert.Throws<ArgumentNullException>(() => new FeatureService(new FeatureState(), null));
        }

        private sealed class RecordingSink : IFeatureStateSink
        {
            public List<(FeatureState Previous, FeatureState Current)> Changes { get; } =
                new List<(FeatureState Previous, FeatureState Current)>();

            public Action<FeatureState, FeatureState> OnChange { get; set; }

            public void OnStateChanged(FeatureState previous, FeatureState current)
            {
                Changes.Add((previous, current));
                OnChange?.Invoke(previous, current);
            }
        }
    }
}
