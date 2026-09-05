using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Domain.Boosters;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Endpoints;

public record AdminUserSearchResult(string KeycloakId, string? Email, string? Pseudo);

public record SetUserRoleRequest(UserRole Role);

public record BoosterTypeResponse(string Key, string Name);

public record CardGrant(string CardReference, int Quantity);

public record BoosterGrant(string BoosterTypeKey, int Quantity);

public record RewardBatchRequest(
    List<string> KeycloakUserIds, string AcquiredFrom, List<CardGrant> Cards, List<BoosterGrant> Boosters);

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

        group.MapGet("booster-types", () =>
            Results.Ok(BoosterCatalog.All.Select(b => new BoosterTypeResponse(b.Key, b.Name))));

        // All-or-nothing for the whole request: every target is resolved/verified
        // against Keycloak up front, then every grant runs inside one transaction
        // (RewardService), so a mid-batch failure — a conflict or a crash — leaves no
        // ambiguity about who got what: either the whole request went through, or
        // none of it did. One RewardEvent per target, holding every card and booster
        // grant from this request together (see RewardService.RewardBatchAsync).
        group.MapPost("rewards", async (
            RewardBatchRequest request,
            UserProvisioningService provisioning,
            RewardService rewards,
            OwnershipDbContext db,
            CancellationToken ct) =>
        {
            var targetIds = request.KeycloakUserIds.Distinct().ToList();
            if (targetIds.Count == 0)
                return Results.BadRequest("At least one target is required.");
            if (request.Cards.Count == 0 && request.Boosters.Count == 0)
                return Results.BadRequest("At least one card or booster grant is required.");
            if (request.Cards.Any(c => c.Quantity < 1) || request.Boosters.Any(b => b.Quantity < 1))
                return Results.BadRequest("Quantity must be at least 1.");
            if (request.Cards.Any(c => CardReferenceParser.IsUnique(c.CardReference) && c.Quantity != 1))
                return Results.BadRequest("A unique card can only be given with quantity 1.");
            var unknownBoosterType = request.Boosters.FirstOrDefault(b => BoosterCatalog.Find(b.BoosterTypeKey) is null);
            if (unknownBoosterType is not null)
                return Results.BadRequest($"Unknown booster type '{unknownBoosterType.BoosterTypeKey}'.");

            // Every given reference must be a real printing — either a unique (stock
            // ledger) or a non-unique one (art catalog, which covers base art too, not
            // just alternates) — otherwise a typo would silently create an unownable
            // card instead of failing loudly.
            var cardReferences = request.Cards.Select(c => c.CardReference).Distinct().ToList();
            if (cardReferences.Count > 0)
            {
                var knownReferences = await db.UniqueCardStock
                    .Where(u => cardReferences.Contains(u.CardReference))
                    .Select(u => u.CardReference)
                    .Union(db.CardArtCatalog
                        .Where(c => cardReferences.Contains(c.Reference))
                        .Select(c => c.Reference))
                    .ToListAsync(ct);
                var unknownReferences = cardReferences.Except(knownReferences).ToList();
                if (unknownReferences.Count > 0)
                    return Results.BadRequest($"Unknown card reference(s): {string.Join(", ", unknownReferences)}.");
            }

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
                await rewards.RewardBatchAsync(
                    userIds,
                    request.Cards.Select(c => (c.CardReference, c.Quantity)).ToList(),
                    request.Boosters.Select(b => (b.BoosterTypeKey, b.Quantity)).ToList(),
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

        return routes;
    }
}
