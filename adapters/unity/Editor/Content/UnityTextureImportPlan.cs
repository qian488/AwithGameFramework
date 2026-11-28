using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AwithGameFrame.Unity.Editor.Content
{
    internal sealed class UnityTextureImportPlan
    {
        internal readonly JObject Before = new JObject();
        internal readonly JObject Expected = new JObject();
        internal bool HasChanges => changes.Count != 0;

        private readonly List<Action> changes = new List<Action>();
        private readonly JsonSerializer serializer = UnityArtImportProfile.CreateSerializer();

        internal UnityTextureImportPlan(TextureImporter importer, UnityTextureImportSettings group, UnityTextureImportSettings exception)
        {
            Track("filterMode", importer.filterMode, exception?.filterMode ?? group?.filterMode, value => importer.filterMode = value);
            var wrap = exception?.wrapMode ?? group?.wrapMode;
            Track("wrapModeU", importer.wrapModeU, wrap, value => importer.wrapModeU = value);
            Track("wrapModeV", importer.wrapModeV, wrap, value => importer.wrapModeV = value);
            Track("wrapModeW", importer.wrapModeW, wrap, value => importer.wrapModeW = value);
            Track("textureCompression", importer.textureCompression, exception?.textureCompression ?? group?.textureCompression, value => importer.textureCompression = value);
            Track("maxTextureSize", importer.maxTextureSize, exception?.maxTextureSize ?? group?.maxTextureSize, value => importer.maxTextureSize = value);
            Track("compressionQuality", importer.compressionQuality, exception?.compressionQuality ?? group?.compressionQuality, value => importer.compressionQuality = value);
            Track("anisoLevel", importer.anisoLevel, exception?.anisoLevel ?? group?.anisoLevel, value => importer.anisoLevel = value);
            Track("mipmapEnabled", importer.mipmapEnabled, exception?.mipmapEnabled ?? group?.mipmapEnabled, value => importer.mipmapEnabled = value);
            Track("readable", importer.isReadable, exception?.readable ?? group?.readable, value => importer.isReadable = value);
            Track("alphaIsTransparency", importer.alphaIsTransparency, exception?.alphaIsTransparency ?? group?.alphaIsTransparency, value => importer.alphaIsTransparency = value);
            Track("srgb", importer.sRGBTexture, exception?.srgb ?? group?.srgb, value => importer.sRGBTexture = value);

            var defaults = (group?.platforms ?? Array.Empty<UnityTexturePlatformSettings>()).ToDictionary(platform => platform.name);
            var overrides = (exception?.platforms ?? Array.Empty<UnityTexturePlatformSettings>()).ToDictionary(platform => platform.name);
            foreach (var name in defaults.Keys.Union(overrides.Keys).OrderBy(value => value, StringComparer.Ordinal))
            {
                defaults.TryGetValue(name, out var baseline);
                overrides.TryGetValue(name, out var assetOverride);
                TrackPlatform(importer, name, baseline, assetOverride);
            }
        }

        internal void Apply()
        {
            foreach (var change in changes) change();
        }

        private void TrackPlatform(TextureImporter importer, string name, UnityTexturePlatformSettings group, UnityTexturePlatformSettings exception)
        {
            var settings = importer.GetPlatformTextureSettings(name);
            var prefix = $"platforms.{name}.";
            var overridden = (exception ?? group).overridden ?? true;
            var start = changes.Count;
            Track(prefix + "overridden", settings.overridden, overridden, value => settings.overridden = value);
            if (!overridden)
            {
                if (settings.overridden) changes.Add(() => importer.ClearPlatformTextureSettings(name));
                return;
            }

            Track(prefix + "format", settings.format, exception?.format ?? group?.format, value => settings.format = value);
            Track(prefix + "maxTextureSize", settings.maxTextureSize, exception?.maxTextureSize ?? group?.maxTextureSize, value => settings.maxTextureSize = value);
            Track(prefix + "compressionQuality", settings.compressionQuality, exception?.compressionQuality ?? group?.compressionQuality, value => settings.compressionQuality = value);
            Track(prefix + "crunchedCompression", settings.crunchedCompression, exception?.crunchedCompression ?? group?.crunchedCompression, value => settings.crunchedCompression = value);
            if (changes.Count != start) changes.Add(() => importer.SetPlatformTextureSettings(settings));
        }

        private void Track<T>(string key, T current, T? desired, Action<T> set) where T : struct
        {
            if (!desired.HasValue) return;
            var value = desired.Value;
            Before[key] = JToken.FromObject(current, serializer);
            Expected[key] = JToken.FromObject(value, serializer);
            if (!EqualityComparer<T>.Default.Equals(current, value)) changes.Add(() => set(value));
        }
    }
}
