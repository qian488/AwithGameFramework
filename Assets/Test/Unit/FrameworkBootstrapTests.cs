using NUnit.Framework;
using System.Collections.Generic;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Bootstrap;

namespace AwithGameFrame.Tests.Unit
{
    /// <summary>
    /// 用于测试的模拟模块
    /// </summary>
    internal class TestModule : IModule
    {
        private ModuleState _state = ModuleState.None;
        public string ModuleName { get; }
        public int Priority { get; }
        public ModuleState State => _state;

        public bool InitializeWasCalled { get; private set; }
        public bool PostInitWasCalled { get; private set; }
        public bool ShutdownWasCalled { get; private set; }
        public int InitOrder { get; private set; }
        public int PostInitOrder { get; private set; }

        private static int _initCounter;
        private static int _postInitCounter;

        public static void ResetCounters()
        {
            _initCounter = 0;
            _postInitCounter = 0;
        }

        public TestModule(string name, int priority)
        {
            ModuleName = name;
            Priority = priority;
        }

        public void Initialize()
        {
            InitializeWasCalled = true;
            _state = ModuleState.Initialized;
            InitOrder = ++_initCounter;
        }

        public void PostInitialize()
        {
            PostInitWasCalled = true;
            _state = ModuleState.Running;
            PostInitOrder = ++_postInitCounter;
        }

        public void Shutdown()
        {
            ShutdownWasCalled = true;
            _state = ModuleState.Shutdown;
        }
    }

    [TestFixture]
    public class FrameworkBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            TestModule.ResetCounters();
            FrameworkBootstrap.Shutdown();
        }

        [Test]
        public void Register_ShouldAddModule()
        {
            var module = new TestModule("TestA", 10);
            FrameworkBootstrap.Register(module);

            Assert.AreEqual(1, FrameworkBootstrap.RegisteredModules.Count);
            Assert.AreSame(module, FrameworkBootstrap.RegisteredModules[0]);
        }

        [Test]
        public void Register_ShouldSkipNullModule()
        {
            FrameworkBootstrap.Register((IModule)null);

            Assert.AreEqual(0, FrameworkBootstrap.RegisteredModules.Count);
        }

        [Test]
        public void Register_ShouldSkipDuplicateModule()
        {
            var module = new TestModule("TestA", 10);
            FrameworkBootstrap.Register(module);
            FrameworkBootstrap.Register(module);

            Assert.AreEqual(1, FrameworkBootstrap.RegisteredModules.Count);
        }

        [Test]
        public void Bootstrap_ShouldInitializeModules_InPriorityOrder()
        {
            var low = new TestModule("LowPriority", 10);
            var high = new TestModule("HighPriority", 100);
            var mid = new TestModule("MidPriority", 50);

            FrameworkBootstrap.Register(low);
            FrameworkBootstrap.Register(high);
            FrameworkBootstrap.Register(mid);
            FrameworkBootstrap.Bootstrap();

            Assert.IsTrue(high.InitializeWasCalled);
            Assert.IsTrue(mid.InitializeWasCalled);
            Assert.IsTrue(low.InitializeWasCalled);

            Assert.AreEqual(1, high.InitOrder, "High priority should init first");
            Assert.AreEqual(2, mid.InitOrder);
            Assert.AreEqual(3, low.InitOrder, "Low priority should init last");
        }

        [Test]
        public void Bootstrap_ShouldCallPostInitialize_AfterAllInitialize()
        {
            var moduleA = new TestModule("A", 10);
            var moduleB = new TestModule("B", 20);

            FrameworkBootstrap.Register(moduleA);
            FrameworkBootstrap.Register(moduleB);
            FrameworkBootstrap.Bootstrap();

            Assert.IsTrue(moduleA.PostInitWasCalled);
            Assert.IsTrue(moduleB.PostInitWasCalled);
        }

        [Test]
        public void Shutdown_ShouldCallAllModules_InReverseOrder()
        {
            var moduleA = new TestModule("A", 10);
            var moduleB = new TestModule("B", 20);

            FrameworkBootstrap.Register(moduleA);
            FrameworkBootstrap.Register(moduleB);
            FrameworkBootstrap.Bootstrap();
            FrameworkBootstrap.Shutdown();

            Assert.IsTrue(moduleA.ShutdownWasCalled);
            Assert.IsTrue(moduleB.ShutdownWasCalled);
        }
    }
}
