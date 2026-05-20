using System.Text;
using AlteredOwnership.Server.Domain;

namespace AlteredOwnership.Server.Tests.Cards;

public class EquinoxCsvParserTests
{
    private const string Timestamp = "\"2026-05-20 17:14:43\";;;\n";
    private const string Header = "card_reference;card_name;rarity;quantity\n";

    private static Stream ToStream(string csv) =>
        new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public async Task Parses_rows_and_keeps_reference_and_quantity()
    {
        const string csv =
            Timestamp +
            Header +
            "ALT_ALIZE_A_AX_35_C;\"Vaike, l'Énergéticienne\";Commun;3\n" +
            "ALT_ALIZE_B_AX_32_U_4624;\"La machine\";Unique;1\n";

        var result = await EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None);

        Assert.Collection(result.Rows,
            r => { Assert.Equal("ALT_ALIZE_A_AX_35_C", r.Reference); Assert.Equal(3, r.Quantity); },
            r => { Assert.Equal("ALT_ALIZE_B_AX_32_U_4624", r.Reference); Assert.Equal(1, r.Quantity); });
    }

    [Fact]
    public async Task Parses_export_timestamp_as_utc()
    {
        const string csv =
            Timestamp +
            Header +
            "ALT_ALIZE_A_AX_35_C;name;Commun;1\n";

        var result = await EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 5, 20, 17, 14, 43, TimeSpan.Zero), result.ExportedAt);
    }

    [Fact]
    public async Task Skips_blank_lines()
    {
        const string csv =
            Timestamp +
            Header +
            "\n" +
            "ALT_ALIZE_A_AX_35_C;name;Commun;2\n" +
            "   \n";

        var result = await EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal(2, result.Rows[0].Quantity);
    }

    [Fact]
    public async Task Throws_on_empty_file()
    {
        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(""), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_timestamp_is_missing()
    {
        // Old-format export: starts straight with the column header, no timestamp line.
        const string csv =
            Header +
            "ALT_ALIZE_A_AX_35_C;name;Commun;1\n";

        var ex = await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None));

        Assert.Equal(
            "this export has no timestamp. The first generated files didn't have it. Request it again and the data will be here.",
            ex.Message);
    }

    [Fact]
    public async Task Throws_on_non_integer_quantity()
    {
        const string csv =
            Timestamp +
            Header +
            "ALT_ALIZE_A_AX_35_C;name;Commun;abc\n";

        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_row_has_too_few_fields()
    {
        const string csv =
            Timestamp +
            Header +
            "ALT_ALIZE_A_AX_35_C;name;Commun\n";

        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None));
    }
}
