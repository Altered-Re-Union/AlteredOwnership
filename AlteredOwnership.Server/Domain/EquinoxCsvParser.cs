using System.Globalization;
using System.Text;

namespace AlteredOwnership.Server.Domain;

public static class EquinoxCsvParser
{
    public record Row(string Reference, int Quantity);

    public record ParseResult(DateTimeOffset ExportedAt, IReadOnlyList<Row> Rows);

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    public static async Task<ParseResult> ParseAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // The export now leads with a timestamp line (e.g. "2026-05-20 17:14:43";;;)
        // before the column header. Imports without it are refused.
        var timestampLine = await reader.ReadLineAsync(ct);
        if (timestampLine is null)
            throw new FormatException("CSV is empty.");

        var exportedAt = ParseTimestamp(timestampLine);

        // Skip the column header (card_reference;card_name;rarity;quantity).
        await reader.ReadLineAsync(ct);

        var rows = new List<Row>();
        var lineNumber = 2;
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

        return new ParseResult(exportedAt, rows);
    }

    private static DateTimeOffset ParseTimestamp(string line)
    {
        var firstField = SplitRow(line)[0].Trim();
        if (!DateTime.TryParseExact(firstField, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new FormatException(
                "this export has no timestamp. The first generated files didn't have it. Request it again and the data will be here.");

        // The export timestamp carries no timezone; treat it as UTC.
        return new DateTimeOffset(parsed, TimeSpan.Zero);
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
