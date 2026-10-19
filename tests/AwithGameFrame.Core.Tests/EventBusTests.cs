using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class EventBusTests
    {
        private class BaseMessage { }
        private sealed class DerivedMessage : BaseMessage { }
        private sealed class Receiver { public void Receive(int _) { } }

        [Test]
        public void BusesAndDeclaredMessageTypesAreIsolated()
        {
            using var first = new EventBus();
            using var second = new EventBus();
            int firstCalls = 0, secondCalls = 0, baseCalls = 0;
            using var a = first.Subscribe<DerivedMessage>(_ => firstCalls++);
            using var b = second.Subscribe<DerivedMessage>(_ => secondCalls++);
            using var c = first.Subscribe<BaseMessage>(_ => baseCalls++);
            var message = new DerivedMessage();
            Assert.That(first.Publish(message).InvocationCount, Is.EqualTo(1));
            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.Zero);
            Assert.That(baseCalls, Is.Zero);
            first.Publish<BaseMessage>(message);
            Assert.That(baseCalls, Is.EqualTo(1));
        }

        [Test]
        public void ReentrantPublicationTakesNewSnapshotWithoutChangingOuterSnapshot()
        {
            using var bus = new EventBus();
            var calls = new List<string>();
            IDisposable second = null;
            using var first = bus.Subscribe<int>(value =>
            {
                calls.Add("first:" + value);
                if (value != 0) return;
                second.Dispose();
                bus.Subscribe<int>(nested => calls.Add("new:" + nested));
                bus.Publish(1).ThrowIfFailed();
            });
            second = bus.Subscribe<int>(value => calls.Add("second:" + value));
            var result = bus.Publish(0);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "first:0", "first:1", "new:1", "second:0" }));
            Assert.That(bus.SubscriptionCount, Is.EqualTo(2));
        }

        [Test]
        public void ListenerErrorsAreOrderedAndDoNotPreventDelivery()
        {
            using var bus = new EventBus();
            var first = new FormatException("first");
            var second = new InvalidOperationException("second");
            int delivered = 0;
            bus.Subscribe<int>(_ => throw first);
            bus.Subscribe<int>(_ => delivered++);
            bus.Subscribe<int>(_ => throw second);
            var result = bus.Publish(7);
            Assert.That(result.InvocationCount, Is.EqualTo(3));
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(result.Errors, Is.EqualTo(new Exception[] { first, second }));
            Assert.That(result.IsSuccess, Is.False);
            var failure = Assert.Throws<AggregateException>(result.ThrowIfFailed);
            Assert.That(failure.InnerExceptions, Is.EqualTo(result.Errors));
        }

        [Test]
        public void SubscriptionCancellationAndScopeDisposalRemoveOnlyTheirListeners()
        {
            using var bus = new EventBus();
            using var lifetime = new CancellationTokenSource();
            using var scope = bus.CreateScope(lifetime.Token);
            using var listenerCancellation = new CancellationTokenSource();
            int calls = 0;
            var cancelled = scope.Subscribe<int>(_ => calls++, listenerCancellation.Token);
            scope.Subscribe<int>(_ => calls++);
            using var independent = bus.Subscribe<int>(_ => calls++);
            Assert.That(scope.SubscriptionCount, Is.EqualTo(2));
            listenerCancellation.Cancel();
            cancelled.Dispose();
            Assert.That(scope.SubscriptionCount, Is.EqualTo(1));
            Assert.That(bus.SubscriptionCount, Is.EqualTo(2));
            lifetime.Cancel();
            scope.Dispose();
            Assert.That(scope.IsDisposed, Is.True);
            Assert.That(scope.SubscriptionCount, Is.Zero);
            Assert.That(bus.Publish(1).InvocationCount, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => scope.Subscribe<int>(_ => { }));
        }

        [Test]
        public void AlreadyCancelledTokensNeverLeaveRegisteredListenersOrScopes()
        {
            using var bus = new EventBus();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            using var subscription = bus.Subscribe<int>(_ => Assert.Fail("Cancelled listener ran."), cancelled.Token);
            using var scope = bus.CreateScope(cancelled.Token);
            Assert.That(bus.SubscriptionCount, Is.Zero);
            Assert.That(scope.IsDisposed, Is.True);
            Assert.That(bus.Publish(1).InvocationCount, Is.Zero);
        }

        [Test]
        public void DisposeDuringDispatchClosesBusButCapturedListenersFinish()
        {
            var bus = new EventBus();
            var scope = bus.CreateScope();
            int remaining = 0;
            scope.Subscribe<int>(_ => bus.Dispose());
            var listener = scope.Subscribe<int>(_ => remaining++);
            var result = bus.Publish(1);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(remaining, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
            Assert.That(bus.SubscriptionCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => bus.Publish(2));
            Assert.Throws<ObjectDisposedException>(() => bus.Subscribe<int>(_ => { }));
            Assert.Throws<ObjectDisposedException>(() => bus.CreateScope());
            listener.Dispose();
            scope.Dispose();
            bus.Dispose();
        }

        [Test]
        public void ListenerRunsOnPublisherThreadAndConcurrentPublishersAreSupported()
        {
            using var bus = new EventBus();
            var threads = new ConcurrentDictionary<int, int>();
            bus.Subscribe<int>(expectedThread =>
            {
                int actual = Thread.CurrentThread.ManagedThreadId;
                if (actual != expectedThread) throw new InvalidOperationException("Unexpected thread switch.");
                threads.AddOrUpdate(actual, 1, (_, count) => count + 1);
            });
            Parallel.For(0, 200, _ => bus.Publish(Thread.CurrentThread.ManagedThreadId).ThrowIfFailed());
            int total = 0;
            foreach (var count in threads.Values) total += count;
            Assert.That(total, Is.EqualTo(200));
        }

        [Test]
        public void CancellationRacingSubscriptionAndBusClosureDoesNotLeak()
        {
            for (int iteration = 0; iteration < 100; iteration++)
            {
                var bus = new EventBus();
                using var cancellation = new CancellationTokenSource();
                var register = Task.Run(() =>
                {
                    try { bus.Subscribe<int>(_ => { }, cancellation.Token); }
                    catch (ObjectDisposedException) { }
                });
                Task.WaitAll(register, Task.Run(cancellation.Cancel), Task.Run(bus.Dispose));
                Assert.That(bus.SubscriptionCount, Is.Zero);
                Assert.That(bus.IsDisposed, Is.True);
            }
        }

        [Test]
        public void DisposedHandlesAndCancelledTokensDoNotRetainListenerTargets()
        {
            using var bus = new EventBus();
            using var cancellation = new CancellationTokenSource();
            var direct = RegisterReceiver(bus, null, cancellation.Token);
            var scope = bus.CreateScope();
            var scoped = RegisterReceiver(bus, scope, cancellation.Token);
            direct.Handle.Dispose();
            bus.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(direct.Target.IsAlive, Is.False);
            Assert.That(scoped.Target.IsAlive, Is.False);
            GC.KeepAlive(direct.Handle);
            GC.KeepAlive(scoped.Handle);
            GC.KeepAlive(scope);
            cancellation.Cancel();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference Target, IDisposable Handle) RegisterReceiver(EventBus bus, EventScope scope, CancellationToken token)
        {
            var receiver = new Receiver();
            return (new WeakReference(receiver), scope == null
                ? bus.Subscribe<int>(receiver.Receive, token)
                : scope.Subscribe<int>(receiver.Receive, token));
        }
    }
}
