using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace AwithGameFrame.Unity.Editor.Content
{
    public static class UnitySpriteAtlasAudit
    {
        [Serializable]
        private sealed class Specification
        {
            public int schemaVersion;
            public AtlasGroup[] groups;
        }

        [Serializable]
        private sealed class AtlasGroup
        {
            public string atlas;
            public string[] sources;
        }

        [Serializable]
        private sealed class AtlasResult
        {
            public string atlas;
            public int expectedSprites, packedSprites;
            public List<string> issues = new List<string>();
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public bool passed;
            public AtlasResult[] groups;
        }

        public static void RunBatch()
        {
            string specification = Argument("-awith-atlas-spec");
            string output = Path.GetFullPath(Argument("-awith-atlas-output"));
            if (output == Path.GetFullPath(specification))
                throw new ArgumentException("The atlas report and specification need distinct paths.");
            var spec = JsonUtility.FromJson<Specification>(File.ReadAllText(specification));
            if (spec?.schemaVersion != 1 || spec.groups == null || spec.groups.Length == 0)
                throw new InvalidDataException("Expected atlas specification schemaVersion 1 and at least one group.");

            var groups = spec.groups.Select(Inspect).ToArray();
            var report = new Report { groups = groups, passed = groups.All(group => group.issues.Count == 0) };
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            if (!report.passed)
                throw new InvalidDataException("Sprite Atlas audit failed. Inspect " + output);
        }

        private static AtlasResult Inspect(AtlasGroup group)
        {
            var result = new AtlasResult { atlas = group?.atlas };
            if (string.IsNullOrWhiteSpace(group?.atlas) || group.sources == null || group.sources.Length == 0)
            {
                result.issues.Add("Expected an atlas path and at least one source asset.");
                return result;
            }
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(group.atlas);
            if (atlas == null) result.issues.Add("Atlas could not be loaded.");

            foreach (string source in group.sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var sprites = string.IsNullOrWhiteSpace(source) ? Array.Empty<Sprite>()
                    : AssetDatabase.LoadAllAssetsAtPath(source).OfType<Sprite>().ToArray();
                if (sprites.Length == 0) result.issues.Add("Source has no imported sprites: " + source);
                result.expectedSprites += sprites.Length;
                if (atlas == null) continue;
                foreach (var sprite in sprites)
                    if (!atlas.CanBindTo(sprite))
                        result.issues.Add($"Atlas cannot bind {source} ({sprite.name}).");
            }
            if (atlas != null)
            {
                result.packedSprites = atlas.spriteCount;
                if (result.packedSprites != result.expectedSprites)
                    result.issues.Add($"Atlas has {result.packedSprites} sprites, expected {result.expectedSprites}.");
            }
            return result;
        }

        private static string Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length)
                throw new ArgumentException("Missing " + name);
            return arguments[index + 1];
        }
    }
}
