using System.Net.Http.Json;

namespace AlteredOwnership.Server.Infrastructure.Cards;

// Client for the cards.alteredcore.org catalog API. With no locale requested the API
// returns each localized field as an object keyed by language, so a single call yields
// the card text in every language.
public interface IAlteredCardsClient
{
    Task<IReadOnlyList<CardDto>> FetchBatchAsync(IReadOnlyCollection<string> references, CancellationToken ct);
}

public sealed class AlteredCardsClient(HttpClient http) : IAlteredCardsClient
{
    public async Task<IReadOnlyList<CardDto>> FetchBatchAsync(
        IReadOnlyCollection<string> references, CancellationToken ct)
    {
        if (references.Count == 0)
            return [];

        var response = await http.PostAsJsonAsync("api/cards/batch", new BatchRequest(references), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<CardDto>>(ct) ?? [];
    }

    private sealed record BatchRequest(IReadOnlyCollection<string> References);
}

// Shape of one card in the batch response (camelCase matched case-insensitively).
// Name/ImagePath are language-keyed maps (e.g. {"fr":"...","en":"..."}).
public sealed record CardDto
{
    public string Reference { get; init; } = default!;
    public Dictionary<string, string>? Name { get; init; }
    public Dictionary<string, string>? ImagePath { get; init; }
    public RefValue? Set { get; init; }
    public FactionValue? Faction { get; init; }
    public RefValue? Rarity { get; init; }
    public RefValue? CardType { get; init; }
    public IReadOnlyList<RefValue>? CardSubTypes { get; init; }
    public string? Variation { get; init; }
    public bool IsBanned { get; init; }
    public bool IsSuspended { get; init; }
    public int? MainCost { get; init; }
    public int? RecallCost { get; init; }
    public int? OceanPower { get; init; }
    public int? MountainPower { get; init; }
    public int? ForestPower { get; init; }
}

public sealed record RefValue(string? Reference);

public sealed record FactionValue(string? Code);
