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

    // Version of the published collection terms (the EULA PDF) in force. Bumped when that
    // document changes, so every import records which terms version the user validated.
    // Events predating this field deserialize to 0 ("accepted before terms were versioned").
    public const int CurrentTermsVersion = 1;

    public record PayloadV1(
        int Version,
        IReadOnlyList<PayloadV1.Item> Cards)
    {
        public bool TermsAccepted { get; init; }
        public int TermsVersion { get; init; }
        public record Item(string Reference, int Quantity);
    }

    public static PayloadV1 Build(bool termsAccepted, IReadOnlyList<PayloadV1.Item> cards)
        => new(CurrentVersion, cards) { TermsAccepted = termsAccepted, TermsVersion = CurrentTermsVersion };

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

    // For the history page: what this one import's payload contributed, filtered to
    // the same alt-art/unique subset Apply folds into the projection (a raw Equinox
    // export lists commons/rares too, which the app never tracks).
    public static EventDescription Describe(JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
        return version switch
        {
            1 => DescribeV1(payloadJson.Deserialize<PayloadV1>()
                ?? throw new InvalidOperationException("Cannot deserialize EquinoxImport V1 payload")),
            _ => throw new NotSupportedException($"EquinoxImport payload version {version} is not supported"),
        };
    }

    private static EventDescription DescribeV1(PayloadV1 payload)
    {
        var items = payload.Cards
            .Where(c => c.Quantity > 0
                && (CardReferenceParser.IsAlternateArt(c.Reference) || CardReferenceParser.IsUnique(c.Reference)))
            .Select(c => new EventItemDelta(c.Reference, c.Quantity))
            .ToList();
        return new EventDescription("Import de collection", items);
    }
}
