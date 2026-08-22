using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class KeycloakUserNotFoundException(string keycloakId)
    : Exception($"No Keycloak account found for id '{keycloakId}'.")
{
    public string KeycloakId { get; } = keycloakId;
}

// Resolves an arbitrary target Keycloak id to the internal Guid, lazily provisioning
// the local Users row like CurrentUserAccessor does for the current principal — but
// since this id isn't authenticated in the current request, its existence has to be
// verified against Keycloak before a local row is created for it.
public class UserProvisioningService(OwnershipDbContext db, IKeycloakAdminClient keycloak, TimeProvider time)
{
    public async Task<Guid> ResolveOrCreateAsync(string keycloakId, CancellationToken ct)
    {
        var existing = await db.Users
            .Where(u => u.KeycloakId == keycloakId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is { } id) return id;

        _ = await keycloak.GetByIdAsync(keycloakId, ct)
            ?? throw new KeycloakUserNotFoundException(keycloakId);

        var user = new User
        {
            Id = Guid.NewGuid(),
            KeycloakId = keycloakId,
            CreatedAt = time.GetUtcNow(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }
}
