using System.Security.Claims;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Infrastructure.Auth;

// Resolves (and lazily provisions) the internal user from the Keycloak 'sub' claim.
// Personal data stays in the Users table; everything downstream uses the internal Guid Id.
public class CurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    OwnershipDbContext db,
    TimeProvider time)
{
    public async Task<Guid> GetOrProvisionInternalIdAsync(CancellationToken ct)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available");

        var keycloakId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Missing 'sub' claim");

        var existing = await db.Users
            .Where(u => u.KeycloakId == keycloakId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is { } id) return id;

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
