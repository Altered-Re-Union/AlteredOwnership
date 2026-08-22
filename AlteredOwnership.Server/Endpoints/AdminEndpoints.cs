using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Endpoints;

public record AdminUserSearchResult(string KeycloakId, string? Email, string? Pseudo);

public record SetUserRoleRequest(UserRole Role);

public record GiveCardRequest(string CardReference, int Quantity, string AcquiredFrom, List<string> KeycloakUserIds);

public record GiveRandomUniqueRequest(string? Set, int Quantity, string AcquiredFrom, List<string> KeycloakUserIds);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin").RequireAuthorization(AuthConstants.AdminPolicy);

        // The admin page's own way of checking "am I allowed here" — every other
        // route here is independently protected regardless of what this returns.
        group.MapGet("ping", () => Results.NoContent());

        group.MapGet("users/search", async (
            string term,
            IKeycloakAdminClient keycloak,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(term))
                return Results.Ok(Array.Empty<AdminUserSearchResult>());

            var users = await keycloak.SearchAsync(term, ct);
            return Results.Ok(users.Select(u => new AdminUserSearchResult(u.Id, u.Email, u.Pseudo)));
        });

        // Current admins, resolved to display info for the admin page's management UI.
        group.MapGet("admins", async (
            OwnershipDbContext db,
            IKeycloakAdminClient keycloak,
            CancellationToken ct) =>
        {
            var adminIds = await db.Users
                .Where(u => u.Role == UserRole.Admin)
                .Select(u => u.KeycloakId)
                .ToListAsync(ct);

            var admins = new List<AdminUserSearchResult>();
            foreach (var keycloakId in adminIds)
            {
                var kcUser = await keycloak.GetByIdAsync(keycloakId, ct);
                admins.Add(new AdminUserSearchResult(keycloakId, kcUser?.Email, kcUser?.Pseudo));
            }
            return Results.Ok(admins);
        });

        group.MapPost("users/{keycloakId}/role", async (
            string keycloakId,
            SetUserRoleRequest request,
            UserProvisioningService provisioning,
            OwnershipDbContext db,
            CancellationToken ct) =>
        {
            try
            {
                var userId = await provisioning.ResolveOrCreateAsync(keycloakId, ct);
                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

                if (request.Role == UserRole.Player && user.Role == UserRole.Admin)
                {
                    var adminCount = await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct);
                    if (adminCount <= 1)
                        return Results.Text("Cannot remove the last remaining admin.", "text/plain", null,
                            StatusCodes.Status409Conflict);
                }

                user.Role = request.Role;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
            catch (KeycloakUserNotFoundException)
            {
                return Results.NotFound("No Keycloak account found for this id.");
            }
        });

        // Both reward routes below are all-or-nothing for the whole request: every
        // target is resolved/verified against Keycloak up front, then the grant(s) run
        // inside one transaction (RewardService), so a mid-batch failure — a conflict,
        // exhausted stock, or a crash — leaves no ambiguity about who got what: either
        // the whole request went through, or none of it did.

        group.MapPost("rewards/card", async (
            GiveCardRequest request,
            UserProvisioningService provisioning,
            RewardService rewards,
            CancellationToken ct) =>
        {
            var targetIds = request.KeycloakUserIds.Distinct().ToList();
            if (targetIds.Count == 0)
                return Results.BadRequest("At least one target is required.");
            if (request.Quantity < 1)
                return Results.BadRequest("Quantity must be at least 1.");
            if (CardReferenceParser.IsUnique(request.CardReference) && request.Quantity != 1)
                return Results.BadRequest("A unique card can only be given with quantity 1.");

            var userIds = new List<Guid>();
            foreach (var keycloakId in targetIds)
            {
                try
                {
                    userIds.Add(await provisioning.ResolveOrCreateAsync(keycloakId, ct));
                }
                catch (KeycloakUserNotFoundException ex)
                {
                    return Results.Text($"{ex.Message} Nothing was granted.", "text/plain", null,
                        StatusCodes.Status404NotFound);
                }
            }

            try
            {
                await rewards.RewardManyAsync(
                    userIds.Select(id => (id, request.CardReference, request.Quantity)).ToList(),
                    request.AcquiredFrom, ct);
            }
            catch (DuplicateUniquesException ex)
            {
                return Results.Text(
                    $"Already owned by one of the targets: {string.Join(", ", ex.References)}. Nothing was granted.",
                    "text/plain", null, StatusCodes.Status409Conflict);
            }
            catch (ConflictingUniquesException ex)
            {
                return Results.Text(
                    $"Already owned by another player: {string.Join(", ", ex.References)}. Nothing was granted.",
                    "text/plain", null, StatusCodes.Status409Conflict);
            }

            return Results.NoContent();
        });

        group.MapPost("rewards/random-unique", async (
            GiveRandomUniqueRequest request,
            UserProvisioningService provisioning,
            RewardService rewards,
            CancellationToken ct) =>
        {
            var targetIds = request.KeycloakUserIds.Distinct().ToList();
            if (targetIds.Count == 0)
                return Results.BadRequest("At least one target is required.");
            if (request.Quantity < 1)
                return Results.BadRequest("Quantity must be at least 1.");

            var userIds = new List<Guid>();
            var keycloakIdByUserId = new Dictionary<Guid, string>();
            foreach (var keycloakId in targetIds)
            {
                try
                {
                    var userId = await provisioning.ResolveOrCreateAsync(keycloakId, ct);
                    userIds.Add(userId);
                    keycloakIdByUserId[userId] = keycloakId;
                }
                catch (KeycloakUserNotFoundException ex)
                {
                    return Results.Text($"{ex.Message} Nothing was granted.", "text/plain", null,
                        StatusCodes.Status404NotFound);
                }
            }

            Dictionary<Guid, List<string>> granted;
            try
            {
                granted = await rewards.RewardRandomUniquesAsync(
                    userIds, request.Set, request.Quantity, request.AcquiredFrom, ct);
            }
            catch (NoUniqueStockAvailableException ex)
            {
                return Results.Text($"{ex.Message} Nothing was granted.", "text/plain", null,
                    StatusCodes.Status409Conflict);
            }
            catch (ConflictingUniquesException ex)
            {
                return Results.Text($"{ex.Message} Nothing was granted.", "text/plain", null,
                    StatusCodes.Status409Conflict);
            }

            return Results.Ok(granted.ToDictionary(kv => keycloakIdByUserId[kv.Key], kv => kv.Value));
        });

        return routes;
    }
}
