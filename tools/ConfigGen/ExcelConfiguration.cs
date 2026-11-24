using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;

namespace AwithGameFrame.Tools.Configuration
{
    public static partial class ConfigurationCompiler
    {
        private static List<string[]> ReadWorkbook(string path, string table)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var file = File.OpenRead(path);
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(file);
            if (reader.ResultsCount != 1) throw new InvalidDataException(table + " XLSX requires one worksheet.");
            var records = new List<string[]>();
            int columns = 0;
            int row = 0;
            while (reader.Read())
            {
                row++;
                var cells = new string[reader.FieldCount];
                int lastValue = 0;
                for (int column = 0; column < cells.Length; column++)
                {
                    var value = reader.GetValue(column);
                    cells[column] = value switch
                    {
                        null => "",
                        string text => text,
                        bool boolean => boolean ? "true" : "false",
                        double number => number.ToString("R", CultureInfo.InvariantCulture),
                        int integer => integer.ToString(CultureInfo.InvariantCulture),
                        _ => throw new InvalidDataException($"{table} XLSX row {row}, column {column + 1} requires text, a number or a boolean.")
                    };
                    if (value != null) lastValue = column + 1;
                }
                if (row == 1) columns = lastValue;
                if (lastValue > columns)
                    throw new InvalidDataException($"{table} XLSX row {row} has data outside the header columns.");
                if (lastValue == 0 && row != 1) continue;
                records.Add(cells.Take(columns).ToArray());
            }
            return records;
        }
    }
}
