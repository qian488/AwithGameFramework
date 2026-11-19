using System.Numerics;
using NUnit.Framework;
using Test.Domain.Mathematics;

namespace Test.Domain.Tests
{
    public sealed class LaunchFormulaTests
    {
        [Test]
        public void SpiralSamplesRadiusAndOutwardVelocityAtQuarterTurn()
        {
            ILaunchFormula2D formula = new SpiralFormula2D(radius: 4f, turns: 1f);
            var start = formula.Sample(0f, 2f);
            var quarter = formula.Sample(0.25f, 2f);
            var end = formula.Sample(1f, 2f);

            AssertVector(start.Position, 0f, 0f);
            AssertVector(quarter.Position, 0f, 1f);
            AssertVector(quarter.Velocity, 0f, 2f);
            AssertVector(end.Position, 4f, 0f);
        }

        [Test]
        public void ProjectileFanUsesBothEdgesAndExposesVelocityToOtherCallers()
        {
            ILaunchFormula2D formula = new ProjectileFormula2D(launchAngleDegrees: 45f, spreadDegrees: 90f);

            AssertVector(formula.Sample(0f, 2f).Velocity, 2f, 0f);
            AssertVector(formula.Sample(1f, 2f).Velocity, 0f, 2f);
            AssertVector(formula.Sample(0.5f, 2f).Position, 0f, 0f);
            Assert.That(formula.Sample(0.5f, 2f).Velocity, Is.EqualTo(ProjectileFormula2D.LaunchVelocity(2f, 45f)));
            AssertVector(ProjectileFormula2D.LaunchVelocity(3f, -90f), 0f, -3f);
        }

        private static void AssertVector(Vector2 value, float expectedX, float expectedY)
        {
            Assert.That(value.X, Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(value.Y, Is.EqualTo(expectedY).Within(0.0001f));
        }
    }
}
