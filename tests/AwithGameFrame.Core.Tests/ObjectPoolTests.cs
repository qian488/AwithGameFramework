using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwithGameFrame.Core.Pooling;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class ObjectPoolTests
    {
        private sealed class Item : IDisposable
        {
            public int Rents;
            public int Returns;
            public int Destroys;
            public void Dispose() { Destroys++; }
            public override bool Equals(object obj) => obj is Item;
            public override int GetHashCode() => 1;
        }

        [Test]
        public void PrewarmCreatesIdleValuesAndRentReturnReuseIdentity()
        {
            int created = 0;
            using var pool = new ObjectPool<Item>(() => { created++; return new Item(); }, 3,
                item => item.Rents++, item => item.Returns++);
            pool.Prewarm(2);
            pool.Prewarm(1);
            Assert.That(created, Is.EqualTo(2));
            Assert.That(pool.CountAvailable, Is.EqualTo(2));
            var item = pool.Rent();
            Assert.That(item.Rents, Is.EqualTo(1));
            Assert.That(item.Returns, Is.Zero);
            Assert.That(pool.CountRented, Is.EqualTo(1));
            pool.Return(item);
            Assert.That(item.Returns, Is.EqualTo(1));
            Assert.That(pool.Rent(), Is.SameAs(item));
            Assert.That(item.Rents, Is.EqualTo(2));
            Assert.That(created, Is.EqualTo(2));
        }

        [Test]
        public void HardCapacityIncludesOutstandingValuesAndPrewarmIsValidatedBeforeCreating()
        {
            int created = 0;
            using var pool = new ObjectPool<Item>(() => { created++; return new Item(); }, 2);
            var first = pool.Rent();
            Assert.Throws<InvalidOperationException>(() => pool.Prewarm(2));
            Assert.That(created, Is.EqualTo(1));
            var second = pool.Rent();
            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.That(created, Is.EqualTo(2));
            pool.Return(first);
            Assert.That(pool.Rent(), Is.SameAs(first));
            Assert.That(pool.CountAll, Is.EqualTo(2));
            Assert.That(pool.CountRented, Is.EqualTo(2));
            GC.KeepAlive(second);
        }

        [Test]
        public void OwnershipUsesReferencesAndRejectsForeignAndDuplicateReturns()
        {
            using var pool = new ObjectPool<Item>(() => new Item());
            using var otherPool = new ObjectPool<Item>(() => new Item());
            var first = pool.Rent();
            var second = pool.Rent();
            Assert.That(pool.CountAll, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => otherPool.Return(first));
            Assert.Throws<ArgumentException>(() => pool.Return(new Item()));
            pool.Return(first);
            Assert.Throws<InvalidOperationException>(() => pool.Return(first));
            pool.Return(second);
            Assert.That(pool.CountAvailable, Is.EqualTo(2));
        }

        [Test]
        public void LeaseReturnIsIdempotentAndCannotBeReturnedThroughRawReference()
        {
            int returned = 0;
            using var pool = new ObjectPool<Item>(() => new Item(), onReturn: _ => returned++);
            var lease = pool.RentLease();
            var value = lease.Value;
            Assert.Throws<InvalidOperationException>(() => pool.Return(value));
            lease.Dispose();
            lease.Dispose();
            Assert.That(returned, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => { _ = lease.Value; });
            using var replacement = pool.RentLease();
            Assert.That(replacement.Value, Is.SameAs(value));
            lease.Dispose();
            Assert.That(pool.CountRented, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateFactoryReferenceCannotDestroyOrLeaseAnAlreadyRentedValue()
        {
            var item = new Item();
            using var pool = new ObjectPool<Item>(() => item, 2);
            using var first = pool.RentLease();
            Assert.Throws<InvalidOperationException>(() => pool.RentLease());
            Assert.That(first.Value, Is.SameAs(item));
            Assert.That(item.Destroys, Is.Zero);
            Assert.That(pool.CountAll, Is.EqualTo(1));
        }

        [Test]
        public void FactoryCannotResurrectReferenceDestroyedAfterCallbackFailure()
        {
            var item = new Item();
            bool fail = true;
            using var pool = new ObjectPool<Item>(() => item, onRent: _ =>
            {
                if (fail) throw new FormatException("rent");
            });
            Assert.Throws<FormatException>(() => pool.Rent());
            fail = false;
            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.That(item.Destroys, Is.EqualTo(1));
            Assert.That(pool.CountAll, Is.Zero);
        }

        [Test]
        public void FailedRentDestroysPoisonedValueAndReportsBothFailures()
        {
            var rentFailure = new FormatException("rent");
            var destroyFailure = new InvalidOperationException("destroy");
            var values = new List<Item>();
            bool fail = true;
            using var pool = new ObjectPool<Item>(() => { var item = new Item(); values.Add(item); return item; },
                onRent: _ => { if (fail) throw rentFailure; }, destroy: item =>
                {
                    item.Destroys++;
                    if (fail) throw destroyFailure;
                });
            pool.Prewarm(1);
            var error = Assert.Throws<AggregateException>(() => pool.Rent());
            Assert.That(error.InnerExceptions, Is.EqualTo(new Exception[] { rentFailure, destroyFailure }));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountAvailable, Is.Zero);
            Assert.That(values[0].Destroys, Is.EqualTo(1));
            fail = false;
            using var lease = pool.RentLease();
            Assert.That(lease.Value, Is.Not.SameAs(values[0]));
        }

        [Test]
        public void FailedLeaseReturnConsumesLeaseAndDestroysValueEvenIfCleanupAlsoFails()
        {
            var returnFailure = new FormatException("return");
            var destroyFailure = new InvalidOperationException("destroy");
            using var pool = new ObjectPool<Item>(() => new Item(), onReturn: _ => throw returnFailure,
                destroy: _ => throw destroyFailure);
            var lease = pool.RentLease();
            var error = Assert.Throws<AggregateException>(lease.Dispose);
            Assert.That(error.InnerExceptions, Is.EqualTo(new Exception[] { returnFailure, destroyFailure }));
            lease.Dispose();
            Assert.That(pool.CountAll, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => { _ = lease.Value; });
        }

        [Test]
        public void FailedPrewarmRollsBackOnlyNewBatchAndCollectsRollbackErrors()
        {
            int created = 0;
            var values = new List<Item>();
            var factoryFailure = new FormatException("factory");
            var destroyFailure = new InvalidOperationException("rollback");
            bool failDestroy = true;
            using var pool = new ObjectPool<Item>(() =>
            {
                if (++created == 4) throw factoryFailure;
                var item = new Item();
                values.Add(item);
                return item;
            }, 4, destroy: item =>
            {
                item.Destroys++;
                if (failDestroy) throw destroyFailure;
            });
            pool.Prewarm(1);
            var original = values[0];
            var error = Assert.Throws<AggregateException>(() => pool.Prewarm(4));
            Assert.That(error.InnerExceptions, Is.EqualTo(new Exception[] { factoryFailure, destroyFailure, destroyFailure }));
            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountAvailable, Is.EqualTo(1));
            Assert.That(values[1].Destroys, Is.EqualTo(1));
            Assert.That(values[2].Destroys, Is.EqualTo(1));
            Assert.That(original.Destroys, Is.Zero);
            failDestroy = false;
            Assert.That(pool.Rent(), Is.SameAs(original));
        }

        [Test]
        public void DisposalDestroysIdleRawAndLeasedValuesOnceAndAggregatesFailures()
        {
            var values = new List<Item>();
            var pool = new ObjectPool<Item>(() => { var item = new Item(); values.Add(item); return item; }, 3,
                destroy: item => { item.Destroys++; throw new FormatException("destroy"); });
            pool.Prewarm(3);
            var raw = pool.Rent();
            var lease = pool.RentLease();
            var error = Assert.Throws<AggregateException>(pool.Dispose);
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(3));
            Assert.That(values.ConvertAll(item => item.Destroys), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.CountAvailable, Is.Zero);
            Assert.That(pool.CountRented, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => pool.Return(raw));
            Assert.Throws<ObjectDisposedException>(() => pool.Rent());
            Assert.Throws<ObjectDisposedException>(() => pool.Prewarm(0));
            Assert.Throws<ObjectDisposedException>(() => { _ = lease.Value; });
            lease.Dispose();
            pool.Dispose();
            Assert.That(values.ConvertAll(item => item.Destroys), Is.EqualTo(new[] { 1, 1, 1 }));
        }

        [Test]
        public void WorkerThreadUseIsRejectedWithoutConsumingLease()
        {
            using var pool = new ObjectPool<Item>(() => new Item());
            var lease = pool.RentLease();
            var value = lease.Value;
            var errors = Task.Run(() => new[]
            {
                Catch(() => pool.Rent()),
                Catch(() => pool.Return(value)),
                Catch(lease.Dispose),
                Catch(pool.Dispose),
                Catch(() => pool.Prewarm(1))
            }).GetAwaiter().GetResult();
            foreach (var error in errors) Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(pool.IsDisposed, Is.False);
            Assert.That(pool.CountRented, Is.EqualTo(1));
            Assert.That(lease.Value, Is.Not.Null);
            lease.Dispose();
            Assert.That(pool.CountAvailable, Is.EqualTo(1));
        }

        [Test]
        public void CallbacksCannotReenterMutationsButCanObserveCounts()
        {
            var reentryErrors = new List<Exception>();
            ObjectPool<Item> pool = null;
            pool = new ObjectPool<Item>(() =>
            {
                reentryErrors.Add(Catch(() => pool.Rent()));
                return new Item();
            }, onRent: _ =>
            {
                Assert.That(pool.CountRented, Is.EqualTo(1));
                reentryErrors.Add(Catch(pool.Dispose));
            }, onReturn: item => reentryErrors.Add(Catch(() => pool.Return(item))),
                destroy: _ => reentryErrors.Add(Catch(() => pool.Prewarm(1))));
            var value = pool.Rent();
            pool.Return(value);
            pool.Dispose();
            Assert.That(reentryErrors.Count, Is.EqualTo(4));
            foreach (var error in reentryErrors) Assert.That(error, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void UnhandledCallbackReentryFailsCheckoutAndStillDestroysCreatedValue()
        {
            var item = new Item();
            ObjectPool<Item> pool = null;
            pool = new ObjectPool<Item>(() => item, onRent: _ => pool.Rent());
            using (pool)
            {
                Assert.Throws<InvalidOperationException>(() => pool.Rent());
                Assert.That(item.Destroys, Is.EqualTo(1));
                Assert.That(pool.CountAll, Is.Zero);
            }
        }

        [Test]
        public void InvalidFactoryResultsAndArgumentsDoNotCreateOwnedValues()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Item>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<Item>(() => new Item(), 0));
            using var pool = new ObjectPool<Item>(() => null, 2);
            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(3));
            Assert.Throws<ArgumentNullException>(() => pool.Return(null));
            Assert.That(pool.CountAll, Is.Zero);
        }

        [Test]
        public void WeakDuplicateTrackingDoesNotKeepDestroyedValuesAlive()
        {
            WeakReference target = null;
            using var pool = new ObjectPool<Item>(() => new Item(), onRent: value =>
            {
                target = new WeakReference(value);
                throw new FormatException("poisoned");
            });
            FailCheckout(pool);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(target.IsAlive, Is.False);
            GC.KeepAlive(pool);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void FailCheckout(ObjectPool<Item> pool)
        {
            Assert.Throws<FormatException>(() => pool.Rent());
        }

        private static Exception Catch(Action action)
        {
            try { action(); return null; }
            catch (Exception error) { return error; }
        }
    }
}
