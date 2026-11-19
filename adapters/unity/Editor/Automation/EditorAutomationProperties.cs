using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationProperties
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        internal static void Execute(AutomationEdit request, GameObject target, AutomationResponse response)
        {
            var components = target.GetComponents<Component>()
                .Where(component => component != null && component.GetType().FullName == request.kind).Take(2).ToArray();
            if (components.Length != 1)
                throw new ArgumentException("Specify the exact full name of a unique component on the target: " + request.kind);

            using var serialized = new SerializedObject(components[0]);
            if (request.action == "component.inspect") Inspect(request, serialized, response);
            else Edit(request, serialized, response);
        }

        private static void Inspect(AutomationEdit request, SerializedObject serialized, AutomationResponse response)
        {
            var limit = request.limit > 0 ? Math.Min(request.limit, 500) : 100;
            var properties = new List<AutomationProperty>();
            using var iterator = serialized.GetIterator();
            while (iterator.NextVisible(true))
            {
                var path = iterator.propertyPath;
                if (!string.IsNullOrEmpty(request.query) && path != request.query &&
                    !path.StartsWith(request.query + ".", StringComparison.Ordinal)) continue;
                if (properties.Count == limit)
                {
                    response.truncated = true;
                    break;
                }
                properties.Add(Describe(iterator));
            }

            response.properties = properties.ToArray();
        }

        private static void Edit(AutomationEdit request, SerializedObject serialized, AutomationResponse response)
        {
            var edits = request.properties;
            if (edits == null || edits.Length == 0 || edits.Length > 32)
                throw new ArgumentException("Supply between 1 and 32 property edits.");

            var remaining = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var edit in edits)
                if (edit == null || string.IsNullOrEmpty(edit.path) || edit.value == null || !remaining.TryAdd(edit.path, edit.value))
                    throw new ArgumentException("Each property edit requires a unique path and a scalar value.");

            var changes = new List<AutomationProperty>();
            using var iterator = serialized.GetIterator();
            while (remaining.Count > 0 && iterator.NextVisible(true))
            {
                if (!remaining.TryGetValue(iterator.propertyPath, out var value)) continue;
                var change = Describe(iterator);
                if (!change.writable || !TrySet(iterator, value))
                    throw new ArgumentException("Unsupported or invalid value for " + change.path + " (" + change.type + ").");
                change.previousValue = change.value;
                change.value = Read(iterator);
                changes.Add(change);
                remaining.Remove(change.path);
            }

            if (remaining.Count > 0) throw new ArgumentException("Visible property not found: " + string.Join(", ", remaining.Keys));
            if (!request.preview)
            {
                serialized.ApplyModifiedProperties();
                serialized.Update();
                foreach (var change in changes)
                {
                    using var current = serialized.FindProperty(change.path);
                    change.value = current == null ? null : Read(current);
                }
            }

            response.preview = request.preview;
            response.properties = changes.ToArray();
        }

        private static AutomationProperty Describe(SerializedProperty property) => new AutomationProperty
        {
            path = property.propertyPath,
            type = property.type,
            value = Read(property),
            writable = property.editable && (property.propertyType == SerializedPropertyType.Boolean ||
                       property.propertyType == SerializedPropertyType.String ||
                       (property.propertyType == SerializedPropertyType.Integer && property.type == "int") ||
                       (property.propertyType == SerializedPropertyType.Float && property.type == "float"))
        };

        private static string Read(SerializedProperty property) => property.propertyType switch
        {
            SerializedPropertyType.Boolean => property.boolValue ? "true" : "false",
            SerializedPropertyType.Integer => property.longValue.ToString(Culture),
            SerializedPropertyType.Float => property.type == "double"
                ? property.doubleValue.ToString("R", Culture) : property.floatValue.ToString("R", Culture),
            SerializedPropertyType.String => property.stringValue,
            SerializedPropertyType.Enum => property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                ? property.enumNames[property.enumValueIndex] : null,
            SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? null :
                AssetDatabase.GetAssetPath(property.objectReferenceValue),
            _ => null
        };

        private static bool TrySet(SerializedProperty property, string value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean when bool.TryParse(value, out var boolean):
                    property.boolValue = boolean;
                    return true;
                case SerializedPropertyType.Integer when int.TryParse(value, NumberStyles.Integer, Culture, out var integer):
                    property.intValue = integer;
                    return true;
                case SerializedPropertyType.Float when float.TryParse(value, NumberStyles.Float, Culture, out var number) &&
                                                       !float.IsNaN(number) && !float.IsInfinity(number):
                    property.floatValue = number;
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = value;
                    return true;
                default:
                    return false;
            }
        }
    }
}
