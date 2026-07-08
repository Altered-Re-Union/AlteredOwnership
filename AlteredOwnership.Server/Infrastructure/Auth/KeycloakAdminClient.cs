using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Infrastructure.Auth;

public interface IKeycloakAdminClient
{
    Task<IReadOnlyList<KeycloakUserDto>> SearchByPseudoAsync(string pseudo, CancellationToken ct);
}

// Talks to Keycloak's Admin REST API using the players-readonly-svc service account
// (client_credentials grant, "view-users" role only — no write access). BaseAddress
// is ExternalHosts:AuthBase (the bare Keycloak host), not Keycloak:Authority, because
// the admin API lives outside the realm's OIDC endpoints.
public sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakAdminOptions> options) : IKeycloakAdminClient
{
    private readonly KeycloakAdminOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // "pseudo" is a custom user attribute (see realm-export.json), not the Keycloak
    // username, so this goes through the admin API's attribute query ("q=key:value")
    // rather than the username filter. Wrapping the value in *…* asks for an
    // infix/contains match (LIKE '%value%') instead of Keycloak's default prefix match —
    // fixed to actually respect this on custom attributes as of Keycloak 26.3
    // (github.com/keycloak/keycloak/issues/39915; we run 26.6.4).
    public async Task<IReadOnlyList<KeycloakUserDto>> SearchByPseudoAsync(string pseudo, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        var query = Uri.EscapeDataString($"pseudo:*{pseudo}*");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"admin/realms/{_options.Realm}/users?q={query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<KeycloakUserDto>>(ct) ?? [];
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have refreshed the token while we waited.
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            });

            using var response = await http.PostAsync($"realms/{_options.Realm}/protocol/openid-connect/token", form, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Empty token response from Keycloak.");

            _cachedToken = token.AccessToken;
            // Refresh a bit before the real expiry so a slow request never rides an
            // already-expired token.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 10);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}

// Subset of Keycloak's UserRepresentation. Field names match the admin API's
// camelCase JSON case-insensitively (System.Net.Http.Json web defaults).
public sealed record KeycloakUserDto
{
    public string Id { get; init; } = default!;
    public string? Email { get; init; }
    public Dictionary<string, List<string>>? Attributes { get; init; }

    // "pseudo" lives in the attributes bag, not as a top-level field.
    public string? Pseudo => Attributes?.TryGetValue("pseudo", out var values) == true ? values.FirstOrDefault() : null;
}
