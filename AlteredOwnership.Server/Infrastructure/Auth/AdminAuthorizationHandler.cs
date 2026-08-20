using System.Security.Claims;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Infrastructure.Auth;

public class AdminRequirement : IAuthorizationRequirement;

// Admin status lives on the local Users row, not in any Keycloak/JWT claim, so it
// can't be expressed with the RequireAssertion(ClaimsPrincipal) pattern the other
// policies use — this handler resolves it from the database instead.
public class AdminAuthorizationHandler(OwnershipDbContext db) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var keycloakId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (keycloakId is null) return;

        var role = await db.Users
            .Where(u => u.KeycloakId == keycloakId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync();

        if (role == UserRole.Admin)
            context.Succeed(requirement);
    }
}
