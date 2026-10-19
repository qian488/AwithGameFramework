using System;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Logging;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class HostClockTests
    {
        private ITimeSource originalTimeSource;

        [SetUp]
        public void SetUp()
        {
            originalTimeSource = TimeHelper.TimeSource;
            TimeHelper.TimeSource = null;
        }

        [TearDown]
        public void TearDown()
        {
            TimeHelper.TimeSource = originalTimeSource;
        }

        [Test]
        public void SimulationClock_ShouldRequireHostAndForwardControls()
        {
            Assert.Throws<InvalidOperationException>(() => TimeHelper.GetGameTime());
            var clock = new TestClock();
            TimeHelper.TimeSource = clock;

            Assert.That(TimeHelper.GetGameTime(), Is.EqualTo(10f));
            Assert.That(TimeHelper.GetDeltaTime(), Is.EqualTo(0.25f));
            TimeHelper.PauseGame();
            Assert.That(clock.TimeScale, Is.Zero);
            TimeHelper.ResumeGame();
            Assert.That(clock.TimeScale, Is.EqualTo(1f));
            TimeHelper.SetTargetFrameRate(120);
            Assert.That(clock.TargetFrameRate, Is.EqualTo(120));
        }

        [Test]
        public void PerformanceMonitor_ShouldMeasureHostTicksWithoutEngineClock()
        {
            var monitor = new PerformanceMonitor { EnablePerformanceLogging = false, EnableAutoLogging = false };
            monitor.Tick(0.25f);
            monitor.Tick(0.25f);
            monitor.Tick(0.25f);
            monitor.Tick(0.25f);

            Assert.That(monitor.CurrentFrameRate, Is.EqualTo(4f));
            Assert.That(monitor.AverageFrameRate, Is.EqualTo(4f));
            monitor.Tick(0f);
            Assert.That(monitor.CurrentFrameRate, Is.EqualTo(4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => monitor.Tick(float.NaN));
        }

        private sealed class TestClock : ITimeSource
        {
            public float GameTime => 10f;
            public float UnscaledGameTime => 20f;
            public float DeltaTime => 0.25f;
            public float UnscaledDeltaTime => 0.5f;
            public float FixedDeltaTime => 0.02f;
            public float TimeScale { get; set; } = 1f;
            public int TargetFrameRate { get; set; } = 60;
        }
    }
}
