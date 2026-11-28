using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Test.Application.BBads.Configuration
{
    public sealed class BBadsConfigurationException : FormatException
    {
        internal BBadsConfigurationException(string table, int row, string field, string reason)
            : base($"{table}.csv, row {row}, field '{field}': {reason}")
        {
            Table = table;
            Row = row;
            Field = field;
        }

        public string Table { get; }

        // The one-based physical line on which the record starts.
        public int Row { get; }

        public string Field { get; }
    }

    internal sealed class BBadsCsv
    {
        private readonly string table;
        private readonly string text;
        private int offset;
        private int line = 1;

        private BBadsCsv(string table, string text)
        {
            this.table = table;
            this.text = text;
            if (text.Length > 0 && text[0] == '\uFEFF')
                offset++;
        }

        internal static IEnumerable<Row> Read(Func<string, string> readCsv, string table, string schema)
        {
            var text = readCsv(table);
            if (text == null)
                throw new BBadsConfigurationException(table, 1, "<table>", "CSV source is missing.");

            var parser = new BBadsCsv(table, text);
            var columns = schema.Split(',');
            if (!parser.TryReadRecord(null, out var headerLine, out var header))
                throw new BBadsConfigurationException(table, 1, "<header>", "CSV header is missing.");

            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var expected = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                var field = header[index].Trim();
                if (!expected.Contains(field))
                    throw new BBadsConfigurationException(table, headerLine, FieldName(null, index, field), "Unexpected column.");
                if (!indexes.TryAdd(field, index))
                    throw new BBadsConfigurationException(table, headerLine, field, "Duplicate column.");
            }

            foreach (var field in columns)
            {
                if (!indexes.ContainsKey(field))
                    throw new BBadsConfigurationException(table, headerLine, field, "Required column is missing.");
            }

            while (parser.TryReadRecord(header, out var rowLine, out var values))
            {
                if (values.Count != header.Count)
                {
                    var field = FieldName(header, Math.Min(values.Count, header.Count));
                    throw new BBadsConfigurationException(table, rowLine, field,
                        $"Expected {header.Count} fields, found {values.Count}.");
                }

                yield return new Row(table, rowLine, indexes, values);
            }
        }

        private bool TryReadRecord(IReadOnlyList<string> header, out int rowLine, out List<string> fields)
        {
            // Whitespace-only lines may separate records. An explicit empty field is still data.
            while (offset < text.Length)
            {
                var next = offset;
                while (next < text.Length && (text[next] == ' ' || text[next] == '\t'))
                    next++;
                if (next == text.Length || IsNewline(text[next]))
                {
                    offset = next;
                    if (offset < text.Length)
                        ReadNewline();
                    continue;
                }
                break;
            }

            rowLine = line;
            fields = null;
            if (offset == text.Length)
                return false;

            fields = new List<string>();
            while (true)
            {
                fields.Add(ReadField(header, rowLine, fields.Count));
                if (offset == text.Length)
                    return true;
                if (IsNewline(text[offset]))
                {
                    ReadNewline();
                    return true;
                }

                offset++; // The comma between fields, including an empty final field.
            }
        }

        private string ReadField(IReadOnlyList<string> header, int rowLine, int index)
        {
            if (offset < text.Length && text[offset] == '"')
            {
                var value = new StringBuilder();
                offset++;
                while (offset < text.Length)
                {
                    if (IsNewline(text[offset]))
                    {
                        var start = offset;
                        ReadNewline();
                        value.Append(text, start, offset - start);
                        continue;
                    }

                    var current = text[offset++];
                    if (current != '"')
                    {
                        value.Append(current);
                        continue;
                    }
                    if (offset < text.Length && text[offset] == '"')
                    {
                        offset++;
                        value.Append('"');
                        continue;
                    }
                    if (AtFieldEnd())
                        return value.ToString();
                    throw new BBadsConfigurationException(table, rowLine, FieldName(header, index), "Unexpected text after a closing quote.");
                }
                throw new BBadsConfigurationException(table, rowLine, FieldName(header, index), "Unclosed quoted field.");
            }

            var fieldStart = offset;
            while (!AtFieldEnd())
            {
                if (text[offset] == '"')
                    throw new BBadsConfigurationException(table, rowLine, FieldName(header, index), "Unexpected quote in an unquoted field.");
                offset++;
            }
            return text.Substring(fieldStart, offset - fieldStart);
        }

        private bool AtFieldEnd() => offset == text.Length || text[offset] == ',' || IsNewline(text[offset]);

        private void ReadNewline()
        {
            if (text[offset++] == '\r' && offset < text.Length && text[offset] == '\n')
                offset++;
            line++;
        }

        private static bool IsNewline(char value) => value == '\r' || value == '\n';

        private static string FieldName(IReadOnlyList<string> header, int index, string name = "") =>
            header != null && index < header.Count ? header[index].Trim() :
            name.Length > 0 ? name : $"column {index + 1}";

        internal sealed class Row
        {
            private readonly string table;
            private readonly int line;
            private readonly IReadOnlyDictionary<string, int> indexes;
            private readonly IReadOnlyList<string> values;

            internal Row(string table, int line, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<string> values)
            {
                this.table = table;
                this.line = line;
                this.indexes = indexes;
                this.values = values;
            }

            internal string Text(string field) => values[indexes[field]].Trim();

            internal string Required(string field)
            {
                var value = Text(field);
                if (value.Length == 0)
                    throw Error(field, "A value is required.");
                return value;
            }

            internal float Number(string field, float minimum = float.MinValue)
            {
                var value = Text(field);
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    float.IsNaN(number) || float.IsInfinity(number))
                    throw Error(field, $"Expected a finite number, found '{value}'.");
                if (number < minimum)
                    throw Error(field, $"Expected at least {minimum.ToString(CultureInfo.InvariantCulture)}, found '{value}'.");
                return number;
            }

            internal int Integer(string field, int minimum)
            {
                var value = Text(field);
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < minimum)
                    throw Error(field, $"Expected an integer of at least {minimum}, found '{value}'.");
                return number;
            }

            internal bool Boolean(string field)
            {
                var value = Text(field);
                if (bool.TryParse(value, out var boolean))
                    return boolean;
                if (value == "0" || value == "1")
                    return value == "1";
                throw Error(field, $"Expected true, false, 0 or 1, found '{value}'.");
            }

            internal T Choice<T>(string field) where T : struct, Enum
            {
                var value = Text(field);
                if (Enum.TryParse<T>(value, true, out var choice) &&
                    string.Equals(value, Enum.GetName(typeof(T), choice), StringComparison.OrdinalIgnoreCase))
                    return choice;
                throw Error(field, $"Expected {string.Join(", ", Enum.GetNames(typeof(T)))}, found '{value}'.");
            }

            internal BBadsConfigurationException Error(string field, string reason) =>
                new BBadsConfigurationException(table, line, field, reason);
        }
    }
}
