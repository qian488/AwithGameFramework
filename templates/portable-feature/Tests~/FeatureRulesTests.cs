using System;
using AwithGameFrame.FeatureTemplate.Domain;
using NUnit.Framework;

namespace AwithGameFrame.FeatureTemplate.Tests
{
    [TestFixture]
    public sealed class FeatureRulesTests
    {
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void TransitionLeavesTheSourceUntouched(bool initial, bool requested)
        {
            var source = new FeatureState { IsEnabled = initial };

            var candidate = FeatureRules.SetEnabled(source, requested);

            Assert.Multiple(() =>
            {
                Assert.That(candidate.IsEnabled, Is.EqualTo(requested));
                Assert.That(source.IsEnabled, Is.EqualTo(initial));
                Assert.That(candidate, Is.Not.SameAs(source));
            });
        }

        [Test]
        public void UnchangedCandidatesAndRepeatedResultsOwnTheirData()
        {
            var source = new FeatureState { IsEnabled = true };
            var first = FeatureRules.SetEnabled(source, true);
            var second = FeatureRules.SetEnabled(source, true);

            first.IsEnabled = false;

            Assert.Multiple(() =>
            {
                Assert.That(source.IsEnabled, Is.True);
                Assert.That(second.IsEnabled, Is.True);
                Assert.That(first, Is.Not.SameAs(source));
                Assert.That(second, Is.Not.SameAs(first));
            });
        }

        [Test]
        public void ChangingTheSourceDoesNotChangeAnEarlierCandidate()
        {
            var source = new FeatureState();
            var candidate = FeatureRules.SetEnabled(source, false);

            source.IsEnabled = true;

            Assert.That(candidate.IsEnabled, Is.False);
        }

        [Test]
        public void MissingStateIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => FeatureRules.Copy(null));
            Assert.Throws<ArgumentNullException>(() => FeatureRules.SetEnabled(null, true));
        }
    }
}
