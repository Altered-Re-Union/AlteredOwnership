using System.Text.Json;
using AlteredOwnership.Server.Domain.Events;

namespace AlteredOwnership.Server.Tests.Events;

public class EquinoxImportEventTests
{
    private static EquinoxImportEvent.PayloadV1 Payload(params (string Ref, int Qty)[] cards) =>
        EquinoxImportEvent.Build(
            termsAccepted: true,
            cards.Select(c => new EquinoxImportEvent.PayloadV1.Item(c.Ref, c.Qty)).ToList());

    private static Dictionary<string, int> Apply(EquinoxImportEvent.PayloadV1 payload)
    {
        var state = new Dictionary<string, int>();
        var json = JsonSerializer.SerializeToDocument(payload);
        EquinoxImportEvent.Apply(state, json);
        return state;
    }

    [Fact]
    public void Apply_keeps_alt_art_cards()
    {
        var payload = Payload(
            ("ALT_ALIZE_A_AX_35_C", 3),
            ("ALT_DUSTERTOP_B_AX_01_C", 1));

        var state = Apply(payload);

        Assert.Equal(3, state["ALT_ALIZE_A_AX_35_C"]);
        Assert.Equal(1, state["ALT_DUSTERTOP_B_AX_01_C"]);
    }

    [Fact]
    public void Apply_keeps_unique_cards()
    {
        var payload = Payload(("ALT_ALIZE_B_AX_32_U_4624", 1));

        var state = Apply(payload);

        Assert.Equal(1, state["ALT_ALIZE_B_AX_32_U_4624"]);
    }

    [Fact]
    public void Apply_drops_non_alt_non_unique_cards()
    {
        var payload = Payload(
            ("ALT_ALIZE_B_AX_32_C", 6),
            ("ALT_ALIZE_B_AX_32_R1", 2));

        var state = Apply(payload);

        Assert.Empty(state);
    }

    [Fact]
    public void Apply_drops_rows_with_zero_or_negative_quantity()
    {
        var payload = Payload(
            ("ALT_ALIZE_A_AX_35_C", 0),
            ("ALT_ALIZE_A_AX_36_C", -1));

        var state = Apply(payload);

        Assert.Empty(state);
    }

    [Fact]
    public void Apply_accumulates_quantities_for_same_reference()
    {
        var payload = Payload(
            ("ALT_ALIZE_A_AX_35_C", 2),
            ("ALT_ALIZE_A_AX_35_C", 3));

        var state = Apply(payload);

        Assert.Equal(5, state["ALT_ALIZE_A_AX_35_C"]);
    }

    [Fact]
    public void Apply_unsupported_version_throws()
    {
        var json = JsonSerializer.SerializeToDocument(new { Version = 99, Cards = Array.Empty<object>() });
        Assert.Throws<NotSupportedException>(() => EquinoxImportEvent.Apply(new Dictionary<string, int>(), json));
    }

    [Fact]
    public void ComputeHash_is_deterministic_regardless_of_order()
    {
        var a = Payload(("ALT_ALIZE_A_AX_35_C", 2), ("ALT_ALIZE_A_AX_36_C", 4));
        var b = Payload(("ALT_ALIZE_A_AX_36_C", 4), ("ALT_ALIZE_A_AX_35_C", 2));

        Assert.Equal(EquinoxImportEvent.ComputeHash(a), EquinoxImportEvent.ComputeHash(b));
    }

    [Fact]
    public void ComputeHash_changes_when_quantity_changes()
    {
        var a = Payload(("ALT_ALIZE_A_AX_35_C", 2));
        var b = Payload(("ALT_ALIZE_A_AX_35_C", 3));

        Assert.NotEqual(EquinoxImportEvent.ComputeHash(a), EquinoxImportEvent.ComputeHash(b));
    }
}
