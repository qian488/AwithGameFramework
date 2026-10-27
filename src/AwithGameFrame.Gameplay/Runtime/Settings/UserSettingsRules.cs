using System;
using System.Collections.Generic;
using System.Globalization;
using AwithGameFrame.Gameplay.Input;

namespace AwithGameFrame.Gameplay.Settings
{
    /// <summary>Settings calculations over caller-provided snapshots, without applying or saving them.</summary>
    public static class UserSettingsRules
    {
        /// <summary>Copies defaults, fills conventional buses and missing mute values, and copies captured bindings when none were supplied.</summary>
        public static UserSettingsData CreateDefaults(UserSettingsData source, IReadOnlyList<InputBindingSnapshot> capturedBindings = null)
        {
            var defaults = Copy(source);
            if (defaults.BusVolumes.Count == 0)
            {
                defaults.BusVolumes.Add("sfx", 1);
                defaults.BusVolumes.Add("music", 1);
            }
            foreach (string bus in defaults.BusVolumes.Keys)
                if (!defaults.BusMuted.ContainsKey(bus)) defaults.BusMuted.Add(bus, false);
            foreach (string bus in defaults.BusMuted.Keys)
                if (!defaults.BusVolumes.ContainsKey(bus))
                    throw new ArgumentException("A muted settings bus must have a configured volume.", nameof(source));
            if (capturedBindings != null && defaults.InputBindings.Count == 0)
                foreach (var binding in capturedBindings) defaults.InputBindings.Add(FromBinding(binding));
            ValidateShape(defaults);
            return defaults;
        }

        /// <summary>Merges saved values with established defaults, then validates and normalizes an independent candidate.</summary>
        public static UserSettingsData Restore(UserSettingsData defaults, UserSettingsData saved,
            IReadOnlyList<string> supportedLanguages, IReadOnlyCollection<string> availableAudioBuses = null,
            IReadOnlyList<InputBindingSnapshot> runtimeBindings = null)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            var restored = saved == null ? Copy(defaults) : Merge(defaults, saved);
            return NormalizeOwned(restored, defaults, supportedLanguages, availableAudioBuses, runtimeBindings);
        }

