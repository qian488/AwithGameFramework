using System;
using System.Collections.Generic;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Bootstrap;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    [NonParallelizable]
    public class CoreRuntimeReliabilityTests
    {
        private Action<LogLevel, string, object> previousOutput;

        [SetUp]
        public void SetUp()
        {
            previousOutput = LoggingHost.Output;
            LoggingHost.Output = (_, _, _) => { };
            FrameworkBootstrap.Shutdown();
            ServiceLocator.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                FrameworkBootstrap.Shutdown();
            }
            finally
            {
                ServiceLocator.Clear();
                LoggingHost.Output = previousOutput;
            }
        }

        [Test]
        public void InitializationFailure_ShouldReleasePartialResourcesAndAllowRetry()
        {
            var calls = new List<string>();
            var first = new LifecycleModule("First", 100, calls);
            var failing = new LifecycleModule("Failing", 50, calls);
            var last = new LifecycleModule("Last", 0, calls);
            var failure = new InvalidOperationException("Initialization failed after acquiring a resource.");
            failing.OnInitialize = () => throw failure;
            FrameworkBootstrap.Register(last);
            FrameworkBootstrap.Register(failing);
            FrameworkBootstrap.Register(first);

            var actual = Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Bootstrap());

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(calls, Is.EqualTo(new[]
            {
                "First.Initialize", "Failing.Initialize", "Failing.Shutdown", "First.Shutdown"
            }));
            Assert.That(FrameworkBootstrap.RegisteredModules.Count, Is.EqualTo(3));

            failing.OnInitialize = null;
            FrameworkBootstrap.Bootstrap();
            FrameworkBootstrap.Bootstrap();
            Assert.That(first.InitializeCount, Is.EqualTo(2));
            Assert.That(failing.InitializeCount, Is.EqualTo(2));
            Assert.That(last.InitializeCount, Is.EqualTo(1));
            Assert.That(last.State, Is.EqualTo(ModuleState.Running));
        }

        [Test]
        public void PostInitializationFailure_ShouldRollbackEveryInitializedModuleInReverseOrder()
        {
            var calls = new List<string>();
            var first = new LifecycleModule("First", 100, calls);
            var failing = new LifecycleModule("Failing", 0, calls);
            var failure = new InvalidOperationException("Post initialization failed.");
            failing.OnPostInitialize = () => throw failure;
            FrameworkBootstrap.Register(first);
            FrameworkBootstrap.Register(failing);

            Assert.That(Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Bootstrap()), Is.SameAs(failure));

            Assert.That(calls, Is.EqualTo(new[]
            {
                "First.Initialize", "Failing.Initialize", "First.PostInitialize", "Failing.PostInitialize",
                "Failing.Shutdown", "First.Shutdown"
            }));
            FrameworkBootstrap.Shutdown();
            Assert.That(first.ShutdownCount, Is.EqualTo(1));
            Assert.That(failing.ShutdownCount, Is.EqualTo(1));
        }

        [Test]
        public void RollbackFailure_ShouldPreserveOriginalFailureAndContinueCleanup()
        {
            var first = new LifecycleModule("First", 100);
            var failing = new LifecycleModule("Failing", 0);
            var initializationFailure = new InvalidOperationException("Initialize failure");
            var cleanupFailure = new InvalidOperationException("Cleanup failure");
            failing.OnInitialize = () => throw initializationFailure;
            failing.OnShutdown = () => throw cleanupFailure;
            FrameworkBootstrap.Register(first);
            FrameworkBootstrap.Register(failing);

            var aggregate = Assert.Throws<AggregateException>(() => FrameworkBootstrap.Bootstrap());

            Assert.That(aggregate.InnerExceptions, Is.EqualTo(new Exception[] { initializationFailure, cleanupFailure }));
            Assert.That(first.ShutdownCount, Is.EqualTo(1));
            failing.OnInitialize = null;
            failing.OnShutdown = null;
            FrameworkBootstrap.Bootstrap();
            Assert.That(first.State, Is.EqualTo(ModuleState.Running));
        }

        [Test]
        public void ShutdownFailure_ShouldCleanupRemainingModulesAndAllowNewSession()
        {
            var calls = new List<string>();
            var first = new LifecycleModule("First", 100, calls);
            var last = new LifecycleModule("Last", 0, calls);
            var failure = new InvalidOperationException("Cannot release one resource");
            last.OnShutdown = () => throw failure;
            FrameworkBootstrap.Register(first);
            FrameworkBootstrap.Register(last);
            FrameworkBootstrap.Bootstrap();
            calls.Clear();

            var aggregate = Assert.Throws<AggregateException>(() => FrameworkBootstrap.Shutdown());

            Assert.That(aggregate.InnerExceptions, Is.EqualTo(new[] { failure }));
            Assert.That(calls, Is.EqualTo(new[] { "Last.Shutdown", "First.Shutdown" }));
            Assert.That(FrameworkBootstrap.RegisteredModules, Is.Empty);
            Assert.DoesNotThrow(() => FrameworkBootstrap.Shutdown());
            Assert.That(last.ShutdownCount, Is.EqualTo(1));

            var replacement = new LifecycleModule("Replacement", 0);
            FrameworkBootstrap.Register(replacement);
            FrameworkBootstrap.Bootstrap();
            Assert.That(replacement.State, Is.EqualTo(ModuleState.Running));
        }

        [Test]
        public void ShutdownBeforeBootstrap_ShouldDiscardRegistrationWithoutCallingModuleCleanup()
        {
            var module = new LifecycleModule("RegisteredOnly", 0);
            FrameworkBootstrap.Register(module);

            FrameworkBootstrap.Shutdown();
            FrameworkBootstrap.Shutdown();

            Assert.That(module.ShutdownCount, Is.Zero);
            Assert.That(FrameworkBootstrap.RegisteredModules, Is.Empty);
        }

        [Test]
        public void LifecycleCallbacks_ShouldRejectRecursiveBootstrapAndNewRegistration()
        {
            var module = new LifecycleModule("Module", 0);
            var extra = new LifecycleModule("Extra", 0);
            module.OnInitialize = () =>
            {
                module.OnInitialize = null;
                Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Bootstrap());
                Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Register(extra));
                Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Shutdown());
            };
            module.OnShutdown = () =>
            {
                module.OnShutdown = null;
                Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Bootstrap());
                Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Register(extra));
                FrameworkBootstrap.Shutdown();
            };
            FrameworkBootstrap.Register(module);

            FrameworkBootstrap.Bootstrap();
            Assert.Throws<InvalidOperationException>(() => FrameworkBootstrap.Register(extra));
            Assert.DoesNotThrow(() => FrameworkBootstrap.Register(module));
            FrameworkBootstrap.Shutdown();

            Assert.That(module.InitializeCount, Is.EqualTo(1));
            Assert.That(module.ShutdownCount, Is.EqualTo(1));
            Assert.That(extra.InitializeCount, Is.Zero);
        }

        [Test]
        public void EventModuleShutdown_ShouldReleaseListenersBeforeNextSession()
        {
            var events = new EventCenter();
            int oldCalls = 0;
            events.AddEventListener<int>("SessionEvent", _ => oldCalls++);
            FrameworkBootstrap.Register(events);
            FrameworkBootstrap.Bootstrap();
            events.EventTrigger("SessionEvent", 1);
            FrameworkBootstrap.Shutdown();

            FrameworkBootstrap.Register(events);
            FrameworkBootstrap.Bootstrap();
            string newValue = null;
            Assert.DoesNotThrow(() => events.AddEventListener<string>("SessionEvent", value => newValue = value));
            events.EventTrigger("SessionEvent", "next session");

            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(newValue, Is.EqualTo("next session"));
        }

        [Test]
        public void EventDispatch_ShouldApplyListenerChangesOnFollowingDispatch()
        {
            var events = new EventCenter();
            var calls = new List<string>();
            Action second = () => calls.Add("Second");
            Action added = () => calls.Add("Added");
            bool changed = false;
            events.AddEventListener("Changed", () =>
            {
                calls.Add("First");
                if (changed) return;
                changed = true;
                events.RemoveEventListener("Changed", second);
                events.AddEventListener("Changed", added);
            });
            events.AddEventListener("Changed", second);

            events.EventTrigger("Changed");
            events.EventTrigger("Changed");

            Assert.That(calls, Is.EqualTo(new[] { "First", "Second", "First", "Added" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CircularFactoryResolution_ShouldFailClearlyAndAllowRetry(bool indirect)
        {
            int calls = 0;
            bool cycle = true;
            var service = new object();
            ServiceLocator.RegisterFactory<object>(() =>
            {
                // Keep the regression test bounded even if cycle detection is removed.
                if (++calls > 1 && cycle) throw new ApplicationException("Unbounded factory recursion.");
                if (cycle)
                {
                    if (indirect) ServiceLocator.Resolve<string>();
                    else ServiceLocator.Resolve<object>();
                }
                return service;
            });
            if (indirect)
                ServiceLocator.RegisterFactory<string>(() =>
                {
                    ServiceLocator.Resolve<object>();
                    return "ready";
                });

            var failure = Assert.Throws<InvalidOperationException>(() => ServiceLocator.Resolve<object>());

            Assert.That(failure.Message, Does.Contain(typeof(object).FullName));
            Assert.That(calls, Is.EqualTo(1));
            cycle = false;
            Assert.That(ServiceLocator.Resolve<object>(), Is.SameAs(service));
            Assert.That(calls, Is.EqualTo(2));
            if (indirect) Assert.That(ServiceLocator.Resolve<string>(), Is.EqualTo("ready"));
        }

        [Test]
        public void NullFactoryResult_ShouldKeepFactoryAvailableForRetry()
        {
            object result = null;
            ServiceLocator.RegisterFactory<object>(() => result);

            var failure = Assert.Throws<InvalidOperationException>(() => ServiceLocator.Resolve<object>());

            Assert.That(failure.Message, Does.Contain("null"));
            Assert.That(ServiceLocator.IsRegistered<object>(), Is.True);
            result = new object();
            Assert.That(ServiceLocator.Resolve<object>(), Is.SameAs(result));
        }

        private sealed class LifecycleModule : IModule
        {
            private readonly List<string> calls;
            public LifecycleModule(string name, int priority, List<string> calls = null)
            {
                ModuleName = name;
                Priority = priority;
                this.calls = calls;
            }

            public string ModuleName { get; }
            public int Priority { get; }
            public ModuleState State { get; private set; }
            public int InitializeCount { get; private set; }
            public int ShutdownCount { get; private set; }
            public Action OnInitialize { get; set; }
            public Action OnPostInitialize { get; set; }
            public Action OnShutdown { get; set; }

            public void Initialize()
            {
                calls?.Add(ModuleName + ".Initialize");
                InitializeCount++;
                State = ModuleState.Initializing;
                OnInitialize?.Invoke();
                State = ModuleState.Initialized;
            }

            public void PostInitialize()
            {
                calls?.Add(ModuleName + ".PostInitialize");
                OnPostInitialize?.Invoke();
                State = ModuleState.Running;
            }

            public void Shutdown()
            {
                calls?.Add(ModuleName + ".Shutdown");
                ShutdownCount++;
                State = ModuleState.ShuttingDown;
                OnShutdown?.Invoke();
                State = ModuleState.Shutdown;
            }
        }
    }
}
