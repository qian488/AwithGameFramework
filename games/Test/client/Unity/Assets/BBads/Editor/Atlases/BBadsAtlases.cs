using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Test.BBads.AtlasTools
{
    public static class BBadsAtlases
    {
        internal const string Root = "Assets/BBads/";
        internal const string Output = Root + "Atlases/";
        internal const string CatalogPath = Root + "Editor/Atlases/Catalog.json";
        private const string DefaultPlatform = "DefaultTexturePlatform";

        [Serializable]
        private sealed class Catalog
        {
            public Group[] groups;
        }

        [Serializable]
        internal sealed class Group
        {
            public string name;
            public string[] sources;
            public string Path => Output + name + ".spriteatlasv2";
        }

        internal static Group[] ReadCatalog() => JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath)).groups;

        [MenuItem("Tools/Test/BBads/Atlases/Apply Catalog")]
        public static void Apply()
        {
            RequireV2();
            var catalog = ReadCatalog();
            // Resolve and validate every source before touching an atlas.
            var sources = catalog.Select(Resolve).ToArray();
            if (sources.SelectMany(group => group).Distinct().Count() != sources.Sum(group => group.Length))
                throw new InvalidOperationException("A source texture is listed more than once in the BBads atlas catalog.");
            for (int i = 0; i < catalog.Length; i++)
                GetSprites(catalog[i], sources[i]);

            Directory.CreateDirectory(Output);
            for (int i = 0; i < catalog.Length; i++)
            {
                var asset = new SpriteAtlasAsset();
                try
                {
                    asset.Add(sources[i]);
                    SpriteAtlasAsset.Save(asset, catalog[i].Path);
                }
                finally
                {
                    Object.DestroyImmediate(asset);
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var group in catalog)
                ApplyImporterSettings((SpriteAtlasImporter)AssetImporter.GetAtPath(group.Path));
            Validate();
        }

        private static void ApplyImporterSettings(SpriteAtlasImporter importer)
        {
            if (HasExpectedSettings(importer))
                return;

            importer.includeInBuild = true;
            var packing = importer.packingSettings;
            packing.padding = 4;
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            importer.packingSettings = packing;
            var texture = importer.textureSettings;
            texture.filterMode = FilterMode.Bilinear;
            texture.generateMipMaps = false;
            texture.readable = false;
            texture.sRGB = true;
            importer.textureSettings = texture;
            var platform = importer.GetPlatformSettings(DefaultPlatform);
            platform.maxTextureSize = 2048;
            platform.format = TextureImporterFormat.Automatic;
            platform.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformSettings(platform);
            importer.SaveAndReimport();
        }

        private static bool HasExpectedSettings(SpriteAtlasImporter importer)
        {
            var packing = importer.packingSettings;
            var texture = importer.textureSettings;
            var platform = importer.GetPlatformSettings(DefaultPlatform);
            return importer.includeInBuild && !packing.enableRotation && !packing.enableTightPacking &&
                packing.padding == 4 && !texture.generateMipMaps && !texture.readable && texture.sRGB &&
                texture.filterMode == FilterMode.Bilinear && platform.maxTextureSize == 2048 &&
                platform.format == TextureImporterFormat.Automatic &&
                platform.textureCompression == TextureImporterCompression.Compressed;
        }

        [MenuItem("Tools/Test/BBads/Atlases/Validate Catalog")]
        public static void Validate()
        {
            RequireV2();
            foreach (var group in ReadCatalog())
            {
                var sprites = GetSprites(group, Resolve(group));
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(group.Path);
                var importer = AssetImporter.GetAtPath(group.Path) as SpriteAtlasImporter;
                if (atlas == null || importer == null)
                    throw new InvalidOperationException("Missing Sprite Atlas V2 at " + group.Path);
                if (!HasExpectedSettings(importer))
                    throw new InvalidOperationException("Unexpected atlas settings at " + group.Path + ". Run Apply Catalog.");

                var unbound = sprites.Where(sprite => !atlas.CanBindTo(sprite)).Select(sprite => sprite.name).ToArray();
                if (atlas.spriteCount != sprites.Length || unbound.Length != 0)
                    throw new InvalidOperationException(
                        $"{group.Path} contains {atlas.spriteCount} sprites, expected {sprites.Length}. " +
                        $"Unbound sprites: {string.Join(", ", unbound)}. Run Apply Catalog.");
                Debug.Log($"BBADS_ATLAS_VALIDATED {group.name} {sprites.Length} sprites ({group.sources.Length} source textures)");
            }
        }

        internal static Object[] Resolve(Group group)
        {
            return group.sources.Select(relative =>
            {
                const string resources = "Resources/";
                string[] candidates = relative.StartsWith("Resources/BBads/", StringComparison.Ordinal)
                    ? new[] { relative, resources + "BBads/" + relative.Substring(resources.Length),
                        resources + relative.Substring("Resources/BBads/".Length) }
                    : new[] { relative };
                var matches = candidates.Select(path => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + path))
                    .Where(texture => texture != null).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        $"Expected one texture for {group.name}/{relative}, found {matches.Length}. Checked {string.Join(", ", candidates)}.");
                return (Object)matches[0];
            }).ToArray();
        }

        internal static Sprite[] GetSprites(Group group, Object[] sources)
        {
            var sprites = sources.SelectMany(source =>
            {
                string path = AssetDatabase.GetAssetPath(source);
                var slices = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                    .OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray();
                if (slices.Length == 0)
                    throw new InvalidOperationException("No imported Sprite sub-assets at " + path);
                return slices;
            }).ToArray();
            if (sprites.Select(sprite => sprite.name).Distinct(StringComparer.Ordinal).Count() != sprites.Length)
                throw new InvalidOperationException("Duplicate Sprite names in " + group.name);
            return sprites;
        }

        private static void RequireV2()
        {
            if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
                throw new InvalidOperationException(
                    "Set Project Settings > Editor > Sprite Packer > Mode to Sprite Atlas V2 (Enabled) before using the BBads atlas commands.");
        }
    }
}
