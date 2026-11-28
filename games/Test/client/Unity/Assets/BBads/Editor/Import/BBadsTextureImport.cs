using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BBads.Editor
{
    public static class BBadsTextureImport
    {
        private const string Root = "Assets/BBads/";
        private const string ExceptionsPath = Root + "ImportSettings/TextureExceptions.json";

        private enum Group { Unmanaged, Ui, Background, World, Spine, FontOrDigits }

        [Serializable]
        private sealed class ExceptionFile
        {
            public AssetException[] exceptions;
        }

        [Serializable]
        private sealed class AssetException
        {
            public string path;
            public string reason;
        }

        [Serializable]
        private sealed class Report
        {
            public int changes;
            public int applied;
            public AssetEntry[] assets;
            public string[] absentExceptions;
        }

        [Serializable]
        private sealed class AssetEntry
        {
            public string path, guid, group, reason;
            public int sourceWidth, sourceHeight;
            public bool sourceHasAlpha, srgb, alphaIsTransparency;
            public string alphaSource, textureType, spriteMode;
            public float pixelsPerUnit;
            public Vector2 importerPivot;
            public Vector4 importerBorder;
            public SpriteEntry[] sprites;
            public string[] dependencies, referencedBy, spineAtlases;
            public TexturePolicy before, expected;
            public bool changed;
        }

        [Serializable]
        private sealed class SpriteEntry
        {
            public string name, fileId;
            public Rect rect;
            public Vector2 pivot;
            public Vector4 border;
            public float pixelsPerUnit;
        }

        [Serializable]
        private sealed class TexturePolicy
        {
            public bool mipmaps, readable;
            public string filter;
            public PlatformPolicy defaultPlatform, standalone, android;
        }

        [Serializable]
        private sealed class PlatformPolicy
        {
            public bool overridden, crunch, splitAlpha;
            public int maxSize, quality;
            public string format, compression, resize;
        }

        [MenuItem("Tools/Test/BBads/Import/Preview Texture Rules")]
        public static void Preview() => WriteReport(false);

        [MenuItem("Tools/Test/BBads/Import/Apply Texture Rules")]
        public static void Apply() => WriteReport(true);

        private static void WriteReport(bool apply)
        {
            string path = Path.GetFullPath("Library/BBads-art-import-" + (apply ? "apply" : "preview") + ".json");
            string[] args = Environment.GetCommandLineArgs();
            int outputFlag = Array.IndexOf(args, "-bbads-import-report");
            if (outputFlag >= 0)
            {
                if (outputFlag + 1 == args.Length || args[outputFlag + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException("Pass a report path after -bbads-import-report.");
                path = Path.GetFullPath(args[outputFlag + 1]);
                string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
                if (path.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Write BBads import reports outside Assets.");
            }

            string json = Run(apply, out int changes, out int applied);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json + "\n");
            Debug.Log($"BBads texture rules changes={changes} applied={applied} report={path}");
        }

        public static string Run(bool apply, out int changes, out int applied)
        {
            var exceptions = ReadExceptions();
            string[] paths = AssetDatabase.GetAllAssetPaths().Where(IsSourcePath)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var atlasPages = FindAtlasPages(paths);
            var users = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string path in paths.Where(path => !AssetDatabase.IsValidFolder(path)))
            {
                string[] direct = AssetDatabase.GetDependencies(path, false)
                    .Where(dependency => dependency != path)
                    .OrderBy(dependency => dependency, StringComparer.Ordinal).ToArray();
                dependencies.Add(path, direct);
                foreach (string dependency in direct)
                    AddReference(users, dependency, path);
            }

            var entries = new List<AssetEntry>();
            var actions = new List<Action>();
            foreach (string path in paths)
            {
                atlasPages.TryGetValue(path, out List<string> atlases);
                Group group = Classify(path, atlases != null);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    if (group == Group.FontOrDigits && IsFontFile(path))
                    {
                        AssetEntry font = Describe(path, group, dependencies, users, atlases);
                        font.reason = "Font geometry and rasterization are preserved.";
                        entries.Add(font);
                    }
                    continue;
                }

                AssetEntry entry = Describe(path, group, dependencies, users, atlases);
                entries.Add(entry);
                importer.GetSourceTextureWidthAndHeight(out entry.sourceWidth, out entry.sourceHeight);
                entry.sourceHasAlpha = importer.DoesSourceTextureHaveAlpha();
                entry.srgb = importer.sRGBTexture;
                entry.alphaSource = importer.alphaSource.ToString();
                entry.alphaIsTransparency = importer.alphaIsTransparency;
                entry.textureType = importer.textureType.ToString();
                entry.spriteMode = importer.spriteImportMode.ToString();
                entry.pixelsPerUnit = importer.spritePixelsPerUnit;
                entry.importerPivot = importer.spritePivot;
                entry.importerBorder = importer.spriteBorder;
                entry.sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Select(sprite =>
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
                    return new SpriteEntry
                    {
                        name = sprite.name, fileId = fileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        rect = sprite.rect, pivot = sprite.pivot, border = sprite.border,
                        pixelsPerUnit = sprite.pixelsPerUnit
                    };
                }).OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                  .ThenBy(sprite => sprite.fileId, StringComparer.Ordinal).ToArray();

                var defaultPlatform = importer.GetDefaultPlatformTextureSettings();
                var standalone = importer.GetPlatformTextureSettings("Standalone");
                var android = importer.GetPlatformTextureSettings("Android");
                entry.before = Snapshot(importer.mipmapEnabled, importer.isReadable, importer.filterMode,
                    defaultPlatform, standalone, android);
                entry.expected = entry.before;
                entry.reason = SkipReason(entry, group, importer, exceptions);
                if (entry.reason != null)
                    continue;

                bool mipmaps = group == Group.World && importer.mipmapEnabled;
                bool readable = group == Group.Spine && importer.isReadable;
                FilterMode filter = group == Group.Spine ? importer.filterMode : FilterMode.Bilinear;
                // Use source dimensions as a floor. Existing art keeps its resolution and slice coordinate space.
                int floor = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(entry.sourceWidth, entry.sourceHeight)));
                bool blockAligned = entry.sourceWidth % 4 == 0 && entry.sourceHeight % 4 == 0;
                var desktopTarget = Target(standalone, group, floor, false, blockAligned);
                var androidTarget = Target(android, group, floor, true, blockAligned);
                entry.expected = Snapshot(mipmaps, readable, filter, defaultPlatform, desktopTarget, androidTarget);
                entry.changed = JsonUtility.ToJson(entry.before) != JsonUtility.ToJson(entry.expected);
                if (entry.changed)
                    actions.Add(() =>
                    {
                        importer.mipmapEnabled = mipmaps;
                        importer.isReadable = readable;
                        importer.filterMode = filter;
                        importer.SetPlatformTextureSettings(desktopTarget);
                        importer.SetPlatformTextureSettings(androidTarget);
                        importer.SaveAndReimport();
                    });
            }

            changes = actions.Count;
            applied = 0;
            if (apply)
            {
                foreach (Action action in actions)
                {
                    action();
                    applied++;
                }
            }

            return JsonUtility.ToJson(new Report
            {
                changes = changes,
                applied = applied,
                assets = entries.ToArray(),
                absentExceptions = exceptions.Keys.Except(paths, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray()
            }, true);
        }

        private static bool IsSourcePath(string path) =>
            path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(Root + "Quality/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(Root + "Editor/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(Root + "ImportSettings/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(Root + "Atlases/", StringComparison.OrdinalIgnoreCase);

        private static Group Classify(string path, bool spinePage)
        {
            if (spinePage)
                return Group.Spine;

            string relative = path.Substring(Root.Length).ToLowerInvariant();
            if (relative.StartsWith("resources/bbads/", StringComparison.Ordinal))
                relative = "resources/" + relative.Substring("resources/bbads/".Length);

            if (relative == "art/cf/bg.png" || relative == "resources/art/cf/bg.png")
                return Group.Background;
            if (relative.StartsWith("fonts/") || relative.Contains("/font/") || relative.Contains("/fonts/") ||
                relative.Contains("/digits/") || relative.Contains("/numbers/") || IsFontFile(path))
                return Group.FontOrDigits;
            if (relative.StartsWith("art/cf/") || relative.StartsWith("resources/items/") ||
                relative.StartsWith("resources/ui/"))
                return Group.Ui;
            if (relative.StartsWith("resources/projectile/") || relative.StartsWith("resources/healthbar/") ||
                relative.StartsWith("resources/km/projectile/"))
                return Group.World;
            return Group.Unmanaged;
        }

        private static bool IsFontFile(string path) =>
            path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase);

        private static string SkipReason(AssetEntry entry, Group group, TextureImporter importer,
            Dictionary<string, string> exceptions)
        {
            if (exceptions.TryGetValue(entry.path, out string reason))
                return reason;
            if (group == Group.Unmanaged)
                return "Outside the recognized art groups.";
            if (group == Group.FontOrDigits)
                return "Font and digit sheets retain source rasterization and slicing.";
            if (entry.sourceWidth <= 0 || entry.sourceHeight <= 0 || entry.sourceWidth > 16384 || entry.sourceHeight > 16384)
                return "Source dimensions are outside supported texture limits.";
            if (group != Group.Spine && importer.textureType != TextureImporterType.Sprite)
                return "Preserve the existing texture type and object references.";
            if (group != Group.Spine && (importer.isReadable || importer.spriteImportMode == SpriteImportMode.None))
                return "Preserve source Read/Write or disabled sprite import.";
            if (group == Group.Spine && importer.textureShape != TextureImporterShape.Texture2D)
                return "Spine atlas page uses a non-2D texture shape.";
            return null;
        }

        private static TextureImporterPlatformSettings Target(
            TextureImporterPlatformSettings current, Group group, int size, bool android, bool blockAligned)
        {
            bool spine = group == Group.Spine;
            int limit = spine ? size : Mathf.Max(size, group == Group.Background ? 2048 : 1024);
            current.overridden = true;
            current.maxTextureSize = Mathf.Max(current.maxTextureSize, limit);
            bool uncompressed = spine || (!android && !blockAligned);
            current.format = uncompressed ? TextureImporterFormat.RGBA32 :
                !android ? TextureImporterFormat.BC7 :
                group == Group.Background ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            current.textureCompression = uncompressed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            current.compressionQuality = 100;
            current.crunchedCompression = false;
            current.allowsAlphaSplitting = false;
            return current;
        }

        private static TexturePolicy Snapshot(bool mipmaps, bool readable, FilterMode filter,
            TextureImporterPlatformSettings defaults, TextureImporterPlatformSettings desktop,
            TextureImporterPlatformSettings android) => new TexturePolicy
        {
            mipmaps = mipmaps, readable = readable, filter = filter.ToString(),
            defaultPlatform = Snapshot(defaults), standalone = Snapshot(desktop), android = Snapshot(android)
        };

        private static PlatformPolicy Snapshot(TextureImporterPlatformSettings settings) => new PlatformPolicy
        {
            overridden = settings.overridden, maxSize = settings.maxTextureSize, quality = settings.compressionQuality,
            format = settings.format.ToString(), compression = settings.textureCompression.ToString(),
            resize = settings.resizeAlgorithm.ToString(), crunch = settings.crunchedCompression,
            splitAlpha = settings.allowsAlphaSplitting
        };

        private static AssetEntry Describe(string path, Group group, Dictionary<string, string[]> dependencies,
            Dictionary<string, List<string>> users, List<string> atlases) => new AssetEntry
        {
            path = path, guid = AssetDatabase.AssetPathToGUID(path), group = group.ToString(),
            dependencies = dependencies[path],
            referencedBy = users.TryGetValue(path, out List<string> paths) ? paths.ToArray() : Array.Empty<string>(),
            spineAtlases = atlases?.ToArray() ?? Array.Empty<string>()
        };

        private static Dictionary<string, string> ReadExceptions()
        {
            var settings = JsonUtility.FromJson<ExceptionFile>(File.ReadAllText(ExceptionsPath));
            if (settings?.exceptions == null)
                throw new InvalidDataException("Missing exceptions array in " + ExceptionsPath);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (AssetException entry in settings.exceptions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.path) || !IsSourcePath(entry.path) ||
                    string.IsNullOrWhiteSpace(entry.reason))
                    throw new InvalidDataException("Invalid texture exception in " + ExceptionsPath);
                if (!result.TryAdd(entry.path, entry.reason))
                    throw new InvalidDataException("Duplicate texture exception: " + entry.path);
            }
            return result;
        }

        private static Dictionary<string, List<string>> FindAtlasPages(string[] paths)
        {
            var pages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var sourcePaths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string atlas in paths.Where(path => path.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)))
            {
                string directory = Path.GetDirectoryName(atlas).Replace('\\', '/');
                bool afterBlank = true;
                foreach (string line in File.ReadLines(atlas))
                {
                    string relative = line.Trim().Replace('\\', '/');
                    bool pageHeader = afterBlank;
                    afterBlank = relative.Length == 0;
                    if (!pageHeader)
                        continue;
                    if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                        !relative.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                        !relative.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string page = directory + "/" + relative;
                    if (sourcePaths.Contains(page))
                        AddReference(pages, page, atlas);
                }
            }
            return pages;
        }

        private static void AddReference(Dictionary<string, List<string>> references, string key, string value)
        {
            if (!references.TryGetValue(key, out List<string> entries))
                references.Add(key, entries = new List<string>());
            if (entries.Count == 0 || entries[entries.Count - 1] != value)
                entries.Add(value);
        }
    }
}
