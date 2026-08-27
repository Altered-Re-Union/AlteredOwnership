using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Domain.Boosters;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Endpoints;

public record BoosterInventoryResponse(string BoosterTypeKey, string Name, string? ImagePath, int Quantity);

public record OpenBoosterRequest(int Quantity = 1);

public record OpenedCardResponse(string CardReference, string? Name, string? ImagePath, bool IsUnique);

public static class BoosterEndpoints
{
    public static IEndpointRouteBuilder MapBoosterEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/boosters");

        group.MapGet("", async (
            CurrentUserAccessor currentUser,
            OwnershipDbContext db,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var inventory = await db.BoosterInventories.Where(b => b.UserId == userId).ToListAsync(ct);

            return Results.Ok(inventory.Select(b =>
            {
                var type = BoosterCatalog.Find(b.BoosterTypeKey);
                return new BoosterInventoryResponse(b.BoosterTypeKey, type?.Name ?? b.BoosterTypeKey, type?.ImagePath, b.Quantity);
            }));
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        // Opens `quantity` boosters of one type in a single action — see
        // BoosterService.OpenAsync for why this is always one event, never one per
        // booster or per card.
        group.MapPost("{boosterTypeKey}/open", async (
            string boosterTypeKey,
            OpenBoosterRequest request,
            string? locale,
            CurrentUserAccessor currentUser,
            BoosterService boosters,
            OwnershipDbContext db,
            CardMetadataBackfiller backfiller,
            CancellationToken ct) =>
        {
            if (request.Quantity < 1)
                return Results.BadRequest("Quantity must be at least 1.");

            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);

            IReadOnlyList<string> cardReferences;
            try
            {
                cardReferences = await boosters.OpenAsync(userId, boosterTypeKey, request.Quantity, ct);
            }
            catch (UnknownBoosterTypeException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (NoBoosterAvailableException ex)
            {
                return Results.Text(ex.Message, "text/plain", null, StatusCodes.Status409Conflict);
            }
            catch (NoUniqueStockAvailableException ex)
            {
                return Results.Text(ex.Message, "text/plain", null, StatusCodes.Status409Conflict);
            }

            // Best-effort, same as the import path: catch up on any drawn reference the
            // catalog doesn't know yet — skipped entirely for uniques (every booster today
            // draws only uniques, which never get a catalog row) so a normal open never pays
            // for the FetchAsync round-trip to cards.alteredcore.org that would otherwise run,
            // fail to find anything, and insert nothing on every single open. Scoped to just
            // the drawn references (not BackfillAsync's whole-collection scan) so this stays
            // correct and cheap if a future booster type ever draws a non-unique.
            var nonUniqueReferences = cardReferences.Where(r => !CardReferenceParser.IsUnique(r)).ToList();
            if (nonUniqueReferences.Count > 0)
                await backfiller.BackfillReferencesForUserAsync(userId, nonUniqueReferences, ct);

            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            var catalog = await db.Cards
                .Where(c => cardReferences.Contains(c.Reference))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Reference, ct);

            var opened = cardReferences.Select(r =>
            {
                var card = catalog.GetValueOrDefault(r);
                return new OpenedCardResponse(
                    r,
                    CardLocalization.Localize(card?.Name, loc),
                    CardLocalization.Localize(card?.ImagePath, loc),
                    CardReferenceParser.IsUnique(r));
            }).ToList();

            return Results.Ok(opened);
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        return routes;
    }
}
