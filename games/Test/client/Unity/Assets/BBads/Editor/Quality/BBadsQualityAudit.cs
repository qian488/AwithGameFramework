using System;
using System.IO;
using BBads.Quality;
using UnityEditor;
using UnityEngine;

namespace BBads.Editor
{
    public static class BBadsQualityAudit
    {
        private const string VariantFolder = "Assets/BBads/Quality/Resources/BBads/Quality/";

        [MenuItem("Tools/Test/BBads/Quality/Validate Variants")]
        public static void Validate()
        {
            var keys = new[]
            {
                BBadsArtQuality.BackgroundKey,
                BBadsArtQuality.BagSlotKey,
                BBadsArtQuality.GangsterIconKey
            };
            foreach (string key in keys)
            {
                bool background = key == BBadsArtQuality.BackgroundKey;
                Sprite high = Inspect(key, BBadsArtTier.High, background);
                Sprite low = Inspect(key, BBadsArtTier.Low, background);
                if (low.texture.width * 2 != high.texture.width || low.texture.height * 2 != high.texture.height ||
                    low.pixelsPerUnit * 2 != high.pixelsPerUnit ||
                    low.rect.size * 2 != high.rect.size || low.pivot * 2 != high.pivot || low.border * 2 != high.border)
                    throw new InvalidDataException("BBads variants differ in logical sprite geometry or are not a 2:1 pair: " + key);
            }
            Debug.Log("BBADS_QUALITY_VARIANTS_VALID");
        }

        private static Sprite Inspect(string key, BBadsArtTier tier, bool background)
        {
            string path = VariantFolder + tier + "/" + key.Substring("BBads/".Length) + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || !(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new InvalidDataException("Missing BBads sprite variant: " + path);

            var desktop = importer.GetPlatformTextureSettings("Standalone");
            var android = importer.GetPlatformTextureSettings("Android");
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            TextureImporterFormat desktopFormat = background ? TextureImporterFormat.BC7 : TextureImporterFormat.RGBA32;
            TextureImporterFormat androidFormat = background ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            if (importer.spriteImportMode != SpriteImportMode.Single || importer.mipmapEnabled || importer.isReadable ||
                !desktop.overridden || desktop.format != desktopFormat ||
                !android.overridden || android.format != androidFormat ||
                desktop.maxTextureSize < Math.Max(width, height) || android.maxTextureSize < Math.Max(width, height))
                throw new InvalidDataException("Unexpected BBads variant import settings: " + path);

            Debug.Log($"BBads {tier} {key} source={width}x{height} " +
                $"imported={sprite.texture.width}x{sprite.texture.height} format={sprite.texture.format} " +
                $"standalone={desktop.format} android={android.format}");
            return sprite;
        }
    }
}
