using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlteredOwnership.Server.Data.Entities;

namespace AlteredOwnership.Server.Domain.Events;

public static class RewardEvent
{
    public const EventKind Kind = EventKind.RewardEvent;
    public const int CurrentVersion = 1;
	
    public record PayloadV1(
        int Version,
        string CardReference,
        int Quantity,
        string AcquiredFrom);

    public static PayloadV1 Build(string cardReference, int quantity, string acquiredFrom)
        => new(CurrentVersion, cardReference, quantity, acquiredFrom);
		
    public static void Apply(Dictionary<string, int> state, JsonDocument payloadJson)
    {
        var version = payloadJson.RootElement.GetProperty("Version").GetInt32();
		
        switch (version)
        {
            case 1:
                ApplyV1(state,
                    payloadJson.Deserialize<PayloadV1>() ??
                    throw new InvalidOperationException("Cannot deserialize RewardEvent payload."));
                break;
            default:
                throw new NotSupportedException($"RewardEvent payload version {version} is not supported");
        }
    }
	
    private static void ApplyV1(Dictionary<string, int> state, PayloadV1 payload)
    {
        if (payload.Quantity <= 0) return;
        state[payload.CardReference] = state.GetValueOrDefault(payload.CardReference) + payload.Quantity;
    }

    // public static string ComputeHash(PayloadV1 payload)
    // {
    //     var canonical = string.Join("|", payload);
    //     return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    // }
}