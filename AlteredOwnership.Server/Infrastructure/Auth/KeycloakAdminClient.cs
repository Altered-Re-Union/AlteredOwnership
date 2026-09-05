using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Infrastructure.Auth;

public interface IKeycloakAdminClient
{
    Task<IReadOnlyList<KeycloakUserDto>> SearchByPseudoAsync(string pseudo, CancellationToken ct);
    Task<IReadOnlyList<KeycloakUserDto>> SearchByEmailAsync(string email, CancellationToken ct);
    Task<IReadOnlyList<KeycloakUserDto>> SearchAsync(string term, CancellationToken ct);
    Task<KeycloakUserDto?> GetByIdAsync(string keycloakId, CancellationToken ct);
}

// Talks to Keycloak's Admin REST API using the players-readonly-svc service account
// (client_credentials grant, "view-users" role only — no write access). BaseAddress
// is ExternalHosts:AuthBase (the bare Keycloak host), not Keycloak:Authority, because
// the admin API lives outside the realm's OIDC endpoints.
public sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakAdminOptions> options, ILogger<KeycloakAdminClient> logger)
    : IKeycloakAdminClient
{
    private readonly KeycloakAdminOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // Matches System.Net.Http.Json's ReadFromJsonAsync default ("web" case-insensitive
    // matching) — needed here because logging the raw body means deserializing by hand.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // "pseudo" is a custom user attribute (see realm-export.json), not the Keycloak
    // username, so this goes through the admin API's attribute query ("q=key:value")
    // rather than the username filter. An infix/contains match (wrapping the value in
    // *…*) was tried here but has been observed returning nothing against our actual
    // 26.5.0 realm; a plain exact match on the same "q" filter does work, so the admin
    // search box requires the full pseudo rather than a partial one. Logged at
    // Information so the exact request/result is visible without reproducing blind.
    public async Task<IReadOnlyList<KeycloakUserDto>> SearchByPseudoAsync(string pseudo, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        var query = Uri.EscapeDataString($"pseudo:{pseudo}");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"admin/realms/{_options.Realm}/users?q={query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogInformation(
            "Keycloak pseudo search: GET {RequestUri} -> {StatusCode} {Body}",
            request.RequestUri, (int)response.StatusCode, body);
        response.EnsureSuccessStatusCode();

        return JsonSerializer.Deserialize<List<KeycloakUserDto>>(body, JsonOptions) ?? [];
    }

    // Keycloak's "email" query param does an infix match unless exact=true is set.
    // Logged the same way as the pseudo search above, for a direct side-by-side
    // comparison of what each query actually sent/received.
    public async Task<IReadOnlyList<KeycloakUserDto>> SearchByEmailAsync(string email, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        var query = Uri.EscapeDataString(email);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"admin/realms/{_options.Realm}/users?email={query}&exact=false");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogInformation(
            "Keycloak email search: GET {RequestUri} -> {StatusCode} {Body}",
            request.RequestUri, (int)response.StatusCode, body);
        response.EnsureSuccessStatusCode();

        return JsonSerializer.Deserialize<List<KeycloakUserDto>>(body, JsonOptions) ?? [];
    }

    // "pseudo" is a custom attribute Keycloak's built-in username/email/name search
    // doesn't cover, so admin lookup by email or pseudo has to combine two separate
    // queries rather than a single built-in "search" param.
    public async Task<IReadOnlyList<KeycloakUserDto>> SearchAsync(string term, CancellationToken ct)
    {
        var byPseudo = SearchByPseudoAsync(term, ct);
        var byEmail = SearchByEmailAsync(term, ct);
        await Task.WhenAll(byPseudo, byEmail);

        return byPseudo.Result
            .Concat(byEmail.Result)
            .DistinctBy(u => u.Id)
            .ToList();
    }

    public async Task<KeycloakUserDto?> GetByIdAsync(string keycloakId, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"admin/realms/{_options.Realm}/users/{Uri.EscapeDataString(keycloakId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<KeycloakUserDto>(ct);
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
