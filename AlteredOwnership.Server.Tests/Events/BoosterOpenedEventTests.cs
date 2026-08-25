using System.Text.Json;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;

namespace AlteredOwnership.Server.Tests.Events;

public class BoosterOpenedEventTests
{
    private static JsonDocument Json(BoosterOpenedEvent.PayloadV1 payload) => JsonSerializer.SerializeToDocument(payload);

    [Fact]
    public void Apply_decrements_the_booster_type_and_increments_every_card_drawn()
    {
        var payload = BoosterOpenedEvent.Build("UNIQUE_RANDOM", 2, ["ALT_ALIZE_B_AX_01_U_1", "ALT_ALIZE_B_AX_02_U_2"]);
        var state = new ProjectionState();
        state.Boosters["UNIQUE_RANDOM"] = 2;

        BoosterOpenedEvent.Apply(state, Json(payload));

        Assert.Equal(0, state.Boosters["UNIQUE_RANDOM"]);
        Assert.Equal(1, state.Cards["ALT_ALIZE_B_AX_01_U_1"]);
        Assert.Equal(1, state.Cards["ALT_ALIZE_B_AX_02_U_2"]);
    }

    [Fact]
    public void Apply_can_drive_the_booster_count_negative_when_replayed_out_of_order()
    {
        // Apply itself doesn't enforce non-negative inventory — that's the DB check
        // constraint's job at reconcile time. Replay must still be a pure fold.
        var payload = BoosterOpenedEvent.Build("UNIQUE_RANDOM", 1, ["ALT_ALIZE_B_AX_01_U_1"]);
        var state = new ProjectionState();

        BoosterOpenedEvent.Apply(state, Json(payload));

        Assert.Equal(-1, state.Boosters["UNIQUE_RANDOM"]);
    }

    [Fact]
    public void Apply_unsupported_version_throws()
    {
        var json = JsonSerializer.SerializeToDocument(new { Version = 99 });
        Assert.Throws<NotSupportedException>(() => BoosterOpenedEvent.Apply(new ProjectionState(), json));
    }

    [Fact]
    public void Describe_is_a_single_description_covering_the_whole_open_action()
    {
        var payload = BoosterOpenedEvent.Build("UNIQUE_RANDOM", 2, ["ALT_ALIZE_B_AX_01_U_1", "ALT_ALIZE_B_AX_02_U_2"]);

        var description = BoosterOpenedEvent.Describe(Json(payload));

        Assert.Equal("Booster ouvert", description.Name);
        Assert.Equal(3, description.Items.Count); // 1 booster delta + 2 card deltas, one event
        Assert.Contains(description.Items, i => i is { Reference: "UNIQUE_RANDOM", Quantity: -2, Kind: EventItemKind.Booster });
        Assert.Contains(description.Items, i => i is { Reference: "ALT_ALIZE_B_AX_01_U_1", Quantity: 1, Kind: EventItemKind.Card });
        Assert.Contains(description.Items, i => i is { Reference: "ALT_ALIZE_B_AX_02_U_2", Quantity: 1, Kind: EventItemKind.Card });
    }
}
