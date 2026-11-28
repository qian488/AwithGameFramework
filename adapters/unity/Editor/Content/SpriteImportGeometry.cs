using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor.Content
{
    internal static class SpriteImportGeometry
    {
        private static readonly string[] GeometryFields =
        {
            "m_SpriteSheet", "m_InternalIDToNameTable", "m_SpriteMode", "m_SpriteMeshType",
            "m_SpriteExtrude", "m_Alignment", "m_SpritePivot",
            "m_SpritePixelsToUnits", "m_SpriteBorder", "m_SpriteGenerateFallbackPhysicsShape", "m_SpriteTessellationDetail"
        };

        internal static JObject Capture(TextureImporter importer)
        {
            var serialized = JObject.Parse(EditorJsonUtility.ToJson(importer));
            var geometry = new JObject { ["textureType"] = importer.textureType.ToString() };
            foreach (var field in GeometryFields)
            {
                if (serialized.TryGetValue(field, out var value)) geometry[field] = value.DeepClone();
            }
            if (importer.textureType == TextureImporterType.Sprite && geometry["m_SpriteSheet"] == null)
                throw new InvalidOperationException("Unity did not expose the serialized Sprite metadata.");

            var sprites = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Sprite>().Select(sprite =>
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var guid, out long fileId))
                    throw new InvalidOperationException($"Could not identify Sprite {sprite.name}.");
                return new { guid, fileId, sprite.name, sprite.pixelsPerUnit };
            });
            geometry["sprites"] = JArray.FromObject(sprites.OrderBy(sprite => sprite.guid, StringComparer.Ordinal)
                .ThenBy(sprite => sprite.fileId).ToArray());
            return geometry;
        }
    }
}
