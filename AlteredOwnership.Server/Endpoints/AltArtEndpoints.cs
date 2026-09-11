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

// A token to inject into a caller's deck view. Unlike OwnershipCheckItem (Lines),
// callers have no other way to know what this card actually is -- a token is never
// part of the input deck, so there's no sibling entry a caller could otherwise clone
// metadata from (see altered-bga-api's DeckOwnershipRewriteHandler.InjectTokens, which
// used to clone an unrelated deck card and got everything but Reference wrong).
// CardType/Faction/Rarity/MainCost come straight from CardArtCatalog; per-illustration
// gameplay stats (power values, illustrator) aren't tracked there, same as every other
// non-CHARACTER group callers already render with those fields null.
public record TokenArtItem(
    string Reference, int Quantity, string? Name, string CardType,
    string Faction, string Rarity, int? MainCost);

// Response for apply-to-deck. Lines[i] corresponds exactly to the i-th item of the
// request body — a single input line can expand into several output lines when its
// multi-art group's exemplaires are split across more than one chosen illustration, so
// a flat list can't preserve this correlation. Tokens are never part of the input deck
// (they're created by other cards' effects, not owned/played copies), so they're
// surfaced separately rather than appended to some arbitrary line.
public record ApplyToDeckResponse(
    IReadOnlyList<IReadOnlyList<OwnershipCheckItem>> Lines, IReadOnlyList<TokenArtItem> Tokens);

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
            string? locale,
            CurrentUserAccessor currentUser,
            AltArtService altArts,
            CancellationToken ct) =>
        {
            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            return Results.Ok(await altArts.ApplyToDeckAsync(userId, deck, loc, ct));
        })
        .RequireAuthorization(AuthConstants.ReadPolicy)
        // Read-only transformation of caller-supplied data, no state change — same
        // reasoning as verify-ownership.
        .DisableAntiforgery();

        return routes;
    }
}
