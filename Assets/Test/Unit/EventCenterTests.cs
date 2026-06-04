using NUnit.Framework;
using UnityEngine.Events;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Event;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class EventCenterTests
    {
        [SetUp]
        public void SetUp()
        {
            EventCenter.GetInstance().Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EventCenter.GetInstance().Clear();
        }

        #region 无参数事件

        [Test]
        public void AddEventListener_And_Trigger_ShouldInvokeCallback()
        {
            bool wasCalled = false;
            EventCenter.GetInstance().AddEventListener("TestEvent", () => wasCalled = true);
            EventCenter.GetInstance().EventTrigger("TestEvent");

            Assert.IsTrue(wasCalled);
        }

        [Test]
        public void RemoveEventListener_ShouldPreventTrigger()
        {
            bool wasCalled = false;
            UnityAction action = () => wasCalled = true;

            EventCenter.GetInstance().AddEventListener("TestEvent", action);
            EventCenter.GetInstance().RemoveEventListener("TestEvent", action);
            EventCenter.GetInstance().EventTrigger("TestEvent");

            Assert.IsFalse(wasCalled);
        }

        [Test]
        public void AddEventListener_MultipleCallbacks_ShouldAllBeInvoked()
        {
            int callCount = 0;
            EventCenter.GetInstance().AddEventListener("TestEvent", () => callCount++);
            EventCenter.GetInstance().AddEventListener("TestEvent", () => callCount++);
            EventCenter.GetInstance().AddEventListener("TestEvent", () => callCount++);
            EventCenter.GetInstance().EventTrigger("TestEvent");

            Assert.AreEqual(3, callCount);
        }

        #endregion

        #region 带参数事件

        [Test]
        public void AddEventListener_WithOneParam_ShouldReceiveParam()
        {
            string received = null;
            EventCenter.GetInstance().AddEventListener<string>("TestParam", (s) => received = s);
            EventCenter.GetInstance().EventTrigger("TestParam", "hello");

            Assert.AreEqual("hello", received);
        }

        [Test]
        public void AddEventListener_WithTwoParams_ShouldReceiveBothParams()
        {
            string received1 = null;
            int received2 = 0;
            EventCenter.GetInstance().AddEventListener<string, int>("TestParams",
                (s, i) => { received1 = s; received2 = i; });
            EventCenter.GetInstance().EventTrigger("TestParams", "text", 42);

            Assert.AreEqual("text", received1);
            Assert.AreEqual(42, received2);
        }

        #endregion

        #region EventId 类型安全重载

        [Test]
        public void EventId_AddEventListener_ShouldWork()
        {
            bool wasCalled = false;
            EventCenter.GetInstance().AddEventListener(FrameworkEvents.SceneLoaded, () => wasCalled = true);
            EventCenter.GetInstance().EventTrigger(FrameworkEvents.SceneLoaded);

            Assert.IsTrue(wasCalled);
        }

        [Test]
        public void EventId_AddEventListener_WithParam_ShouldWork()
        {
            string received = null;
            EventCenter.GetInstance().AddEventListener<string>(FrameworkEvents.KeyDown, (s) => received = s);
            EventCenter.GetInstance().EventTrigger(FrameworkEvents.KeyDown, "Space");

            Assert.AreEqual("Space", received);
        }

        [Test]
        public void EventId_ShouldBeCompatible_WithStringApi()
        {
            bool wasCalled = false;
            // Add via EventId, trigger via string
            EventCenter.GetInstance().AddEventListener(FrameworkEvents.SceneLoaded, () => wasCalled = true);
            EventCenter.GetInstance().EventTrigger("SceneLoaded");

            Assert.IsTrue(wasCalled);
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ShouldRemoveAllListeners()
        {
            bool wasCalled = false;
            EventCenter.GetInstance().AddEventListener("TestEvent", () => wasCalled = true);
            EventCenter.GetInstance().Clear();
            EventCenter.GetInstance().EventTrigger("TestEvent");

            Assert.IsFalse(wasCalled);
        }

        #endregion
    }
}
