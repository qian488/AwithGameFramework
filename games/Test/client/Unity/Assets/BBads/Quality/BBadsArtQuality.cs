using System;
using UnityEngine;

namespace BBads.Quality
{
    public enum BBadsArtTier { High, Low }

    public static class BBadsArtQuality
    {
        public const string BackgroundKey = "BBads/Art/CF/BG";
        public const string BagSlotKey = "BBads/Items/Bag_Slot";
        public const string GangsterIconKey = "BBads/Items/Cat_Gangster1";

        private const string Root = "BBads/";
        private const string VariantRoot = Root + "Quality/";
        private const string ForceFlag = "-bbads-art-quality";

        public static BBadsArtTier SelectedTier { get; private set; } = ReadLaunchTier();

        // Call before loading presentation assets. Passing null restores the launch default.
        public static void SetOverride(BBadsArtTier? tier)
        {
            if (tier.HasValue && tier != BBadsArtTier.High && tier != BBadsArtTier.Low)
                throw new ArgumentOutOfRangeException(nameof(tier));

            SelectedTier = tier ?? ReadLaunchTier();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSelection() => SetOverride(null);

        public static Sprite LoadSprite(string key, Sprite fallback = null)
        {
            return LoadSprite(key, out _, fallback);
        }

        public static Sprite LoadSprite(string key, out string resolvedKey, Sprite fallback = null)
        {
            if (!IsLogicalKey(key))
                throw new ArgumentException("Expected a logical BBads Resources sprite key.", nameof(key));

            string relativeKey = key.Substring(Root.Length);
            resolvedKey = VariantRoot + SelectedTier + "/" + relativeKey;
            Sprite sprite = Resources.Load<Sprite>(resolvedKey);
            if (sprite != null)
                return sprite;

            BBadsArtTier other = SelectedTier == BBadsArtTier.High ? BBadsArtTier.Low : BBadsArtTier.High;
            resolvedKey = VariantRoot + other + "/" + relativeKey;
            sprite = Resources.Load<Sprite>(resolvedKey);
            if (sprite != null)
                return sprite;

            resolvedKey = null;
            if (fallback != null)
                return fallback;

            sprite = Resources.Load<Sprite>(key);
            if (sprite != null)
                resolvedKey = key;
            return sprite;
        }

        private static bool IsLogicalKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(Root, StringComparison.Ordinal) ||
                key.StartsWith(VariantRoot, StringComparison.OrdinalIgnoreCase) || key.Contains("\\"))
                return false;

            foreach (string segment in key.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
            return true;
        }

        private static BBadsArtTier ReadLaunchTier()
        {
            // Last valid flag wins. A runtime override also works on devices without launch arguments.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = args.Length - 2; i >= 1; i--)
            {
                if (!string.Equals(args[i], ForceFlag, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(args[i + 1], "high", StringComparison.OrdinalIgnoreCase))
                    return BBadsArtTier.High;
                if (string.Equals(args[i + 1], "low", StringComparison.OrdinalIgnoreCase))
                    return BBadsArtTier.Low;
            }

            return Application.platform == RuntimePlatform.Android ? BBadsArtTier.Low : BBadsArtTier.High;
        }
    }
}
