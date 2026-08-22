using System.Text.Json;
using AlteredOwnership.Server.Domain.Events;

namespace AlteredOwnership.Server.Tests.Events;

public class RewardEventTests
{
    private static JsonDocument Json(RewardEvent.PayloadV1 payload) => JsonSerializer.SerializeToDocument(payload);

    [Fact]
    public void Apply_adds_quantity_for_the_reference()
    {
        var payload = RewardEvent.Build("ALT_ALIZE_A_AX_35_C", 2, "Event Paris");
        var state = new Dictionary<string, int>();

        RewardEvent.Apply(state, Json(payload));

        Assert.Equal(2, state["ALT_ALIZE_A_AX_35_C"]);
    }

    [Fact]
    public void Apply_drops_zero_or_negative_quantity()
    {
        var payload = RewardEvent.Build("ALT_ALIZE_A_AX_35_C", 0, "Event Paris");
        var state = new Dictionary<string, int>();

        RewardEvent.Apply(state, Json(payload));

        Assert.Empty(state);
    }

    [Fact]
    public void Apply_unsupported_version_throws()
    {
        var json = JsonSerializer.SerializeToDocument(new { Version = 99 });
        Assert.Throws<NotSupportedException>(() => RewardEvent.Apply(new Dictionary<string, int>(), json));
    }

    [Fact]
    public void Describe_uses_acquired_from_as_the_event_name()
    {
        var payload = RewardEvent.Build("ALT_ALIZE_A_AX_35_C", 3, "Event Paris Août 2026");

        var description = RewardEvent.Describe(Json(payload));

        Assert.Equal("Event Paris Août 2026", description.Name);
        var item = Assert.Single(description.Items);
        Assert.Equal("ALT_ALIZE_A_AX_35_C", item.Reference);
        Assert.Equal(3, item.Quantity);
    }
}
