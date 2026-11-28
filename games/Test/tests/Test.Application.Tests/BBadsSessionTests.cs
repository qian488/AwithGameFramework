using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Test.Application.BBads.Session;

namespace Test.Application.Tests
{
    [TestFixture]
    public sealed class BBadsSessionTests
    {
        [TestCase(1f, 0.5f, 2f, 1.5f, 0)]
        [TestCase(1f, 1f, 2f, 0f, 1)]
        [TestCase(1f, 4.5f, 2f, 1.5f, 2)]
        [TestCase(0f, 2f, 0f, 0f, 2)]
        public void SummonChargeCarriesUnusedFish(float initial, float amount, float cost, float remaining, int summons)
        {
            Assert.That(BBadsSummonCharge.Apply(initial, amount, cost), Is.EqualTo((remaining, summons)));
        }

        [Test]
        public void DispatchesDueEnemiesThenItemsAndCompletesOnce()
        {
            var arrivals = new List<string>();
            var scheduledItem = new ItemSpawn(0.5f, "cat", 2f, 2, 3);
            using var schedule = new BBadsSpawnSchedule(
                3f,
                new[] { EnemyAt(2f, "late"), EnemyAt(1f, "early"), EnemyAt(2f, "late") },
                new[] { new ItemSpawn(2.5f, "fish"), scheduledItem });
            schedule.EnemySpawnRequested += spawn => arrivals.Add(spawn.UnitId);
            schedule.ItemSpawnRequested += spawn =>
            {
                arrivals.Add(spawn.ItemId);
                if (spawn.ItemId == "cat")
                {
                    Assert.That(spawn.HasScheduledPlacement, Is.True);
                    Assert.That((spawn.PlaceTime, spawn.X, spawn.Y), Is.EqualTo((2f, 2, 3)));
                }
            };
            schedule.Completed += () => arrivals.Add("completed");

            schedule.Play();
            schedule.Tick(2.5f);
            Assert.That(arrivals, Is.EqualTo(new[] { "early", "late", "late", "cat", "fish" }));
            Assert.That(schedule.IsCompleted, Is.False);
            schedule.Tick(0.5f);
            schedule.Play();
            schedule.Tick(3f);

            Assert.That(schedule.Elapsed, Is.EqualTo(3f));
            Assert.That(schedule.IsPlaying, Is.False);
            Assert.That(schedule.IsCompleted, Is.True);
            Assert.That(arrivals, Is.EqualTo(new[] { "early", "late", "late", "cat", "fish", "completed" }));
        }

        [Test]
        public void PauseResumesPendingSpawnsAndResetReplaysTimeZeroOnce()
        {
            var arrivals = new List<string>();
            using var schedule = new BBadsSpawnSchedule(1f, new[] { EnemyAt(0f, "zero"), EnemyAt(1f, "one") }, null);
            schedule.EnemySpawnRequested += spawn => arrivals.Add(spawn.UnitId);
            schedule.Completed += () => arrivals.Add("completed");

            schedule.Play();
            schedule.Tick(0f);
            schedule.Tick(0f);
            schedule.Tick(0.25f);
            schedule.Pause();
            schedule.Tick(100f);
            Assert.That(schedule.Elapsed, Is.EqualTo(0.25f));
            Assert.That(arrivals, Is.EqualTo(new[] { "zero" }));

            schedule.Play();
            schedule.Tick(0.75f);
            Assert.That(arrivals, Is.EqualTo(new[] { "zero", "one", "completed" }));

            schedule.ResetRun();
            schedule.Tick(50f);
            Assert.That(schedule.Elapsed, Is.Zero);
            Assert.That(schedule.IsCompleted, Is.False);
            schedule.Play();
            schedule.Tick(1f);
            Assert.That(arrivals, Is.EqualTo(new[] { "zero", "one", "completed", "zero", "one", "completed" }));
        }

        [Test]
        public void ResetFromCallbackStopsTheCurrentRunAndDoesNotSkipTheFirstRowOfTheNext()
        {
            var arrivals = new List<string>();
            bool reset = true;
            using var schedule = new BBadsSpawnSchedule(0f, new[] { EnemyAt(0f, "first"), EnemyAt(0f, "second") }, null);
            schedule.EnemySpawnRequested += spawn =>
            {
                arrivals.Add(spawn.UnitId);
                if (reset)
                {
                    reset = false;
                    schedule.ResetRun();
                    schedule.Play();
                    schedule.Tick(0f);
                }
            };
            schedule.Completed += () => arrivals.Add("completed");

            schedule.Play();
            schedule.Tick(0f);
            Assert.That(arrivals, Is.EqualTo(new[] { "first" }));
            schedule.Tick(0f);
            Assert.That(arrivals, Is.EqualTo(new[] { "first", "first", "second", "completed" }));
        }

        [Test]
        public void DisposingFromACallbackStopsPendingSubscribersAndFutureTicks()
        {
            using var schedule = new BBadsSpawnSchedule(0f, new[] { EnemyAt(0f, "first"), EnemyAt(0f, "second") },
                new[] { new ItemSpawn(0f, "fish") });
            var arrivals = new List<string>();
            schedule.EnemySpawnRequested += spawn =>
            {
                arrivals.Add(spawn.UnitId);
                schedule.Dispose();
            };
            schedule.EnemySpawnRequested += _ => Assert.Fail("A later subscriber received a released spawn.");
            schedule.ItemSpawnRequested += _ => Assert.Fail("A released item spawned.");
            schedule.Completed += () => Assert.Fail("A released run completed.");

            schedule.Play();
            schedule.Tick(0f);
            schedule.Dispose();
            schedule.ResetRun();
            schedule.Play();
            schedule.Tick(100f);

            Assert.That(arrivals, Is.EqualTo(new[] { "first" }));
            Assert.That(schedule.IsPlaying, Is.False);
            Assert.That(schedule.IsCompleted, Is.False);
        }

        [Test]
        public void ASpawnAlreadySentToAThrowingCallbackIsNotRetried()
        {
            var arrivals = new List<string>();
            using var schedule = new BBadsSpawnSchedule(0f, new[] { EnemyAt(0f, "first"), EnemyAt(0f, "second") }, null);
            schedule.EnemySpawnRequested += spawn =>
            {
                arrivals.Add(spawn.UnitId);
                if (spawn.UnitId == "first")
                    throw new InvalidOperationException("Integration failure");
            };

            schedule.Play();
            Assert.Throws<InvalidOperationException>(() => schedule.Tick(0f));
            schedule.Tick(0f);

            Assert.That(arrivals, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(schedule.IsCompleted, Is.True);
        }

        [Test]
        public void SummonPositionsCycleInConfiguredOrderAndRestartFromTheFirst()
        {
            var first = new Vector2(1f, 2f);
            var second = new Vector2(-1f, 3f);
            var positions = new BBadsSummonPositions(new[] { first, second });

            Assert.That(positions.TryTakeNext(out Vector2 position), Is.True);
            Assert.That(position, Is.EqualTo(first));
            Assert.That(positions.TryTakeNext(out position), Is.True);
            Assert.That(position, Is.EqualTo(second));
            Assert.That(positions.TryTakeNext(out position), Is.True);
            Assert.That(position, Is.EqualTo(first));
            positions.Reset();
            Assert.That(positions.TryTakeNext(out position), Is.True);
            Assert.That(position, Is.EqualTo(first));
            Assert.That(new BBadsSummonPositions(null).TryTakeNext(out _), Is.False);
        }

        static EnemySpawn EnemyAt(float time, string id) => new EnemySpawn(time, id, new Vector2(1f, 2f));
    }
}
