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
        IReadOnlyList<PayloadV1.Item> Cards)
    {
        public bool TermsAccepted { get; init; }
        public record Item(string Reference, int Quantity);
    }

    public static PayloadV1 Build(bool termsAccepted, IReadOnlyList<PayloadV1.Item> cards)
        => new(CurrentVersion, cards) { TermsAccepted = termsAccepted };

    // Deterministic fingerprint of an Equinox export, used to reject re-imports of
    // the same collection globally. The export timestamp is intentionally excluded:
    // re-importing the same cards under a different timestamp is still a duplicate.
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
