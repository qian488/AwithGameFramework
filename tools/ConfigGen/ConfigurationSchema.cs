using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AwithGameFrame.Tools.Configuration
{
    internal sealed class ConfigurationSchema
    {
        internal sealed class TypeSpec
        {
            internal string Name;
            internal TypeSpec Element;
            internal string Leaf => Element == null ? Name : Element.Leaf;
        }
        internal sealed class Field
        {
            internal string Name, Reference;
            internal bool AllowZero;
            internal TypeSpec Type;
        }
        internal sealed class Definition
        {
            internal string Name;
            internal List<Field> Fields = new List<Field>();
        }
        internal sealed class Enumeration
        {
            internal string Name;
            internal Dictionary<string, int> Values = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private static readonly Regex Identifier = new Regex("^[A-Z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Primitives = new HashSet<string> { "int", "long", "float", "double", "bool", "string" };
        internal string Namespace { get; }
        internal List<Definition> Tables { get; } = new List<Definition>();
        internal Dictionary<string, Definition> Structures { get; } = new Dictionary<string, Definition>(StringComparer.Ordinal);
        internal Dictionary<string, Enumeration> Enums { get; } = new Dictionary<string, Enumeration>(StringComparer.Ordinal);

        internal ConfigurationSchema(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Configuration schema must be an object.");
            RejectDuplicateProperties(root, "schema");
            Namespace = Text(root, "namespace");
            if (string.IsNullOrWhiteSpace(Namespace) || Namespace.Split('.').Any(part => !Identifier.IsMatch(part)))
                throw new InvalidDataException("namespace must contain PascalCase C# identifiers.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in Array(root, "enums", optional: true))
            {
                var definition = new Enumeration { Name = DeclareName(source, names) };
                if (!source.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(definition.Name + " requires an object of enum names and int values.");
                var numbers = new HashSet<int>();
                foreach (var value in values.EnumerateObject())
                {
                    if (!Identifier.IsMatch(value.Name) || value.Name == definition.Name || !IsInt(value.Value) || !numbers.Add(value.Value.GetInt32()))
                        throw new InvalidDataException(definition.Name + " has an invalid or ambiguous enum member: " + value.Name);
                    definition.Values.Add(value.Name, value.Value.GetInt32());
                }
                if (definition.Values.Count == 0) throw new InvalidDataException(definition.Name + " requires at least one enum member.");
                Enums.Add(definition.Name, definition);
            }
            foreach (var source in Array(root, "types", optional: true))
            {
                var definition = ParseDefinition(source, names);
                Structures.Add(definition.Name, definition);
            }
            foreach (var source in Array(root, "tables")) Tables.Add(ParseDefinition(source, names));
            var tableNames = new HashSet<string>(Tables.Select(table => table.Name), StringComparer.Ordinal);
            foreach (var definition in Structures.Values.Concat(Tables))
            {
                foreach (var field in definition.Fields)
                {
                    if (!Primitives.Contains(field.Type.Leaf) && !Enums.ContainsKey(field.Type.Leaf) && !Structures.ContainsKey(field.Type.Leaf))
                        throw new InvalidDataException(definition.Name + "." + field.Name + " uses unknown type " + field.Type.Leaf + ".");
                    if (field.Reference != null && (field.Type.Leaf != "int" || !tableNames.Contains(field.Reference)))
                        throw new InvalidDataException(definition.Name + "." + field.Name + " has an invalid reference target or type: " + field.Reference);
                }
            }
            foreach (var table in Tables)
                if (!table.Fields.Any(field => field.Name == "Id" && field.Type.Element == null && field.Type.Name == "int"))
                    throw new InvalidDataException(table.Name + " requires an int Id field.");
            foreach (var structure in Structures.Values) CheckCycles(structure, new HashSet<string>(StringComparer.Ordinal));
        }

        private static string DeclareName(JsonElement source, HashSet<string> names)
        {
            var name = Text(source, "name");
            if (!Identifier.IsMatch(name) || name == "SchemaHash" || !names.Add(name))
                throw new InvalidDataException("Invalid or duplicate generated type name: " + name);
            return name;
        }

        private static Definition ParseDefinition(JsonElement source, HashSet<string> names)
        {
            var definition = new Definition { Name = DeclareName(source, names) };
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in Array(source, "fields"))
            {
                var name = Text(field, "name");
                if (!Identifier.IsMatch(name) || name == "SchemaHash" || name == definition.Name || !fields.Add(name))
                    throw new InvalidDataException("Invalid field in " + definition.Name + ": " + name);
                string target = null;
                if (field.TryGetProperty("references", out _)) target = Text(field, "references");
                bool allowZero = false;
                if (field.TryGetProperty("allowZero", out var optional))
                {
                    if (optional.ValueKind != JsonValueKind.True && optional.ValueKind != JsonValueKind.False)
                        throw new InvalidDataException(definition.Name + "." + name + " allowZero must be a boolean.");
                    allowZero = optional.GetBoolean();
                    if (target == null) throw new InvalidDataException("allowZero requires an explicit reference target.");
                }
                definition.Fields.Add(new Field { Name = name, Type = ParseType(Text(field, "type")), Reference = target, AllowZero = allowZero });
            }
            return definition;
        }

        private static TypeSpec ParseType(string type)
        {
            if (type.Length > 128) throw new InvalidDataException("Configuration type nesting is too deep.");
            if (type.EndsWith("[]", StringComparison.Ordinal)) return new TypeSpec { Element = ParseType(type.Substring(0, type.Length - 2)) };
            if (!Primitives.Contains(type) && !Identifier.IsMatch(type)) throw new InvalidDataException("Invalid configuration type: " + type);
            return new TypeSpec { Name = type };
        }

        private void CheckCycles(Definition definition, HashSet<string> path)
        {
            if (!path.Add(definition.Name)) throw new InvalidDataException("Recursive configuration structure: " + definition.Name);
            foreach (var field in definition.Fields)
                if (Structures.TryGetValue(field.Type.Leaf, out var nested)) CheckCycles(nested, path);
            path.Remove(definition.Name);
        }

        internal void ValidateObject(JsonElement value, Definition definition, string path)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException(path + " must be a nonnull object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!definition.Fields.Any(field => field.Name == property.Name) || !names.Add(property.Name))
                    throw new InvalidDataException(path + " has an unknown or duplicate field: " + property.Name);
            foreach (var field in definition.Fields)
            {
                if (!value.TryGetProperty(field.Name, out var child)) throw new InvalidDataException(path + "." + field.Name + " is required.");
                ValidateValue(child, field.Type, path + "." + field.Name);
            }
        }

        private void ValidateValue(JsonElement value, TypeSpec type, string path)
        {
            if (type.Element != null)
            {
                if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException(path + " must be a nonnull array.");
                int index = 0;
                foreach (var element in value.EnumerateArray()) ValidateValue(element, type.Element, path + "[" + index++ + "]");
                return;
            }
            if (Structures.TryGetValue(type.Name, out var structure)) { ValidateObject(value, structure, path); return; }
            bool valid;
            if (Enums.TryGetValue(type.Name, out var enumeration))
                valid = value.ValueKind == JsonValueKind.String ? enumeration.Values.ContainsKey(value.GetString())
                    : IsInt(value) && enumeration.Values.ContainsValue(value.GetInt32());
            else switch (type.Name)
            {
                case "int": valid = IsInt(value); break;
                case "long": valid = value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _); break;
                case "float": valid = value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var f) && float.IsFinite(f); break;
                case "double": valid = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d); break;
                case "bool": valid = value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False; break;
                case "string": valid = value.ValueKind == JsonValueKind.String; break;
                default: valid = false; break;
            }
            if (!valid) throw new InvalidDataException(path + " has an invalid or null " + type.Name + " value.");
        }

        internal void ValidateReferences(JsonElement value, Definition definition, Dictionary<string, HashSet<int>> ids, string path)
        {
            foreach (var field in definition.Fields)
                ValidateNestedReferences(value.GetProperty(field.Name), field.Type, field, ids, path + "." + field.Name);
        }

        private void ValidateNestedReferences(JsonElement value, TypeSpec type, Field field, Dictionary<string, HashSet<int>> ids, string path)
        {
            if (type.Element != null)
            {
                int index = 0;
                foreach (var item in value.EnumerateArray()) ValidateNestedReferences(item, type.Element, field, ids, path + "[" + index++ + "]");
            }
            else if (field.Reference != null)
            {
                int id = value.GetInt32();
                if (!(field.AllowZero && id == 0) && !ids[field.Reference].Contains(id))
                    throw new InvalidDataException(path + " references missing " + field.Reference + " Id " + id + ".");
            }
            else if (Structures.TryGetValue(type.Name, out var nested)) ValidateReferences(value, nested, ids, path);
        }

        internal string EmitClass(Definition definition, string hash)
        {
            var code = new StringBuilder("// <auto-generated />\nusing System;\n\nnamespace ").Append(Namespace)
                .Append("\n{\n    [global::System.SerializableAttribute]\n    public sealed class ").Append(definition.Name).Append("\n    {\n");
            if (hash != null) code.Append("        public const string SchemaHash = \"").Append(hash).Append("\";\n");
            foreach (var field in definition.Fields)
                code.Append("        public ").Append(CSharpType(field.Type)).Append(' ').Append(field.Name).Append(" { get; set; }\n");
            return code.Append("    }\n}\n").ToString();
        }

        private string CSharpType(TypeSpec type) => type.Element != null ? CSharpType(type.Element) + "[]"
            : Primitives.Contains(type.Name) ? type.Name : "global::" + Namespace + "." + type.Name;

        internal string EmitEnum(Enumeration definition)
        {
            var code = new StringBuilder("// <auto-generated />\nnamespace ").Append(Namespace).Append("\n{\n    public enum ").Append(definition.Name).Append("\n    {\n");
            foreach (var value in definition.Values.OrderBy(value => value.Key, StringComparer.Ordinal)) code.Append("        ").Append(value.Key).Append(" = ").Append(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
            return code.Append("    }\n}\n").ToString();
        }

        private static bool IsInt(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);
        private static string Text(JsonElement source, string name)
        {
            if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
                throw new InvalidDataException("Expected a nonempty schema string: " + name);
            return value.GetString();
        }
        private static IEnumerable<JsonElement> Array(JsonElement source, string name, bool optional = false)
        {
            if (!source.TryGetProperty(name, out var value))
            {
                if (optional) return System.Array.Empty<JsonElement>();
                throw new InvalidDataException("Missing schema array: " + name);
            }
            if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException(name + " must be an array.");
            return value.EnumerateArray().ToArray();
        }
        private static void RejectDuplicateProperties(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException(path + " contains duplicate property " + property.Name);
                    RejectDuplicateProperties(property.Value, path + "." + property.Name);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var element in value.EnumerateArray()) RejectDuplicateProperties(element, path);
        }
    }
}
