using System;
using System.Collections.Generic;
using System.Linq;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class ViewLayoutRulesTests
    {
        [Test]
        public void LayoutOrdersLayersAndCreationIdsWithoutChangingSourceAndReturnsIndependentResult()
        {
            var source = new List<ViewLayoutItem>
            {
                Item(7, int.MaxValue), Item(5, 0), Item(1, int.MinValue), Item(3, 0)
            };
            var original = source.ToArray();
            var result = ViewLayoutRules.Calculate(source);
            Assert.That(source, Is.EqualTo(original));
            Assert.That(result.Select(entry => entry.InstanceId), Is.EqualTo(new long[] { 1, 3, 5, 7 }));
            Assert.That(result.Select(entry => entry.State.Order), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(result.All(entry => entry.State.IsInputEnabled && !entry.State.IsCovered), Is.True);
            Assert.That(result.Where(entry => entry.State.IsFocused).Select(entry => entry.InstanceId), Is.EqualTo(new long[] { 7 }));
            source.Clear();
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.Throws<NotSupportedException>(() => ((IList<ViewLayoutEntry>)result).Clear());
        }

        [Test]
        public void HighestModalBlocksLowerViewsAndAllowsFocusOnlyAtOrAboveItsLayerOrder()
        {
            var result = ViewLayoutRules.Calculate(new[]
            {
                Item(1, 0), Item(2, 10, modal: true), Item(3, 20, modal: true, focus: false), Item(4, 30, focus: false)
            });
            Assert.That(result.Select(entry => entry.State.IsCovered), Is.EqualTo(new[] { true, true, false, false }));
            Assert.That(result.Select(entry => entry.State.IsInputEnabled), Is.EqualTo(new[] { false, false, true, true }));
            Assert.That(result.Any(entry => entry.State.IsFocused), Is.False);
            var focusedOverlay = ViewLayoutRules.Calculate(new[] { Item(1, 0), Item(3, 20, modal: true, focus: false), Item(4, 30) });
            Assert.That(focusedOverlay.Single(entry => entry.State.IsFocused).InstanceId, Is.EqualTo(4));
            var focusRestored = ViewLayoutRules.Calculate(new[] { Item(1, 0), Item(4, 30, focus: false) });
            Assert.That(focusRestored.Single(entry => entry.State.IsFocused).InstanceId, Is.EqualTo(1));
        }

        [Test]
        public void EmptyAndNoFocusLayoutsHaveNoFocusedEntryAndInvalidSnapshotsAreRejected()
        {
            Assert.That(ViewLayoutRules.Calculate(Array.Empty<ViewLayoutItem>()), Is.Empty);
            Assert.That(ViewLayoutRules.Calculate(new[] { Item(1, 0, focus: false) })[0].State.IsFocused, Is.False);
            Assert.Throws<ArgumentNullException>(() => ViewLayoutRules.Calculate(null));
            Assert.Throws<ArgumentException>(() => ViewLayoutRules.Calculate(new[] { Item(1, 0), null }));
            Assert.Throws<ArgumentException>(() => ViewLayoutRules.Calculate(new[] { Item(1, 0), Item(1, 10) }));
        }

        [TestCase(typeof(ViewHandle))]
        [TestCase(typeof(SceneHandle))]
        public void ApplicationHandlesDoNotExposeNativeInstancesOrOwnedResourceScopes(Type handleType)
        {
            foreach (var property in handleType.GetProperties())
            {
                Assert.That(typeof(ResourceScope).IsAssignableFrom(property.PropertyType), Is.False, property.Name);
                Assert.That(typeof(IView).IsAssignableFrom(property.PropertyType), Is.False, property.Name);
                Assert.That(typeof(ISceneInstance).IsAssignableFrom(property.PropertyType), Is.False, property.Name);
                Assert.That(property.SetMethod?.IsPublic == true, Is.False, property.Name);
            }
        }

        private static ViewLayoutItem Item(long id, int layer, bool modal = false, bool focus = true)
            => new ViewLayoutItem(id, new ViewRequest("view-" + id, layer, modal, focus));
    }
}