        /// <summary>Copies a complete candidate and validates it against explicit catalog, audio and input facts.</summary>
        /// <remarks>Null audio or input facts mean that service is absent. Supplied snapshots must remain stable during the call.</remarks>
        public static UserSettingsData Normalize(UserSettingsData candidate, UserSettingsData defaults,
            IReadOnlyList<string> supportedLanguages, IReadOnlyCollection<string> availableAudioBuses = null,
            IReadOnlyList<InputBindingSnapshot> runtimeBindings = null)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            return NormalizeOwned(Copy(candidate), defaults, supportedLanguages, availableAudioBuses, runtimeBindings);
        }

        /// <summary>Overlays configurable actions on a complete runtime snapshot, preserving additional actions and rejecting conflicts.</summary>
        public static IReadOnlyList<InputBindingSnapshot> BuildInputBindings(UserSettingsData settings,
            IReadOnlyList<InputBindingSnapshot> runtimeBindings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (runtimeBindings == null) throw new ArgumentNullException(nameof(runtimeBindings));
            if (settings.InputBindings == null) throw new ArgumentException("Settings collections cannot be null.", nameof(settings));
            ValidateBindings(settings.InputBindings);
            var result = new List<InputBindingSnapshot>(runtimeBindings.Count);
            foreach (var binding in runtimeBindings)
            {
                if (binding == null) throw new ArgumentException("A binding snapshot cannot contain null.", nameof(runtimeBindings));
                result.Add(new InputBindingSnapshot(binding.ContextId, binding.Action, binding.Control, binding.Scale, binding.DeadZone));
            }
            foreach (var binding in settings.InputBindings)
            {
                int index = result.FindIndex(item => item.ContextId == binding.ContextId && item.Action == binding.Action);
                if (index < 0) throw new ArgumentException("A settings input context/action has not been created.");
                result[index] = new InputBindingSnapshot(binding.ContextId, binding.Action, binding.Control, binding.Scale, binding.DeadZone);
            }
            // Validate the final layout so settings can replace conflicting old controls atomically.
            var actions = new HashSet<string>(StringComparer.Ordinal);
            var controls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in result)
                ValidateBinding(binding.ContextId, binding.Action, binding.Control, binding.Scale, binding.DeadZone, actions, controls);
            return result.AsReadOnly();
        }

        /// <summary>Compares settings structurally; dictionary and binding declaration order do not matter.</summary>
        public static bool AreEquivalent(UserSettingsData a, UserSettingsData b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.BusVolumes == null || b.BusVolumes == null || a.BusMuted == null || b.BusMuted == null)
                return false;
            if (a.Language != b.Language || a.MasterVolume != b.MasterVolume || a.MasterMuted != b.MasterMuted ||
                a.BusVolumes.Count != b.BusVolumes.Count || a.BusMuted.Count != b.BusMuted.Count || !HaveSameBindings(a.InputBindings, b.InputBindings))
                return false;
            foreach (var pair in a.BusVolumes)
                if (!ContainsOrdinal(b.BusVolumes.Keys, pair.Key) || !b.BusVolumes.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
            foreach (var pair in a.BusMuted)
                if (!ContainsOrdinal(b.BusMuted.Keys, pair.Key) || !b.BusMuted.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
            return true;
        }

        /// <summary>Compares valid binding sets by context/action and values, independently of their order.</summary>
        public static bool HaveSameBindings(IReadOnlyList<UserInputBinding> a, IReadOnlyList<UserInputBinding> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var left in a)
            {
                if (left == null) return false;
                UserInputBinding right = null;
                foreach (var item in b)
                    if (item != null && item.ContextId == left.ContextId && item.Action == left.Action) { right = item; break; }
                if (right == null || right.Control != left.Control || right.Scale != left.Scale || right.DeadZone != left.DeadZone) return false;
            }
            return true;
        }

        internal static UserSettingsData Copy(UserSettingsData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.BusVolumes == null || source.BusMuted == null || source.InputBindings == null)
                throw new ArgumentException("Settings collections cannot be null.", nameof(source));
            var copy = new UserSettingsData
            {
                Language = source.Language,
                MasterVolume = source.MasterVolume,
                MasterMuted = source.MasterMuted,
                BusVolumes = new Dictionary<string, float>(source.BusVolumes, StringComparer.Ordinal),
                BusMuted = new Dictionary<string, bool>(source.BusMuted, StringComparer.Ordinal)
            };
            foreach (var binding in source.InputBindings) copy.InputBindings.Add(CopyBinding(binding));
            return copy;
        }

        private static UserSettingsData Merge(UserSettingsData defaults, UserSettingsData saved)
        {
            ValidateShape(saved);
            var merged = Copy(defaults);
            merged.Language = saved.Language;
            merged.MasterVolume = saved.MasterVolume;
            merged.MasterMuted = saved.MasterMuted;
            foreach (var pair in saved.BusVolumes)
            {
                if (!merged.BusVolumes.ContainsKey(pair.Key)) throw new ArgumentException("Saved settings contain an unknown audio bus: " + pair.Key);
                merged.BusVolumes[pair.Key] = pair.Value;
            }
            foreach (var pair in saved.BusMuted)
            {
                if (!merged.BusMuted.ContainsKey(pair.Key)) throw new ArgumentException("Saved settings contain an unknown audio bus: " + pair.Key);
                merged.BusMuted[pair.Key] = pair.Value;
            }
            foreach (var binding in saved.InputBindings)
            {
                int index = merged.InputBindings.FindIndex(item => item.ContextId == binding.ContextId && item.Action == binding.Action);
                if (index < 0) throw new ArgumentException("Saved settings contain an unknown input action.");
                merged.InputBindings[index] = CopyBinding(binding);
            }
            return merged;
        }

        private static UserSettingsData NormalizeOwned(UserSettingsData candidate, UserSettingsData defaults,
            IReadOnlyList<string> supportedLanguages, IReadOnlyCollection<string> availableAudioBuses,
            IReadOnlyList<InputBindingSnapshot> runtimeBindings)
        {
            if (supportedLanguages == null) throw new ArgumentNullException(nameof(supportedLanguages));
            ValidateShape(defaults);
            ValidateShape(candidate);
            string normalizedLanguage = NormalizeLanguage(candidate.Language);
            bool supported = false;
            var culture = CultureInfo.GetCultureInfo(normalizedLanguage);
            while (culture.Name.Length != 0)
            {
                foreach (string available in supportedLanguages)
                    if (string.Equals(available, culture.Name, StringComparison.OrdinalIgnoreCase)) { supported = true; break; }
                if (supported) break;
                culture = culture.Parent;
            }
            if (!supported) throw new ArgumentException("The settings language has no localization dataset.");
            candidate.Language = normalizedLanguage;
            if (candidate.BusVolumes.Count != defaults.BusVolumes.Count || candidate.BusMuted.Count != defaults.BusMuted.Count)
                throw new ArgumentException("Settings must include every configured audio bus.");
            foreach (string bus in candidate.BusVolumes.Keys)
                if (!ContainsOrdinal(defaults.BusVolumes.Keys, bus) || !candidate.BusMuted.ContainsKey(bus) ||
                    (availableAudioBuses != null && !ContainsOrdinal(availableAudioBuses, bus)))
                    throw new ArgumentException("The settings audio bus is not configured: " + bus);
            if (candidate.InputBindings.Count != defaults.InputBindings.Count)
                throw new ArgumentException("Settings must include every configurable input action.");
            foreach (var binding in candidate.InputBindings)
                if (!defaults.InputBindings.Exists(item => item.ContextId == binding.ContextId && item.Action == binding.Action))
                    throw new ArgumentException("The settings input action is not configured.");
            if (runtimeBindings != null) BuildInputBindings(candidate, runtimeBindings);
            return candidate;
        }

        private static bool ContainsOrdinal(IEnumerable<string> names, string value)
        {
            foreach (string name in names) if (string.Equals(name, value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void ValidateShape(UserSettingsData settings)
        {
            NormalizeLanguage(settings.Language);
            Volume(settings.MasterVolume);
            if (settings.BusVolumes == null || settings.BusMuted == null || settings.InputBindings == null)
                throw new ArgumentException("Settings collections cannot be null.");
            foreach (var pair in settings.BusVolumes) { Name(pair.Key); Volume(pair.Value); }
            foreach (var pair in settings.BusMuted) Name(pair.Key);
            ValidateBindings(settings.InputBindings);
        }

        private static void ValidateBindings(IReadOnlyList<UserInputBinding> bindings)
        {
            var actions = new HashSet<string>(StringComparer.Ordinal);
            var controls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                if (binding == null) throw new ArgumentException("Input settings cannot contain null.");
                ValidateBinding(binding.ContextId, binding.Action, binding.Control, binding.Scale, binding.DeadZone, actions, controls);
            }
        }

        private static void ValidateBinding(string contextId, string action, string control, float scale, float deadZone,
            HashSet<string> actions, HashSet<string> controls)
        {
            Name(contextId); Name(action); Name(control);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale == 0 || Math.Abs(scale) > 1 ||
                float.IsNaN(deadZone) || deadZone < 0 || deadZone >= 1)
                throw new ArgumentException("Input scale and dead zone are invalid.");
            if (!actions.Add(contextId + "\0" + action) || !controls.Add(contextId + "\0" + control))
                throw new ArgumentException("Input actions and controls must be unique within their context.");
        }

        private static string NormalizeLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code != code.Trim()) throw new ArgumentException("A nonempty culture name is required.", nameof(code));
            try
            {
                var culture = CultureInfo.GetCultureInfo(code);
                if (culture.Name.Length == 0) throw new ArgumentException("The invariant culture is not a language.", nameof(code));
                return culture.Name;
            }
            catch (CultureNotFoundException error) { throw new ArgumentException("The language is not a valid culture name.", nameof(code), error); }
        }

        private static void Name(string value)
        { if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0) throw new ArgumentException("Settings names cannot be empty or contain NUL."); }

        private static void Volume(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value), "Volume must be finite in [0, 1]."); }

        private static UserInputBinding FromBinding(InputBindingSnapshot item)
        {
            if (item == null) throw new ArgumentException("A binding snapshot cannot contain null.");
            return new UserInputBinding { ContextId = item.ContextId, Action = item.Action, Control = item.Control, Scale = item.Scale, DeadZone = item.DeadZone };
        }

        private static UserInputBinding CopyBinding(UserInputBinding item)
        {
            if (item == null) throw new ArgumentException("Input settings cannot contain null.");
            return new UserInputBinding { ContextId = item.ContextId, Action = item.Action, Control = item.Control, Scale = item.Scale, DeadZone = item.DeadZone };
        }
    }
}
