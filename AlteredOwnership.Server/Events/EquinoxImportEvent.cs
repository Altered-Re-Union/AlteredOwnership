using System.Text.Json;

namespace AlteredOwnership.Server.Events;

// All the logic for a single event kind lives in one file:
// payload definition(s) per version, apply mutation, and validation.
public static class EquinoxImportEvent
{
    public const EventKind Kind = EventKind.EquinoxImport;
    public const int CurrentVersion = 1;

    public record PayloadV1(
        int Version,
        DateTimeOffset ExportedAt,
        IReadOnlyList<PayloadV1.Item> Cards)
    {
        public record Item(string Reference, int Quantity);
    }

    public static PayloadV1 Build(DateTimeOffset exportedAt, IReadOnlyList<PayloadV1.Item> cards)
        => new(CurrentVersion, exportedAt, cards);

    public static void Apply(Dictionary<string, int> state, JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
        switch (version)
        {
            case 1:
                ApplyV1(state, payloadJson.Deserialize<PayloadV1>()
                    ?? throw new InvalidOperationException("Cannot deserialize EquinoxImport V1 payload"));
                break;

            default:
                throw new NotSupportedException($"EquinoxImport payload version {version} is not supported");
        }
    }

    private static void ApplyV1(Dictionary<string, int> state, PayloadV1 payload)
    {
        foreach (var item in payload.Cards)
        {
            if (item.Quantity <= 0) continue;

            state[item.Reference] = state.GetValueOrDefault(item.Reference) + item.Quantity;
        }
    }
}
