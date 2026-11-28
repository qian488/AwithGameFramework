using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AwithGameFrame.Unity.Editor.Content
{
    internal static class UnityArtImportRules
    {
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
        private static readonly string[] PlatformNames = { "Standalone", "Android", "iPhone", "WebGL" };

        internal static UnityArtImportProfile Read(string path)
        {
            var json = JObject.Parse(File.ReadAllText(path),
                new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var profile = json.ToObject<UnityArtImportProfile>(UnityArtImportProfile.CreateSerializer());
            if (profile.schemaVersion != 1)
                throw new InvalidDataException("Art import profile requires schemaVersion 1.");
            profile.assetRoots ??= new[] { "Assets" };
            if (profile.assetRoots.Length == 0 || profile.assetRoots.Any(root => !IsAssetPath(root) || !AssetDatabase.IsValidFolder(root)))
                throw new InvalidDataException("assetRoots must list existing project folders under Assets.");
            if (profile.groups == null || profile.groups.Length == 0)
                throw new InvalidDataException("An art import profile requires at least one group.");
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in profile.groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.id) || !groupIds.Add(group.id))
                    throw new InvalidDataException("Art import groups require unique, nonempty IDs.");
                if (group.pathPrefixes != null && group.pathPrefixes.Any(prefix => !IsAssetPath(prefix)))
                    throw new InvalidDataException($"Group {group.id} requires Assets-relative folder prefixes.");
                if (group.extensions != null && group.extensions.Any(extension =>
                    string.IsNullOrWhiteSpace(extension) || extension[0] != '.' || extension.Length < 2 ||
                    extension.Skip(1).Any(character => !char.IsLetterOrDigit(character))))
                    throw new InvalidDataException($"Group {group.id} requires extensions such as .png.");
                if (group.settings == null)
                    throw new InvalidDataException($"Group {group.id} requires settings.");
                ValidateSettings(group.settings, group.id);
            }

            profile.exceptions ??= Array.Empty<UnityArtImportException>();
            var exceptionPaths = new HashSet<string>(Paths);
            foreach (var exception in profile.exceptions)
            {
                if (exception == null || !IsAssetPath(exception.assetPath) || Paths.Equals(exception.assetPath, "Assets") ||
                    !profile.assetRoots.Any(root => IsUnder(exception.assetPath, root)) || !exceptionPaths.Add(exception.assetPath))
                    throw new InvalidDataException($"Asset exception needs a unique file path inside assetRoots: {exception?.assetPath}");
                if (exception.groupId != null && !groupIds.Contains(exception.groupId))
                    throw new InvalidDataException($"Unknown group {exception.groupId} on exception {exception.assetPath}.");
                if (exception.exclude && (exception.groupId != null || exception.settings != null))
                    throw new InvalidDataException($"Excluded asset cannot also prescribe settings: {exception.assetPath}");
                if (exception.settings != null) ValidateSettings(exception.settings, exception.assetPath);
            }
            return profile;
        }

        internal static UnityArtImportGroup Match(UnityArtImportGroup[] groups, string path, string groupId)
        {
            if (groupId != null) return groups.First(group => group.id == groupId);

            return groups.FirstOrDefault(group =>
                (group.pathPrefixes == null || group.pathPrefixes.Length == 0 ||
                    group.pathPrefixes.Any(prefix => IsUnder(path, prefix))) &&
                (group.extensions == null || group.extensions.Length == 0 ||
                    group.extensions.Contains(Path.GetExtension(path), Paths)));
        }

        private static void ValidateSettings(UnityTextureImportSettings settings, string owner)
        {
            ValidateSize(settings.maxTextureSize, owner);
            ValidateRange(settings.compressionQuality, 0, 100, "compressionQuality", owner);
            ValidateRange(settings.anisoLevel, 0, 16, "anisoLevel", owner);
            var platforms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var platform in settings.platforms ?? Array.Empty<UnityTexturePlatformSettings>())
            {
                if (platform == null || !PlatformNames.Contains(platform.name) || !platforms.Add(platform.name))
                    throw new InvalidDataException($"{owner} requires unique platform names from {string.Join(", ", PlatformNames)}.");
                var hasSettings = platform.format.HasValue || platform.maxTextureSize.HasValue ||
                    platform.compressionQuality.HasValue || platform.crunchedCompression.HasValue;
                if ((platform.overridden == false && hasSettings) || (platform.overridden == null && !hasSettings))
                    throw new InvalidDataException($"Invalid override for {platform.name} in {owner}. Set overridden alone or specify platform settings.");
                ValidateSize(platform.maxTextureSize, owner + "/" + platform.name);
                ValidateRange(platform.compressionQuality, 0, 100, "compressionQuality", owner + "/" + platform.name);
            }
        }

        private static void ValidateSize(int? size, string owner)
        {
            if (size is int value && (value < 32 || value > 16384 || (value & (value - 1)) != 0))
                throw new InvalidDataException($"{owner} maxTextureSize must be a power of two from 32 to 16384.");
        }

        private static void ValidateRange(int? value, int min, int max, string field, string owner)
        {
            if (value < min || value > max)
                throw new InvalidDataException($"{owner} {field} must be between {min} and {max}.");
        }

        private static bool IsAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || path.Contains("\\") || path.Contains(":")) return false;
            return (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) &&
                path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }

        private static bool IsUnder(string path, string folder) =>
            Paths.Equals(path, folder) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
