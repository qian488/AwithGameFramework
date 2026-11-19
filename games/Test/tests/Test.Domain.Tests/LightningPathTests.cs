using System.Numerics;
using NUnit.Framework;
using Test.Domain.Mathematics;

namespace Test.Domain.Tests
{
    public sealed class LightningPathTests
    {
        [Test]
        public void BoltKeepsEndpointsAndUsesSeedForInteriorJitter()
        {
            var start = Vector3.Zero;
            var end = new Vector3(6f, 0f, 0f);
            var path = LightningPath.Build(start, end, 6, 0.4f, 19);
            Assert.That(path, Has.Length.EqualTo(7));
            Assert.That(path[0], Is.EqualTo(start));
            Assert.That(path[6], Is.EqualTo(end));
            Assert.That(path, Is.EqualTo(LightningPath.Build(start, end, 6, 0.4f, 19)));
            Assert.That(path, Is.Not.EqualTo(LightningPath.Build(start, end, 6, 0.4f, 20)));
            for (var index = 1; index < 6; index++)
            {
                Assert.That(path[index].X, Is.EqualTo(index).Within(0.0001f));
                Assert.That(path[index].Y, Is.InRange(-0.4f, 0.4f));
                Assert.That(path[index].Z, Is.Zero);
            }
        }

        [Test]
        public void CoincidentAnchorsProduceFinitePointsAtTheAnchor()
        {
            var anchor = Vector3.One;
            Assert.That(LightningPath.Build(anchor, anchor, 2, 1f, 13), Is.EqualTo(new[] { anchor, anchor, anchor }));
        }
    }
}
