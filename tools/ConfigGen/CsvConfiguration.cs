using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AwithGameFrame.Tools.Configuration
{
    public static partial class ConfigurationCompiler
    {
        private static readonly JsonSerializerOptions CsvJsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Compiles one JSON, CSV or XLSX source per table from a flat data directory.</summary>
        public static IReadOnlyDictionary<string, string> CompileDirectory(string schemaJson, string dataDirectory)
        {
            using var schema = JsonDocument.Parse(schemaJson);
            var model = new ConfigurationSchema(schema.RootElement);
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(dataDirectory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".csv" or ".json" or ".xlsx")) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                var table = model.Tables.SingleOrDefault(definition => definition.Name == name);
                if (table == null) throw new InvalidDataException("Unknown data table: " + name);
                if (data.ContainsKey(name)) throw new InvalidDataException("Multiple data sources for table " + name + ". Choose one file.");
                if (extension == ".xlsx")
                {
                    data.Add(name, RecordsToJson(ReadWorkbook(path, name), model, table, "XLSX"));
                    continue;
                }
                var source = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
                if (source.Length != 0 && source[0] == '\uFEFF') source = source.Substring(1);
                data.Add(name, extension == ".csv"
                    ? RecordsToJson(ParseCsv(source, name), model, table, "CSV") : source);
            }
            return Compile(schemaJson, data);
        }

        private static string RecordsToJson(List<string[]> records, ConfigurationSchema schema, ConfigurationSchema.Definition table, string format)
        {
            var label = table.Name + " " + format;
            if (records.Count == 0) throw new InvalidDataException(label + " requires a header.");
            var header = records[0];
            if (header.Length != table.Fields.Count || header.Distinct(StringComparer.Ordinal).Count() != header.Length ||
                header.Any(name => !table.Fields.Any(field => field.Name == name)))
                throw new InvalidDataException(label + " header must contain every field exactly once.");
            var fields = header.Select(name => table.Fields.Single(field => field.Name == name)).ToArray();
            var rows = new List<string>();
            for (int record = 1; record < records.Count; record++)
            {
                var cells = records[record];
                if (cells.Length != header.Length)
                    throw new InvalidDataException(label + " record " + (record + 1) + " has " + cells.Length + " columns; expected " + header.Length + ".");
                var properties = new List<string>();
                for (int column = 0; column < fields.Length; column++)
                {
                    var field = fields[column];
                    var cell = cells[column];
                    string json;
                    if (field.Type.Element == null && field.Type.Name == "string") json = JsonSerializer.Serialize(cell, CsvJsonOptions);
                    else
                    {
                        cell = cell.Trim();
                        if (field.Type.Element == null && schema.Enums.ContainsKey(field.Type.Name) &&
                            !int.TryParse(cell, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _))
                            json = JsonSerializer.Serialize(cell, CsvJsonOptions);
                        else
                        {
                            if (field.Type.Element == null && field.Type.Name == "bool") cell = cell.ToLowerInvariant();
                            try
                            {
                                using var parsed = JsonDocument.Parse(cell);
                                json = parsed.RootElement.GetRawText();
                            }
                            catch (JsonException error)
                            {
                                throw new InvalidDataException(label + " record " + (record + 1) + " field " + field.Name + " requires a valid " + (field.Type.Element == null ? field.Type.Name : "array JSON") + " value.", error);
                            }
                        }
                    }
                    properties.Add(JsonSerializer.Serialize(field.Name) + ":" + json);
                }
                var rowJson = "{" + string.Join(",", properties) + "}";
                using (var row = JsonDocument.Parse(rowJson)) schema.ValidateObject(row.RootElement, table, label + " record " + (record + 1));
                rows.Add(rowJson);
            }
            return "[" + string.Join(",", rows) + "]";
        }

        // Quoted fields preserve commas, escaped quotes, and their original embedded newlines.
        private static List<string[]> ParseCsv(string source, string table)
        {
            var records = new List<string[]>();
            var cells = new List<string>();
            var value = new StringBuilder();
            bool quoted = false, closedQuote = false, started = false;
            for (int index = 0; index < source.Length; index++)
            {
                char character = source[index];
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '"') { value.Append('"'); index++; }
                        else { quoted = false; closedQuote = true; }
                    }
                    else value.Append(character);
                    continue;
                }
                if (character == ',' || character == '\r' || character == '\n')
                {
                    cells.Add(value.ToString());
                    value.Clear();
                    closedQuote = false;
                    started = character == ',';
                    if (character != ',')
                    {
                        records.Add(cells.ToArray());
                        cells.Clear();
                        if (character == '\r' && index + 1 < source.Length && source[index + 1] == '\n') index++;
                    }
                }
                else if (character == '"' && value.Length == 0 && !closedQuote) { quoted = true; started = true; }
                else
                {
                    if (character == '"' || closedQuote)
                        throw new InvalidDataException(table + " CSV record " + (records.Count + 1) + " contains an unexpected character around a quoted field.");
                    value.Append(character);
                    started = true;
                }
            }
            if (quoted) throw new InvalidDataException(table + " CSV record " + (records.Count + 1) + " has an unterminated quoted field.");
            if (started || closedQuote || cells.Count != 0 || value.Length != 0)
            {
                cells.Add(value.ToString());
                records.Add(cells.ToArray());
            }
            return records;
        }
    }
}
