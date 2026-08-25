using System.Text.Json;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.EventSourcing;

namespace AlteredOwnership.Server.Domain.Events;

// An admin distribution: any mix of fixed cards and booster grants to one user,
// recorded as a single event so the history page shows one line per distribution
// instead of one per item. Never shipped to production, so the payload was changed
// in place rather than bumping to a V2 — but dev/preprod environments that
// exercised the reward feature before this change already have Version:1 events in
// the old single-card shape ({CardReference, Quantity}, no Cards/Boosters lists at
// all), so reads still have to tolerate that shape (see DeserializePayloadV1).
public static class RewardEvent
{
    public const EventKind Kind = EventKind.RewardEvent;
    public const int CurrentVersion = 1;

    public record PayloadV1(
        int Version,
        IReadOnlyList<PayloadV1.Item> Cards,
        IReadOnlyList<PayloadV1.BoosterItem> Boosters,
        string AcquiredFrom)
    {
        public record Item(string Reference, int Quantity);
        public record BoosterItem(string BoosterTypeKey, int Quantity);
    }

    public static PayloadV1 Build(
        IReadOnlyList<PayloadV1.Item> cards, IReadOnlyList<PayloadV1.BoosterItem> boosters, string acquiredFrom)
        => new(CurrentVersion, cards, boosters, acquiredFrom);

    // Pre-existing Version:1 events written before Cards/Boosters existed have a
    // top-level "CardReference"/"Quantity" instead — reshape those into the current
    // record instead of deserializing straight into it (which leaves Cards/Boosters
    // null and crashes Apply/Describe).
    private static PayloadV1 DeserializePayloadV1(JsonDocument payloadJson)
    {
        var root = payloadJson.RootElement;
        if (root.TryGetProperty("CardReference", out var legacyReference))
        {
            var quantity = root.GetProperty("Quantity").GetInt32();
            var acquiredFrom = root.GetProperty("AcquiredFrom").GetString()!;
            return new PayloadV1(1, [new PayloadV1.Item(legacyReference.GetString()!, quantity)], [], acquiredFrom);
        }

        return payloadJson.Deserialize<PayloadV1>()
            ?? throw new InvalidOperationException("Cannot deserialize RewardEvent payload.");
    }

    public static void Apply(ProjectionState state, JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();

        switch (version)
        {
            case 1:
                ApplyV1(state, DeserializePayloadV1(payloadJson));
                break;
            default:
                throw new NotSupportedException($"RewardEvent payload version {version} is not supported");
        }
    }

    private static void ApplyV1(ProjectionState state, PayloadV1 payload)
    {
        foreach (var item in payload.Cards)
        {
            if (item.Quantity <= 0) continue;
            state.Cards[item.Reference] = state.Cards.GetValueOrDefault(item.Reference) + item.Quantity;
        }

        foreach (var booster in payload.Boosters)
        {
            if (booster.Quantity <= 0) continue;
            state.Boosters[booster.BoosterTypeKey] = state.Boosters.GetValueOrDefault(booster.BoosterTypeKey) + booster.Quantity;
        }
    }

    // For the history page: the event's name is the free-text AcquiredFrom the admin
    // entered (e.g. an event name), not a fixed label.
    public static EventDescription Describe(JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
        return version switch
        {
            1 => DescribeV1(DeserializePayloadV1(payloadJson)),
            _ => throw new NotSupportedException($"RewardEvent payload version {version} is not supported"),
        };
    }

    private static EventDescription DescribeV1(PayloadV1 payload)
    {
        var items = payload.Cards
            .Select(c => new EventItemDelta(c.Reference, c.Quantity))
            .Concat(payload.Boosters.Select(b => new EventItemDelta(b.BoosterTypeKey, b.Quantity, EventItemKind.Booster)))
            .ToList();
        return new EventDescription(payload.AcquiredFrom, items);
    }
}
