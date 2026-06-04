using NUnit.Framework;
using AwithGameFrame.Core.DI;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class ServiceLocatorTests
    {
        interface ITestService { string GetName(); }
        class TestService : ITestService { public string GetName() => "test"; }
        class OtherService : ITestService { public string GetName() => "other"; }

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
        }

        [Test]
        public void Register_ShouldStoreInstance()
        {
            var service = new TestService();
            ServiceLocator.Register<ITestService>(service);

            Assert.IsTrue(ServiceLocator.IsRegistered<ITestService>());
        }

        [Test]
        public void Resolve_ShouldReturnRegisteredInstance()
        {
            var service = new TestService();
            ServiceLocator.Register<ITestService>(service);

            var resolved = ServiceLocator.Resolve<ITestService>();

            Assert.AreSame(service, resolved);
        }

        [Test]
        public void Resolve_ShouldThrow_WhenNotRegistered()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                ServiceLocator.Resolve<ITestService>();
            });
        }

        [Test]
        public void TryResolve_ShouldReturnNull_WhenNotRegistered()
        {
            var result = ServiceLocator.TryResolve<ITestService>();
            Assert.IsNull(result);
        }

        [Test]
        public void TryResolve_ShouldReturnInstance_WhenRegistered()
        {
            ServiceLocator.Register<ITestService>(new TestService());
            var result = ServiceLocator.TryResolve<ITestService>();
            Assert.IsNotNull(result);
            Assert.AreEqual("test", result.GetName());
        }

        [Test]
        public void Replace_ShouldOverwritePreviousRegistration()
        {
            ServiceLocator.Register<ITestService>(new TestService());
            var replacement = new OtherService();
            ServiceLocator.Register<ITestService>(replacement);

            var resolved = ServiceLocator.Resolve<ITestService>();
            Assert.AreSame(replacement, resolved);
            Assert.AreEqual("other", resolved.GetName());
        }

        [Test]
        public void Unregister_ShouldRemoveService()
        {
            ServiceLocator.Register<ITestService>(new TestService());
            ServiceLocator.Unregister<ITestService>();

            Assert.IsFalse(ServiceLocator.IsRegistered<ITestService>());
        }

        [Test]
        public void Factory_ShouldBeCalledOnFirstResolve()
        {
            int callCount = 0;
            ServiceLocator.RegisterFactory<ITestService>(() =>
            {
                callCount++;
                return new TestService();
            });

            Assert.IsTrue(ServiceLocator.IsRegistered<ITestService>());
            Assert.AreEqual(0, callCount);

            var resolved = ServiceLocator.Resolve<ITestService>();
            Assert.AreEqual(1, callCount);
            Assert.IsNotNull(resolved);
        }

        [Test]
        public void Factory_ShouldBeCalledOnlyOnce()
        {
            int callCount = 0;
            ServiceLocator.RegisterFactory<ITestService>(() =>
            {
                callCount++;
                return new TestService();
            });

            ServiceLocator.Resolve<ITestService>();
            ServiceLocator.Resolve<ITestService>();
            ServiceLocator.Resolve<ITestService>();

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Factory_ShouldReturnSameInstanceOnSubsequentResolve()
        {
            ServiceLocator.RegisterFactory<ITestService>(() => new TestService());

            var first = ServiceLocator.Resolve<ITestService>();
            var second = ServiceLocator.Resolve<ITestService>();

            Assert.AreSame(first, second);
        }

        [Test]
        public void RegisterInstance_ShouldOverwriteFactory()
        {
            ServiceLocator.RegisterFactory<ITestService>(() => new TestService());
            var instance = new OtherService();
            ServiceLocator.Register<ITestService>(instance);

            var resolved = ServiceLocator.Resolve<ITestService>();
            Assert.AreSame(instance, resolved);
        }

        [Test]
        public void Clear_ShouldRemoveAllRegistrations()
        {
            ServiceLocator.Register<ITestService>(new TestService());
            ServiceLocator.Register<string>("value");

            ServiceLocator.Clear();

            Assert.IsFalse(ServiceLocator.IsRegistered<ITestService>());
            Assert.IsFalse(ServiceLocator.IsRegistered<string>());
        }

        [Test]
        public void IsRegistered_ShouldReturnFalse_ForUnregisteredType()
        {
            Assert.IsFalse(ServiceLocator.IsRegistered<string>());
        }
    }
}
