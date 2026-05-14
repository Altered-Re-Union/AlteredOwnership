using System.Globalization;
using System.Text;

namespace AlteredOwnership.Server.Domain;

public static class EquinoxCsvParser
{
    public record Row(string Reference, int Quantity);

    public static async Task<IReadOnlyList<Row>> ParseAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var header = await reader.ReadLineAsync(ct);
        if (header is null)
            throw new FormatException("CSV is empty.");

        var rows = new List<Row>();
        var lineNumber = 1;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitRow(line);
            if (fields.Count < 4)
                throw new FormatException($"Line {lineNumber}: expected 4 fields, got {fields.Count}.");

            var reference = fields[0].Trim();
            if (!int.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
                throw new FormatException($"Line {lineNumber}: quantity '{fields[3]}' is not an integer.");

            rows.Add(new Row(reference, quantity));
        }

        return rows;
    }

    private static List<string> SplitRow(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (c == ';' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
