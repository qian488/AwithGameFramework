using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Gameplay.Settings;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class UserSettingsRulesTests
    {
        private static readonly string[] Languages = { "en", "fr" };

        [Test]
        public void DefaultCompletionLeavesTheSourceUntouchedAndReturnsIndependentSettings()
        {
            var source = new UserSettingsData { MasterVolume = .7f };
            var captured = new List<InputBindingSnapshot>
            {
                new InputBindingSnapshot("game", "jump", "key/space", .5f, .2f)
            };
            string before = State(source);

            var first = UserSettingsRules.CreateDefaults(source, captured);
            var second = UserSettingsRules.CreateDefaults(source, captured);

            Assert.Multiple(() =>
            {
                Assert.That(State(source), Is.EqualTo(before));
                Assert.That(first.MasterVolume, Is.EqualTo(.7f));
                Assert.That(first.BusVolumes, Is.EquivalentTo(new Dictionary<string, float> { ["sfx"] = 1, ["music"] = 1 }));
                Assert.That(first.BusMuted, Is.EquivalentTo(new Dictionary<string, bool> { ["sfx"] = false, ["music"] = false }));
                Assert.That(first.InputBindings.Single().Control, Is.EqualTo("key/space"));
                Assert.That(first.InputBindings[0].Scale, Is.EqualTo(.5f));
                Assert.That(first.InputBindings[0].DeadZone, Is.EqualTo(.2f));
            });

            first.BusVolumes["sfx"] = .1f;
            first.BusMuted["music"] = true;
            first.InputBindings[0].Control = "key/j";
            captured.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(State(source), Is.EqualTo(before));
                Assert.That(second.BusVolumes["sfx"], Is.EqualTo(1));
                Assert.That(second.BusMuted["music"], Is.False);
                Assert.That(second.InputBindings.Single().Control, Is.EqualTo("key/space"));
            });
        }

        [Test]
        public void ExplicitDefaultsKeepTheirConfiguredBusAndActionSubsetWithoutSharingNestedValues()
        {
            var source = new UserSettingsData
            {
                BusVolumes = new Dictionary<string, float> { ["dialogue"] = .6f },
                InputBindings = new List<UserInputBinding> { Binding("jump", "key/j") }
            };
            string before = State(source);
            var defaults = UserSettingsRules.CreateDefaults(source,
                new[] { new InputBindingSnapshot("game", "dynamic", "key/d") });

            Assert.Multiple(() =>
            {
                Assert.That(State(source), Is.EqualTo(before));
                Assert.That(defaults.BusVolumes.Keys, Is.EqualTo(new[] { "dialogue" }));
                Assert.That(defaults.BusMuted["dialogue"], Is.False);
                Assert.That(defaults.InputBindings.Select(item => item.Action), Is.EqualTo(new[] { "jump" }));
            });

            source.BusVolumes["dialogue"] = .2f;
            source.InputBindings[0].Control = "key/x";
            Assert.That(defaults.BusVolumes["dialogue"], Is.EqualTo(.6f));
            Assert.That(defaults.InputBindings[0].Control, Is.EqualTo("key/j"));
            defaults.BusMuted["dialogue"] = true;
            defaults.InputBindings[0].Scale = -1;
            Assert.That(source.BusMuted, Is.Empty);
            Assert.That(source.InputBindings[0].Scale, Is.EqualTo(1));
        }

        [Test]
        public void InvalidDefaultsDoNotLeavePartiallyCompletedCollectionsInTheSource()
        {
            var source = new UserSettingsData { BusMuted = new Dictionary<string, bool> { ["removed"] = true } };
            string before = State(source);

            Assert.Throws<ArgumentException>(() => UserSettingsRules.CreateDefaults(source,
                new[] { new InputBindingSnapshot("game", "jump", "key/space") }));

            Assert.That(State(source), Is.EqualTo(before));
        }

        [TestCase("fr-ca", "fr-CA")]
        [TestCase("en-us", "en-US")]
        public void NormalizationRetainsTheRequestedCultureWhenAParentDatasetSupportsIt(string requested, string expected)
        {
            var defaults = ConfiguredSettings();
            var candidate = ConfiguredSettings();
            candidate.Language = requested;
            string candidateBefore = State(candidate);
            string defaultsBefore = State(defaults);

            var normalized = UserSettingsRules.Normalize(candidate, defaults, new[] { "EN", "FR" });

            Assert.Multiple(() =>
            {
                Assert.That(normalized.Language, Is.EqualTo(expected));
                Assert.That(State(candidate), Is.EqualTo(candidateBefore));
                Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
            });
            normalized.BusVolumes["sfx"] = .1f;
            normalized.BusMuted["music"] = false;
            normalized.InputBindings[0].Control = "key/j";
            Assert.That(State(candidate), Is.EqualTo(candidateBefore));
            Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
            candidate.InputBindings[1].Control = "key/x";
            Assert.That(normalized.InputBindings[1].Control, Is.EqualTo("key/f"));
        }

        [Test]
        public void AnUnrelatedLanguageIsRejectedInsteadOfUsingTheDefaultAsAnArbitraryFallback()
        {
            var defaults = ConfiguredSettings();
            var candidate = ConfiguredSettings();
            candidate.Language = "de-DE";
            string before = State(candidate);

            Assert.Throws<ArgumentException>(() => UserSettingsRules.Normalize(candidate, defaults, Languages));

            Assert.That(State(candidate), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidationFailureDoesNotPartiallyNormalizeOrRepairTheCandidate(bool missingConfiguredBus)
        {
            var defaults = ConfiguredSettings();
            var candidate = ConfiguredSettings();
            candidate.Language = "fr-ca";
            if (missingConfiguredBus) candidate.BusVolumes.Remove("music");
            string candidateBefore = State(candidate);
            string defaultsBefore = State(defaults);
            var availableBuses = missingConfiguredBus ? new[] { "sfx", "music" } : new[] { "sfx" };

            Assert.Throws<ArgumentException>(() => UserSettingsRules.Normalize(candidate, defaults, Languages, availableBuses));

            Assert.That(State(candidate), Is.EqualTo(candidateBefore));
            Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
        }

        [Test]
        public void RestoreMergesNewDefaultsWithSavedPreferencesWithoutSharingEitherInput()
        {
            var defaults = ConfiguredSettings();
            var saved = new UserSettingsData
            {
                Language = "fr-ca", MasterVolume = .3f, MasterMuted = true,
                BusVolumes = new Dictionary<string, float> { ["sfx"] = .2f },
                BusMuted = new Dictionary<string, bool> { ["sfx"] = true },
                InputBindings = new List<UserInputBinding> { Binding("jump", "key/j") }
            };
            string defaultsBefore = State(defaults);
            string savedBefore = State(saved);

            var restored = UserSettingsRules.Restore(defaults, saved, Languages);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Language, Is.EqualTo("fr-CA"));
                Assert.That(restored.MasterVolume, Is.EqualTo(.3f));
                Assert.That(restored.MasterMuted, Is.True);
                Assert.That(restored.BusVolumes["sfx"], Is.EqualTo(.2f));
                Assert.That(restored.BusMuted["sfx"], Is.True);
                Assert.That(restored.BusVolumes["music"], Is.EqualTo(.6f));
                Assert.That(restored.BusMuted["music"], Is.True);
                Assert.That(restored.InputBindings.Single(item => item.Action == "jump").Control, Is.EqualTo("key/j"));
                Assert.That(restored.InputBindings.Single(item => item.Action == "fire").Control, Is.EqualTo("key/f"));
                Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
                Assert.That(State(saved), Is.EqualTo(savedBefore));
            });

            restored.BusVolumes["sfx"] = .9f;
            restored.BusMuted["music"] = false;
            restored.InputBindings.Single(item => item.Action == "fire").Control = "key/x";
            Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
            Assert.That(State(saved), Is.EqualTo(savedBefore));
            defaults.BusVolumes["music"] = .1f;
            saved.InputBindings[0].Control = "key/k";
            Assert.That(restored.BusVolumes["music"], Is.EqualTo(.6f));
            Assert.That(restored.InputBindings.Single(item => item.Action == "jump").Control, Is.EqualTo("key/j"));
        }

        [Test]
        public void RestoreWithoutSavedDataReturnsIndependentDefaultsEachTime()
        {
            var defaults = ConfiguredSettings();
            string before = State(defaults);
            var first = UserSettingsRules.Restore(defaults, null, Languages);
            var second = UserSettingsRules.Restore(defaults, null, Languages);

            first.BusVolumes.Clear();
            first.BusMuted.Clear();
            first.InputBindings[0].Control = "key/j";

            Assert.That(State(defaults), Is.EqualTo(before));
            Assert.That(State(second), Is.EqualTo(before));
        }

        [TestCase("volume")]
        [TestCase("mute")]
        [TestCase("action")]
        public void RestoreRejectsRemovedItemsWithoutChangingDefaultsOrTheSavedData(string removedItem)
        {
            var defaults = ConfiguredSettings();
            var saved = new UserSettingsData { Language = "fr-ca" };
            if (removedItem == "volume") saved.BusVolumes.Add("removed", .5f);
            if (removedItem == "mute") saved.BusMuted.Add("removed", true);
            if (removedItem == "action") saved.InputBindings.Add(Binding("removed", "key/x"));
            string defaultsBefore = State(defaults);
            string savedBefore = State(saved);

            Assert.Throws<ArgumentException>(() => UserSettingsRules.Restore(defaults, saved, Languages));

            Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
            Assert.That(State(saved), Is.EqualTo(savedBefore));
        }

        [Test]
        public void StructuralComparisonIgnoresDeclarationOrderButDetectsNestedPreferenceChanges()
        {
            var first = ConfiguredSettings();
            var reordered = ConfiguredSettings();
            reordered.BusVolumes = new Dictionary<string, float> { ["music"] = .6f, ["sfx"] = .8f };
            reordered.BusMuted = new Dictionary<string, bool> { ["music"] = true, ["sfx"] = false };
            reordered.InputBindings.Reverse();
            string firstBefore = State(first);
            string reorderedBefore = State(reordered);

            Assert.That(UserSettingsRules.AreEquivalent(first, reordered), Is.True);
            Assert.That(UserSettingsRules.HaveSameBindings(first.InputBindings, reordered.InputBindings), Is.True);
            Assert.That(State(first), Is.EqualTo(firstBefore));
            Assert.That(State(reordered), Is.EqualTo(reorderedBefore));

            reordered.InputBindings[0].Scale = -1;
            Assert.That(UserSettingsRules.HaveSameBindings(first.InputBindings, reordered.InputBindings), Is.False);
            Assert.That(UserSettingsRules.AreEquivalent(first, reordered), Is.False);
            reordered.InputBindings[0].Scale = 1;
            reordered.BusMuted["music"] = false;
            Assert.That(UserSettingsRules.HaveSameBindings(first.InputBindings, reordered.InputBindings), Is.True);
            Assert.That(UserSettingsRules.AreEquivalent(first, reordered), Is.False);
        }

        [Test]
        public void BatchBindingSwapPreservesDynamicActionsAndProducesAnIndependentCompleteSnapshot()
        {
            var settings = ConfiguredSettings();
            settings.InputBindings[0].Control = "key/f";
            settings.InputBindings[1].Control = "key/space";
            var runtime = RuntimeBindings();
            string settingsBefore = State(settings);
            string runtimeBefore = JsonSerializer.Serialize(runtime);

            var bindings = UserSettingsRules.BuildInputBindings(settings, runtime);

            Assert.That(bindings.Select(item => (item.ContextId, item.Action, item.Control, item.Scale, item.DeadZone)), Is.EqualTo(new[]
            {
                ("game", "jump", "key/f", 1f, .1f),
                ("game", "fire", "key/space", 1f, .1f),
                ("game", "dynamic", "key/d", -.5f, .2f),
                ("ui", "accept", "key/space", 1f, .1f)
            }));
            Assert.That(State(settings), Is.EqualTo(settingsBefore));
            Assert.That(JsonSerializer.Serialize(runtime), Is.EqualTo(runtimeBefore));
            Assert.That(bindings[2], Is.Not.SameAs(runtime[2]));

            settings.InputBindings[0].Control = "key/x";
            runtime.Clear();
            Assert.That(bindings.Count, Is.EqualTo(4));
            Assert.That(bindings[0].Control, Is.EqualTo("key/f"));
            Assert.That(bindings[2].Control, Is.EqualTo("key/d"));
        }

        [Test]
        public void BindingConflictWithAnUnconfiguredDynamicActionRejectsTheWholePlan()
        {
            var settings = ConfiguredSettings();
            settings.InputBindings[0].Control = "key/d";
            var runtime = RuntimeBindings();
            string settingsBefore = State(settings);
            string runtimeBefore = JsonSerializer.Serialize(runtime);

            Assert.Throws<ArgumentException>(() => UserSettingsRules.BuildInputBindings(settings, runtime));
            Assert.Throws<ArgumentException>(() => UserSettingsRules.Normalize(settings, ConfiguredSettings(), Languages,
                runtimeBindings: runtime));

            Assert.That(State(settings), Is.EqualTo(settingsBefore));
            Assert.That(JsonSerializer.Serialize(runtime), Is.EqualTo(runtimeBefore));
        }

        [TestCase("empty-action")]
        [TestCase("non-finite-scale")]
        [TestCase("duplicate-action")]
        public void MalformedDynamicRuntimeBindingsAreRejectedWithoutChangingAnyInput(string invalidBinding)
        {
            var settings = ConfiguredSettings();
            var defaults = ConfiguredSettings();
            var runtime = RuntimeBindings();
            if (invalidBinding == "empty-action")
                runtime[2] = new InputBindingSnapshot("game", "", "key/d");
            if (invalidBinding == "non-finite-scale")
                runtime[2] = new InputBindingSnapshot("game", "dynamic", "key/d", float.NaN);
            if (invalidBinding == "duplicate-action")
                runtime.Add(new InputBindingSnapshot("game", "dynamic", "key/other"));
            var jsonOptions = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            string settingsBefore = State(settings);
            string defaultsBefore = State(defaults);
            string runtimeBefore = JsonSerializer.Serialize(runtime, jsonOptions);

            Assert.Throws<ArgumentException>(() => UserSettingsRules.BuildInputBindings(settings, runtime));
            Assert.Throws<ArgumentException>(() => UserSettingsRules.Normalize(settings, defaults, Languages,
                runtimeBindings: runtime));

            Assert.That(State(settings), Is.EqualTo(settingsBefore));
            Assert.That(State(defaults), Is.EqualTo(defaultsBefore));
            Assert.That(JsonSerializer.Serialize(runtime, jsonOptions), Is.EqualTo(runtimeBefore));
        }

        [Test]
        public void AnEmptyRuntimeSnapshotMeansConfiguredActionsAreMissingRatherThanInputBeingDisconnected()
        {
            var settings = ConfiguredSettings();
            var defaults = ConfiguredSettings();
            string before = State(settings);

            Assert.DoesNotThrow(() => UserSettingsRules.Normalize(settings, defaults, Languages));
            Assert.Throws<ArgumentException>(() => UserSettingsRules.Normalize(settings, defaults, Languages,
                runtimeBindings: Array.Empty<InputBindingSnapshot>()));

            Assert.That(State(settings), Is.EqualTo(before));
        }

        private static UserSettingsData ConfiguredSettings() => new UserSettingsData
        {
            Language = "en", MasterVolume = .9f,
            BusVolumes = new Dictionary<string, float> { ["sfx"] = .8f, ["music"] = .6f },
            BusMuted = new Dictionary<string, bool> { ["sfx"] = false, ["music"] = true },
            InputBindings = new List<UserInputBinding> { Binding("jump", "key/space"), Binding("fire", "key/f") }
        };

        private static UserInputBinding Binding(string action, string control) => new UserInputBinding
        { ContextId = "game", Action = action, Control = control };

        private static List<InputBindingSnapshot> RuntimeBindings() => new List<InputBindingSnapshot>
        {
            new InputBindingSnapshot("game", "jump", "key/space"),
            new InputBindingSnapshot("game", "fire", "key/f"),
            new InputBindingSnapshot("game", "dynamic", "key/d", -.5f, .2f),
            new InputBindingSnapshot("ui", "accept", "key/space")
        };

        private static string State(UserSettingsData data) => JsonSerializer.Serialize(data);
    }
}
