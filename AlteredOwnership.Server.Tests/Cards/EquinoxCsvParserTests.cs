using System.Text;
using AlteredOwnership.Server.Domain;

namespace AlteredOwnership.Server.Tests.Cards;

public class EquinoxCsvParserTests
{
    private static Stream ToStream(string csv) =>
        new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public async Task Parses_rows_and_keeps_reference_and_quantity()
    {
        const string csv =
            "card_reference;card_name;rarity;quantity\n" +
            "ALT_ALIZE_A_AX_35_C;\"Vaike, l'Énergéticienne\";Commun;3\n" +
            "ALT_ALIZE_B_AX_32_U_4624;\"La machine\";Unique;1\n";

        var rows = await EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None);

        Assert.Collection(rows,
            r => { Assert.Equal("ALT_ALIZE_A_AX_35_C", r.Reference); Assert.Equal(3, r.Quantity); },
            r => { Assert.Equal("ALT_ALIZE_B_AX_32_U_4624", r.Reference); Assert.Equal(1, r.Quantity); });
    }

    [Fact]
    public async Task Skips_blank_lines()
    {
        const string csv =
            "card_reference;card_name;rarity;quantity\n" +
            "\n" +
            "ALT_ALIZE_A_AX_35_C;name;Commun;2\n" +
            "   \n";

        var rows = await EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Quantity);
    }

    [Fact]
    public async Task Throws_on_empty_file()
    {
        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(""), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_on_non_integer_quantity()
    {
        const string csv =
            "card_reference;card_name;rarity;quantity\n" +
            "ALT_ALIZE_A_AX_35_C;name;Commun;abc\n";

        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_row_has_too_few_fields()
    {
        const string csv =
            "card_reference;card_name;rarity;quantity\n" +
            "ALT_ALIZE_A_AX_35_C;name;Commun\n";

        await Assert.ThrowsAsync<FormatException>(
            () => EquinoxCsvParser.ParseAsync(ToStream(csv), CancellationToken.None));
    }
}
