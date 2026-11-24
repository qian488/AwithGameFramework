using System;
using NUnit.Framework;
using AwithGameFrame.Data.Generation;

namespace AwithGameFrame.Tests
{
    [TestFixture]
    public sealed class GeneratedAssetManifestTests
    {
        private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Test]
        public void ManifestFingerprintIsStableAndMatchesEquivalentOutputs()
        {
            var output = new GeneratedAssetOutput("Generated/Environment.prefab", 12, Hash);
            var manifest = new GeneratedAssetManifest("rain-pass", "2", Hash, 7, new[] { output });

            Assert.That(manifest.Fingerprint, Is.Not.Empty);
            Assert.That(manifest.Matches("rain-pass", "2", Hash.ToUpperInvariant(), 7,
                new[] { new GeneratedAssetOutput("Generated/Environment.prefab", 12, Hash.ToUpperInvariant()) }), Is.True);
            Assert.That(manifest.Matches("rain-pass", "3", Hash, 7, new[] { output }), Is.False);
            Assert.That(manifest.Matches("rain-pass", "2", Hash, 8, new[] { output }), Is.False);
        }

        [Test]
        public void ManifestRejectsUnsafeOrDuplicateOutputs()
        {
            Assert.Throws<ArgumentException>(() => new GeneratedAssetOutput("../Environment.prefab", 1, Hash));
            var output = new GeneratedAssetOutput("Environment.prefab", 1, Hash);
            Assert.Throws<ArgumentException>(() => new GeneratedAssetManifest("generator", "1", Hash, null, new[] { output, output }));
            Assert.Throws<ArgumentException>(() => new GeneratedAssetManifest("generator", "1", Hash, null,
                new[] { new GeneratedAssetOutput("Environment.prefab", 1, Hash), new GeneratedAssetOutput("environment.prefab", 1, Hash) }));
        }
    }
}
