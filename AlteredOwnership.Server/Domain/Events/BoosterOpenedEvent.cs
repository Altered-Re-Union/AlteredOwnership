using System.Text.Json;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Boosters;
using AlteredOwnership.Server.Infrastructure.EventSourcing;

namespace AlteredOwnership.Server.Domain.Events;

// One "open" action: consumes some quantity of one booster type and reveals the
// cards drawn for it, all as a single event so the history page shows one line per
// open regardless of how many boosters were opened at once or how many cards each
// contained. CardReferences holds every card drawn across the whole action (today
// always exactly one per booster opened, since every current BoosterType draws a
// single unique — but the shape already supports a future multi-card booster
// without a new payload version).
public static class BoosterOpenedEvent
{
    public const EventKind Kind = EventKind.BoosterOpened;
    public const int CurrentVersion = 1;

    public record PayloadV1(int Version, string BoosterTypeKey, int BoostersOpened, IReadOnlyList<string> CardReferences);

    public static PayloadV1 Build(string boosterTypeKey, int boostersOpened, IReadOnlyList<string> cardReferences)
        => new(CurrentVersion, boosterTypeKey, boostersOpened, cardReferences);

    public static void Apply(ProjectionState state, JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
        switch (version)
        {
            case 1:
                ApplyV1(state, payloadJson.Deserialize<PayloadV1>()
                    ?? throw new InvalidOperationException("Cannot deserialize BoosterOpened payload."));
                break;
            default:
                throw new NotSupportedException($"BoosterOpened payload version {version} is not supported");
        }
    }

    private static void ApplyV1(ProjectionState state, PayloadV1 payload)
    {
        state.Boosters[payload.BoosterTypeKey] = state.Boosters.GetValueOrDefault(payload.BoosterTypeKey) - payload.BoostersOpened;

        foreach (var reference in payload.CardReferences)
            state.Cards[reference] = state.Cards.GetValueOrDefault(reference) + 1;
    }

    public static EventDescription Describe(JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
        return version switch
        {
            1 => DescribeV1(payloadJson.Deserialize<PayloadV1>()
                ?? throw new InvalidOperationException("Cannot deserialize BoosterOpened payload.")),
            _ => throw new NotSupportedException($"BoosterOpened payload version {version} is not supported"),
        };
    }

    private static EventDescription DescribeV1(PayloadV1 payload)
    {
        var items = new List<EventItemDelta>
        {
            new(payload.BoosterTypeKey, -payload.BoostersOpened, EventItemKind.Booster),
        };
        items.AddRange(payload.CardReferences.Select(r => new EventItemDelta(r, 1)));
        var boosterName = BoosterCatalog.Find(payload.BoosterTypeKey)?.Name ?? payload.BoosterTypeKey;
        return new EventDescription($"Ouverture de booster : {boosterName}", items);
    }
}
