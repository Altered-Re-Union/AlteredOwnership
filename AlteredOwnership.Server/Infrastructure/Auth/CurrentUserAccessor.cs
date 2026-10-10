using System.Security.Claims;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Infrastructure.Auth;

public readonly record struct CurrentUser(Guid Id, AltArtPreferenceMode AltArtPreferenceMode);

// Resolves (and lazily provisions) the internal user from the Keycloak 'sub' claim.
// Personal data stays in the Users table; everything downstream uses the internal Guid Id.
public class CurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    OwnershipDbContext db,
    TimeProvider time)
{
    public async Task<Guid> GetOrProvisionInternalIdAsync(CancellationToken ct) =>
        (await GetOrProvisionAsync(ct)).Id;

    // Same lookup, but also returns the alt-art mode in the same query, for callers that
    // need both (apply-to-deck) and would otherwise spend a second round trip on it.
    public async Task<CurrentUser> GetOrProvisionAsync(CancellationToken ct)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available");

        var keycloakId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Missing 'sub' claim");

        var existing = await db.Users
            .Where(u => u.KeycloakId == keycloakId)
            .Select(u => (CurrentUser?)new CurrentUser(u.Id, u.AltArtPreferenceMode))
            .FirstOrDefaultAsync(ct);

        if (existing is { } found) return found; // null (not default) when the user doesn't exist yet

        var user = new User
        {
            Id = Guid.NewGuid(),
            KeycloakId = keycloakId,
            CreatedAt = time.GetUtcNow(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return new CurrentUser(user.Id, user.AltArtPreferenceMode);
    }
}
