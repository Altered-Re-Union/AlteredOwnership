using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlteredOwnership.Server.Data.Entities;

namespace AlteredOwnership.Server.Domain.Events;

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

    // Deterministic fingerprint of an Equinox export, used to reject re-imports of
    // the same file globally. ExportedAt is intentionally excluded for now: it's
    // hardcoded server-side until Equinox ships the field, so it carries no signal.
    public static string ComputeHash(PayloadV1 payload)
    {
        var canonical = string.Join("|", payload.Cards
            .OrderBy(c => c.Reference, StringComparer.Ordinal)
            .Select(c => $"{c.Reference}:{c.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

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
            if (!CardReferenceParser.IsAlternateArt(item.Reference) && !CardReferenceParser.IsUnique(item.Reference)) continue;

            state[item.Reference] = state.GetValueOrDefault(item.Reference) + item.Quantity;
        }
    }
}
