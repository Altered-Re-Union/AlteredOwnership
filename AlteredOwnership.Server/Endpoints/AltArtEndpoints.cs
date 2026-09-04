using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;

namespace AlteredOwnership.Server.Endpoints;

// One deduplicated alt-art family (FamilyId, Faction, Rarity) — Reference is its
// representative printing (earliest non-promo one).
public record AltArtFamilyResponse(
    int FamilyId, string Faction, string Rarity, string Reference,
    string? Name, string CardType, int? MainCost);

// Identifies one alt-art group — the same (FamilyId, Faction, Rarity) triple used
// throughout this feature, no synthetic id.
public record AltArtGroupKey(int FamilyId, string Faction, string Rarity);

// Resolves one caller-supplied Reference to its multi-art group, for callers (the
// deckbuilder) that only know a printing's exact Reference and not its
// (FamilyId, Faction, Rarity) group key. References that aren't in the catalog, or
// whose group has only one known illustration, are simply omitted from the response.
public record AltArtReferenceGroup(string Reference, int FamilyId, string Faction, string Rarity);

// One known printing within a group. OwnedQuantity is null when this printing is
// owned in unlimited quantity by every player (see AltArtRules.IsInfinite). SortOrder
// is CardsData's own chronological print id — callers needing "the standard/leftmost
// art" (the group's default) sort Options by it ascending rather than relying on the
// player's current slot choices, which may all be explicit.
public record AltArtOption(string Reference, string Set, bool IsPromo, int? OwnedQuantity, int SortOrder);

// The resolved art for one copy ("exemplaire") slot — either the player's explicit
// choice, or (IsExplicitChoice = false) the group's default art.
public record AltArtSlotChoice(int SlotIndex, string Reference, bool IsExplicitChoice);

public record AltArtOptionsResponse(
    int FamilyId, string Faction, string Rarity,
    IReadOnlyList<AltArtOption> Options, IReadOnlyList<AltArtSlotChoice> Slots);

// Sets every slot's art for one group in a single call — index 0 is slot 1, etc.
// SlotReferences.Count must equal the group's slot count (1 for HERO, else 3). A null
// entry resets that slot back to the group's default art.
public record SetAltArtPreferenceRequest(
    int FamilyId, string Faction, string Rarity, IReadOnlyList<string?> SlotReferences);

// Response for apply-to-deck. Lines[i] corresponds exactly to the i-th item of the
// request body — a single input line can expand into several output lines when its
// multi-art group's exemplaires are split across more than one chosen illustration, so
// a flat list can't preserve this correlation. Tokens are never part of the input deck
// (they're created by other cards' effects, not owned/played copies), so they're
// surfaced separately rather than appended to some arbitrary line.
public record ApplyToDeckResponse(
    IReadOnlyList<IReadOnlyList<OwnershipCheckItem>> Lines, IReadOnlyList<OwnershipCheckItem> Tokens);

public static class AltArtEndpoints
{
    public static IEndpointRouteBuilder MapAltArtEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/alt-arts");

        group.MapGet("families", async (
            AltArtFamilyQuery query,
            string? locale,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            return Results.Ok(await altArts.GetFamiliesAsync(query, loc, ct));
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        group.MapPost("resolve-references", async (
            List<string> references,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            return Results.Ok(await altArts.ResolveReferencesAsync(references, ct));
        })
        .RequireAuthorization(AuthConstants.ReadPolicy)
        .DisableAntiforgery();

        group.MapPost("options", async (
            List<AltArtGroupKey> keys,
            CurrentUserAccessor currentUser,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            return Results.Ok(await altArts.GetOptionsAsync(userId, keys, ct));
        })
        .RequireAuthorization(AuthConstants.ReadPolicy)
        .DisableAntiforgery();

        group.MapPut("preferences", async (
            SetAltArtPreferenceRequest request,
            CurrentUserAccessor currentUser,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            try
            {
                await altArts.SetPreferenceAsync(userId, request, ct);
            }
            catch (InvalidAltArtRequestException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (AltArtSlotShortfallException ex)
            {
                return Results.Conflict(ex.Shortfalls);
            }

            return Results.NoContent();
        })
        // Lax like boosters-open: this must be callable by third-party Bearer clients
        // (e.g. alteredcore-website's server-side proxy), which WritePolicy — cookie-only
        // by design — would always reject with 401 regardless of token scope.
        .RequireAuthorization(AuthConstants.ReadPolicy);

        group.MapPost("apply-to-deck", async (
            List<OwnershipCheckItem> deck,
            CurrentUserAccessor currentUser,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            return Results.Ok(await altArts.ApplyToDeckAsync(userId, deck, ct));
        })
        .RequireAuthorization(AuthConstants.ReadPolicy)
        // Read-only transformation of caller-supplied data, no state change — same
        // reasoning as verify-ownership.
        .DisableAntiforgery();

        return routes;
    }
}
