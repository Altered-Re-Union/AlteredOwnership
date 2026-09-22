using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Infrastructure.Bga;

public interface IBgaApiClient
{
    Task<string?> ResolveReunionIdAsync(string bgaName, CancellationToken ct);
}

// Talks to altered-bga-api's GET /api/players/reunion-id, which resolves a BGA username
// to the Reunion (Keycloak) user id BGA sent alongside it on some past finished game --
// a lookup over that gateway's own game history, not a live BGA identity call. A name
// never seen in a finished game 404s, which is a normal "no match" here, not an error.
public sealed class BgaApiClient(HttpClient http, IOptions<BgaApiOptions> options) : IBgaApiClient
{
    private readonly BgaApiOptions _options = options.Value;

    public async Task<string?> ResolveReunionIdAsync(string bgaName, CancellationToken ct)
    {
        var query = $"api/players/reunion-id?apiKey={Uri.EscapeDataString(_options.ApiKey)}&bgaName={Uri.EscapeDataString(bgaName)}";
        using var response = await http.GetAsync(query, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PlayerLookupResponse>(ct);
        return body?.ReunionUserId;
    }

    private sealed record PlayerLookupResponse(string BgaName, string ReunionUserId);
}
