using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    [NonParallelizable]
    public class BaseManagerConcurrencyTests
    {
        [SetUp]
        public void SetUp()
        {
            ContendedManager.ResetInstance();
            ResettableManager.ResetInstance();
            ContendedManager.ConstructionCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            ContendedManager.ResetInstance();
            ResettableManager.ResetInstance();
        }

        [Test]
        public async Task ConcurrentFirstAccess_ShouldPublishOneFullyInitializedInstancePerReset()
        {
            const int workers = 8;
            const int rounds = 64;
            var instances = new ContendedManager[workers, rounds];
            var states = new ModuleState[workers, rounds];
            var payloads = new int[workers, rounds];
            using var barrier = new Barrier(workers + 1);
            var tasks = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
            {
                for (int round = 0; round < rounds; round++)
                {
                    Synchronize(barrier);
                    var manager = ContendedManager.GetInstance();
                    instances[worker, round] = manager;
                    states[worker, round] = manager?.State ?? ModuleState.None;
                    payloads[worker, round] = manager?.Payload ?? 0;
                    Synchronize(barrier);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            for (int round = 0; round < rounds; round++)
            {
                ContendedManager.ResetInstance();
                Synchronize(barrier);
                Synchronize(barrier);
            }
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(ContendedManager.ConstructionCount, Is.EqualTo(rounds));
            for (int round = 0; round < rounds; round++)
            for (int worker = 0; worker < workers; worker++)
            {
                Assert.That(instances[worker, round], Is.Not.Null);
                Assert.That(instances[worker, round], Is.SameAs(instances[0, round]));
                Assert.That(states[worker, round], Is.EqualTo(ModuleState.Initialized));
                Assert.That(payloads[worker, round], Is.EqualTo(42));
            }
        }

        [Test]
        public async Task GetDuringReset_ShouldWaitForShutdownAndReturnReplacement()
        {
            using var shutdownStarted = new ManualResetEventSlim();
            using var allowShutdown = new ManualResetEventSlim();
            using var getterStarted = new ManualResetEventSlim();
            var previous = ResettableManager.GetInstance();
            previous.OnShutdown = () =>
            {
                shutdownStarted.Set();
                if (!allowShutdown.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Shutdown was not released.");
            };
            var reset = Task.Factory.StartNew(ResettableManager.ResetInstance,
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task<ResettableManager> getter = null;
            bool returnedDuringShutdown = false;
            try
            {
                Assert.That(shutdownStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                getter = Task.Factory.StartNew(() =>
                {
                    getterStarted.Set();
                    return ResettableManager.GetInstance();
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.That(getterStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                returnedDuringShutdown = await Task.WhenAny(getter, Task.Delay(150)) == getter;
            }
            finally
            {
                allowShutdown.Set();
                await reset.WaitAsync(TimeSpan.FromSeconds(5));
                if (getter != null) await getter.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.That(returnedDuringShutdown, Is.False);
            var current = await getter;
            Assert.That(current, Is.Not.Null.And.Not.SameAs(previous));
            Assert.That(current.State, Is.EqualTo(ModuleState.Initialized));
            Assert.That(previous.State, Is.EqualTo(ModuleState.Shutdown));
            Assert.That(previous.ShutdownCount, Is.EqualTo(1));
        }

        private static void Synchronize(Barrier barrier)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent singleton access did not complete.");
        }

        private sealed class ContendedManager : BaseManager<ContendedManager>
        {
            internal static int ConstructionCount;
            internal readonly int Payload;

            public ContendedManager()
            {
                Thread.SpinWait(10000);
                Payload = 42;
                Interlocked.Increment(ref ConstructionCount);
            }
        }

        private sealed class ResettableManager : BaseManager<ResettableManager>
        {
            internal Action OnShutdown;
            internal int ShutdownCount;

            public ResettableManager() { }

            public override void Shutdown()
            {
                OnShutdown?.Invoke();
                ShutdownCount++;
                base.Shutdown();
            }
        }
    }
}
