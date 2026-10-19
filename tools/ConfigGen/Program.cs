using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AwithGameFrame.Tools.Configuration
{
    /// <summary>A deterministic, dependency-free configuration compiler used by CI and hosts.</summary>
    public static class ConfigurationCompiler
    {
        private static readonly Regex Identifier = new Regex("^[A-Z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Types = new HashSet<string> { "int", "long", "float", "double", "bool", "string" };

        public static void AssertOutputSet(string directory, IEnumerable<string> names)
        {
            if (!Directory.Exists(directory)) return;
            var expected = new HashSet<string>(names.Select(name => Path.GetFullPath(Path.Combine(directory, name))), StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                if ((Path.GetExtension(file) == ".cs" || Path.GetExtension(file) == ".json") && !expected.Contains(Path.GetFullPath(file)))
                    throw new InvalidDataException("Unexpected generated file; remove the obsolete artifact explicitly: " + file);
        }

        public static IReadOnlyDictionary<string, string> Compile(string schemaJson, IReadOnlyDictionary<string, string> data)
        {
            using var schema = JsonDocument.Parse(schemaJson);
            var root = schema.RootElement;
            var ns = root.GetProperty("namespace").GetString();
            if (string.IsNullOrWhiteSpace(ns) || ns.Split('.').Any(p => !Identifier.IsMatch(p)))
                throw new InvalidDataException("namespace must contain PascalCase C# identifiers.");
            var tables = root.GetProperty("tables").EnumerateArray().ToArray();
            var tableIds = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var parsed = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var table in tables)
                {
                    var name = table.GetProperty("name").GetString();
                    if (name == null || name == "SchemaHash" || !Identifier.IsMatch(name) || tableIds.ContainsKey(name))
                        throw new InvalidDataException("Invalid or duplicate table name: " + name);
                    var fields = table.GetProperty("fields").EnumerateArray().ToArray();
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var field in fields)
                    {
                        var fieldName = field.GetProperty("name").GetString();
                        var type = field.GetProperty("type").GetString();
                        if (fieldName == null || !Identifier.IsMatch(fieldName) || fieldName == "SchemaHash" || fieldName == name || !names.Add(fieldName) || !Types.Contains(type))
                            throw new InvalidDataException("Invalid field in " + name + ": " + fieldName);
                    }
                    if (!fields.Any(f => f.GetProperty("name").GetString() == "Id" && f.GetProperty("type").GetString() == "int"))
                        throw new InvalidDataException(name + " requires an int Id field.");
                    if (!data.TryGetValue(name, out var rowsJson)) throw new InvalidDataException("Missing data for " + name);
                    var document = JsonDocument.Parse(rowsJson);
                    parsed.Add(name, document);
                    if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException(name + " data must be an array.");
                    var ids = new HashSet<int>();
                    tableIds.Add(name, ids);
                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException(name + " row must be an object.");
                        var rowNames = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var value in row.EnumerateObject())
                            if (!names.Contains(value.Name) || !rowNames.Add(value.Name)) throw new InvalidDataException(name + " unknown or duplicate field: " + value.Name);
                        foreach (var field in fields)
                        {
                            var fieldName = field.GetProperty("name").GetString();
                            if (!row.TryGetProperty(fieldName, out var value) || !Matches(value, field.GetProperty("type").GetString()))
                                throw new InvalidDataException(name + "." + fieldName + " is missing or has the wrong type.");
                        }
                        var id = row.GetProperty("Id").GetInt32();
                        if (!ids.Add(id)) throw new InvalidDataException(name + " duplicate Id: " + id);
                    }
                }
                foreach (var name in data.Keys)
                    if (!tableIds.ContainsKey(name)) throw new InvalidDataException("Unknown data table: " + name);
                foreach (var table in tables)
                {
                    var name = table.GetProperty("name").GetString();
                    var fields = table.GetProperty("fields").EnumerateArray().ToArray();
                    foreach (var field in fields)
                    {
                        if (!field.TryGetProperty("references", out var reference)) continue;
                        var target = reference.GetString();
                        if (target == null || !tableIds.TryGetValue(target, out var targetIds) || field.GetProperty("type").GetString() != "int")
                            throw new InvalidDataException(name + " invalid reference target: " + target);
                        foreach (var row in parsed[name].RootElement.EnumerateArray())
                            if (!targetIds.Contains(row.GetProperty(field.GetProperty("name").GetString()).GetInt32()))
                                throw new InvalidDataException(name + " references missing " + target + " Id.");
                    }
                    // Hash the schema, independent of JSON property order/indentation. Data edits retain the schema version.
                    var hashInput = Canonical(root.GetProperty("tables")) + "|" + ns;
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(hashInput))).Replace("-", "").ToLowerInvariant();
                    var code = new StringBuilder("// <auto-generated />\nusing System;\n\nnamespace ").Append(ns).Append("\n{\n    [global::System.SerializableAttribute]\n    public sealed class ").Append(name).Append("\n    {\n        public const string SchemaHash = \"").Append(hash).Append("\";\n");
                    foreach (var field in fields)
                        code.Append("        public ").Append(field.GetProperty("type").GetString()).Append(' ').Append(field.GetProperty("name").GetString()).Append(" { get; set; }\n");
                    code.Append("    }\n}\n");
                    outputs.Add(name + ".cs", code.ToString());
                    var sortedRows = parsed[name].RootElement.EnumerateArray().OrderBy(r => r.GetProperty("Id").GetInt32()).ToArray();
                    outputs.Add(name + ".json", "{\"schemaHash\":\"" + hash + "\",\"rows\":[" + string.Join(",", sortedRows.Select(Canonical)) + "]}\n");
                }
                return outputs;
            }
            finally { foreach (var document in parsed.Values) document.Dispose(); }
        }

        private static bool Matches(JsonElement value, string type)
        {
            switch (type)
            {
                case "int": return value.TryGetInt32Safe();
                case "long": return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);
                case "float": return value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var f) && !float.IsInfinity(f) && !float.IsNaN(f);
                case "double": return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && !double.IsInfinity(d) && !double.IsNaN(d);
                case "bool": return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
                case "string": return value.ValueKind == JsonValueKind.String;
                default: return false;
            }
        }

        private static bool TryGetInt32Safe(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);

        private static string Canonical(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                return "{" + string.Join(",", element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}";
            if (element.ValueKind == JsonValueKind.Array) return "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]";
            return element.GetRawText();
        }
    }

    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 1 && args[0] == "--protocol") return GenerateProtocol(args);
                if (args.Length < 3 || args.Length > 4 || (args.Length == 4 && args[3] != "--check"))
                    throw new ArgumentException("Usage: ConfigGen <schema.json> <data-directory> <output-directory> [--check]");
                var data = Directory.GetFiles(args[1], "*.json").ToDictionary(Path.GetFileNameWithoutExtension, File.ReadAllText, StringComparer.Ordinal);
                var outputs = ConfigurationCompiler.Compile(File.ReadAllText(args[0]), data);
                ConfigurationCompiler.AssertOutputSet(args[2], outputs.Keys);
                foreach (var output in outputs)
                {
                    var path = Path.Combine(args[2], output.Key);
                    if (args.Length == 4)
                    {
                        if (!File.Exists(path) || File.ReadAllText(path) != output.Value) throw new InvalidDataException("Generated configuration is stale: " + path);
                    }
                }
                if (args.Length != 4)
                {
                    Directory.CreateDirectory(args[2]);
                    foreach (var output in outputs) File.WriteAllText(Path.Combine(args[2], output.Key), output.Value, new UTF8Encoding(false));
                }
                Console.WriteLine("Configuration " + (args.Length == 4 ? "verified" : "generated") + ": " + outputs.Count + " files.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        private static int GenerateProtocol(string[] args)
        {
            if (args.Length < 4 || args.Length > 5 || (args.Length == 5 && args[4] != "--check"))
                throw new ArgumentException("Usage: ConfigGen --protocol <schema.json> <baseline.json> <output.cs> [--check]");
            var output = ProtocolCompiler.Compile(File.ReadAllText(args[1]), File.ReadAllText(args[2]));
            if (args.Length == 5)
            {
                if (!File.Exists(args[3]) || File.ReadAllText(args[3]) != output) throw new InvalidDataException("Generated protocol is stale: " + args[3]);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3])));
                File.WriteAllText(args[3], output, new UTF8Encoding(false));
            }
            Console.WriteLine("Protocol " + (args.Length == 5 ? "verified." : "generated."));
            return 0;
        }
    }
}
