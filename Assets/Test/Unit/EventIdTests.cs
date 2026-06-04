using NUnit.Framework;
using AwithGameFrame.Core.Event;
using System.Collections.Generic;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class EventIdTests
    {
        [Test]
        public void Constructor_ShouldStoreValue()
        {
            var eventId = new EventId("TestEvent");
            Assert.AreEqual("TestEvent", eventId.Value);
        }

        [Test]
        public void Constructor_ShouldThrow_OnNullValue()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
            {
                new EventId(null);
            });
        }

        [Test]
        public void ImplicitOperator_ToString_ShouldReturnValue()
        {
            var eventId = new EventId("MyEvent");
            string str = eventId; // Test implicit conversion
            Assert.AreEqual("MyEvent", str);
        }

        [Test]
        public void Equals_WithSameValue_ShouldReturnTrue()
        {
            var a = new EventId("Event");
            var b = new EventId("Event");

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
        }

        [Test]
        public void Equals_WithDifferentValue_ShouldReturnFalse()
        {
            var a = new EventId("EventA");
            var b = new EventId("EventB");

            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_WithObject_ShouldWork()
        {
            var a = new EventId("Event");
            object b = new EventId("Event");

            Assert.IsTrue(a.Equals(b));
        }

        [Test]
        public void Equals_WithDifferentType_ShouldReturnFalse()
        {
            var a = new EventId("Event");
            Assert.IsFalse(a.Equals("Event"));
        }

        [Test]
        public void GetHashCode_ShouldBeSame_ForSameValue()
        {
            var a = new EventId("Event");
            var b = new EventId("Event");

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void GetHashCode_ShouldBeDifferent_ForDifferentValue()
        {
            var a = new EventId("EventA");
            var b = new EventId("EventB");

            Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ShouldReturnValue()
        {
            var eventId = new EventId("MyEvent");
            Assert.AreEqual("MyEvent", eventId.ToString());
        }

        [Test]
        public void FrameworkEvents_ShouldBeUnique()
        {
            // All predefined events should have unique values
            var events = new EventId[]
            {
                FrameworkEvents.ConfigChanged,
                FrameworkEvents.ConfigReloaded,
                FrameworkEvents.SceneLoading,
                FrameworkEvents.SceneLoaded,
                FrameworkEvents.KeyDown,
                FrameworkEvents.KeyUp,
                FrameworkEvents.FrameworkInitialized,
                FrameworkEvents.FrameworkShutdown,
                FrameworkEvents.ApplicationPause,
                FrameworkEvents.ApplicationResume,
                FrameworkEvents.ModuleInitialized,
                FrameworkEvents.ModuleShutdown,
            };

            var seen = new HashSet<string>();
            foreach (var evt in events)
            {
                Assert.IsTrue(seen.Add(evt.Value), $"Duplicate EventId: {evt.Value}");
            }
        }

        [Test]
        public void AsDictionaryKey_ShouldWork()
        {
            var dict = new Dictionary<EventId, string>();
            dict[FrameworkEvents.KeyDown] = "pressed";
            dict[FrameworkEvents.KeyUp] = "released";

            Assert.AreEqual("pressed", dict[new EventId("KeyDown")]);
            Assert.AreEqual("released", dict[new EventId("KeyUp")]);
        }
    }
}
