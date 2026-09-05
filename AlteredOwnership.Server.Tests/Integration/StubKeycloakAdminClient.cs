using AlteredOwnership.Server.Infrastructure.Auth;

namespace AlteredOwnership.Server.Tests.Integration;

// Configurable test double for IKeycloakAdminClient — the real client hits a live
// preprod Keycloak host tests have no credentials for.
public class StubKeycloakAdminClient : IKeycloakAdminClient
{
    private readonly Dictionary<string, KeycloakUserDto> _users = new();

    public StubKeycloakAdminClient KnownUser(string keycloakId, string? email = null, string? pseudo = null)
    {
        _users[keycloakId] = new KeycloakUserDto
        {
            Id = keycloakId,
            Email = email,
            Attributes = pseudo is null ? null : new Dictionary<string, List<string>> { ["pseudo"] = [pseudo] },
        };
        return this;
    }

    public Task<KeycloakUserDto?> GetByIdAsync(string keycloakId, CancellationToken ct) =>
        Task.FromResult(_users.GetValueOrDefault(keycloakId));

    // Mirrors the real KeycloakAdminClient: pseudo search is an exact match (its infix
    // form doesn't work against the real realm), unlike email search below.
    public Task<IReadOnlyList<KeycloakUserDto>> SearchByPseudoAsync(string pseudo, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KeycloakUserDto>>(_users.Values
            .Where(u => u.Pseudo is not null && u.Pseudo.Equals(pseudo, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task<IReadOnlyList<KeycloakUserDto>> SearchByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KeycloakUserDto>>(_users.Values
            .Where(u => u.Email is not null && u.Email.Contains(email, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public async Task<IReadOnlyList<KeycloakUserDto>> SearchAsync(string term, CancellationToken ct) =>
        (await SearchByPseudoAsync(term, ct)).Concat(await SearchByEmailAsync(term, ct)).DistinctBy(u => u.Id).ToList();
}
