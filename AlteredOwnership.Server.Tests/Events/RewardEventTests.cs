using System.Text.Json;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;

namespace AlteredOwnership.Server.Tests.Events;

public class RewardEventTests
{
    private static JsonDocument Json(RewardEvent.PayloadV1 payload) => JsonSerializer.SerializeToDocument(payload);

    private static RewardEvent.PayloadV1 Payload(
        (string Reference, int Quantity)[]? cards = null,
        (string BoosterTypeKey, int Quantity)[]? boosters = null,
        string acquiredFrom = "Event Paris") =>
        RewardEvent.Build(
            (cards ?? []).Select(c => new RewardEvent.PayloadV1.Item(c.Reference, c.Quantity)).ToList(),
            (boosters ?? []).Select(b => new RewardEvent.PayloadV1.BoosterItem(b.BoosterTypeKey, b.Quantity)).ToList(),
            acquiredFrom);

    [Fact]
    public void Apply_adds_quantity_for_the_reference()
    {
        var payload = Payload(cards: [("ALT_ALIZE_A_AX_35_C", 2)]);
        var state = new ProjectionState();

        RewardEvent.Apply(state, Json(payload));

        Assert.Equal(2, state.Cards["ALT_ALIZE_A_AX_35_C"]);
    }

    [Fact]
    public void Apply_adds_booster_quantity_for_the_type_key()
    {
        var payload = Payload(boosters: [("UNIQUE_RANDOM", 3)]);
        var state = new ProjectionState();

        RewardEvent.Apply(state, Json(payload));

        Assert.Equal(3, state.Boosters["UNIQUE_RANDOM"]);
        Assert.Empty(state.Cards);
    }

    [Fact]
    public void Apply_folds_cards_and_boosters_from_the_same_event_into_their_own_projection()
    {
        var payload = Payload(
            cards: [("ALT_ALIZE_A_AX_35_C", 2)],
            boosters: [("UNIQUE_RANDOM", 1)]);
        var state = new ProjectionState();

        RewardEvent.Apply(state, Json(payload));

        Assert.Equal(2, state.Cards["ALT_ALIZE_A_AX_35_C"]);
        Assert.Equal(1, state.Boosters["UNIQUE_RANDOM"]);
    }

    [Fact]
    public void Apply_drops_zero_or_negative_quantity()
    {
        var payload = Payload(cards: [("ALT_ALIZE_A_AX_35_C", 0)], boosters: [("UNIQUE_RANDOM", 0)]);
        var state = new ProjectionState();

        RewardEvent.Apply(state, Json(payload));

        Assert.Empty(state.Cards);
        Assert.Empty(state.Boosters);
    }

    [Fact]
    public void Apply_unsupported_version_throws()
    {
        var json = JsonSerializer.SerializeToDocument(new { Version = 99 });
        Assert.Throws<NotSupportedException>(() => RewardEvent.Apply(new ProjectionState(), json));
    }

    [Fact]
    public void Describe_uses_acquired_from_as_the_event_name()
    {
        var payload = Payload(cards: [("ALT_ALIZE_A_AX_35_C", 3)], acquiredFrom: "Event Paris Août 2026");

        var description = RewardEvent.Describe(Json(payload));

        Assert.Equal("Event Paris Août 2026", description.Name);
        var item = Assert.Single(description.Items);
        Assert.Equal("ALT_ALIZE_A_AX_35_C", item.Reference);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(EventItemKind.Card, item.Kind);
    }

    [Fact]
    public void Describe_tags_booster_items_and_keeps_card_items_untagged()
    {
        var payload = Payload(
            cards: [("ALT_ALIZE_A_AX_35_C", 1)],
            boosters: [("UNIQUE_RANDOM", 2)]);

        var description = RewardEvent.Describe(Json(payload));

        Assert.Equal(2, description.Items.Count);
        Assert.Contains(description.Items, i => i is { Reference: "ALT_ALIZE_A_AX_35_C", Quantity: 1, Kind: EventItemKind.Card });
        Assert.Contains(description.Items, i => i is { Reference: "UNIQUE_RANDOM", Quantity: 2, Kind: EventItemKind.Booster });
    }
}
