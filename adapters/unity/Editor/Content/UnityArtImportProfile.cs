using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor.Content
{
    [Serializable]
    public sealed class UnityArtImportProfile
    {
        public int schemaVersion;
        public string[] assetRoots;
        public UnityArtImportGroup[] groups;
        public UnityArtImportException[] exceptions;

        internal static JsonSerializer CreateSerializer()
        {
            var serializer = new JsonSerializer { MissingMemberHandling = MissingMemberHandling.Error };
            serializer.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
            return serializer;
        }
    }

    [Serializable]
    public sealed class UnityArtImportGroup
    {
        public string id;
        public string[] pathPrefixes;
        public string[] extensions;
        public UnityTextureImportSettings settings;
    }

    [Serializable]
    public sealed class UnityArtImportException
    {
        public string assetPath;
        public bool exclude;
        public string groupId;
        public UnityTextureImportSettings settings;
    }

    [Serializable]
    public sealed class UnityTextureImportSettings
    {
        public FilterMode? filterMode;
        public TextureWrapMode? wrapMode;
        public TextureImporterCompression? textureCompression;
        public int? maxTextureSize;
        public int? compressionQuality;
        public int? anisoLevel;
        public bool? mipmapEnabled;
        public bool? readable;
        public bool? alphaIsTransparency;
        public bool? srgb;
        public UnityTexturePlatformSettings[] platforms;
    }

    [Serializable]
    public sealed class UnityTexturePlatformSettings
    {
        public string name;
        public bool? overridden;
        public TextureImporterFormat? format;
        public int? maxTextureSize;
        public int? compressionQuality;
        public bool? crunchedCompression;
    }

    public sealed class UnityArtImportReport
    {
        public int schemaVersion = 1;
        public string mode;
        public int scanned;
        public int pending;
        public int changed;
        public int unchanged;
        public int excluded;
        public int unmatched;
        public int failed;
        public List<UnityArtImportEntry> assets = new List<UnityArtImportEntry>();
    }

    public sealed class UnityArtImportEntry
    {
        public string assetPath;
        public string groupId;
        public string status;
        public string message;
        public bool? spriteGeometryPreserved;
        public JObject before;
        public JObject expected;
        public JObject applied;
    }
}
